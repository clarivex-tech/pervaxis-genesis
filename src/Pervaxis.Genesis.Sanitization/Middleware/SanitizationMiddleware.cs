/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Sanitization.Diagnostics;
using Pervaxis.Genesis.Sanitization.Options;
using Pervaxis.Genesis.Sanitization.Services;

namespace Pervaxis.Genesis.Sanitization.Middleware;

/// <summary>
/// ASP.NET Core middleware that auto-sanitizes all string fields in POST/PUT/PATCH request bodies.
/// Off by default — opt-in via <see cref="SanitizationOptions.EnableMiddleware"/>.
/// </summary>
internal sealed class SanitizationMiddleware
{
    private static readonly HashSet<string> SanitizableMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH"
    };

    private readonly RequestDelegate _next;
    private readonly GenesisSanitizer _sanitizer;
    private readonly SanitizationOptions _options;
    private readonly ILogger<SanitizationMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizationMiddleware"/> class.
    /// </summary>
    public SanitizationMiddleware(
        RequestDelegate next,
        GenesisSanitizer sanitizer,
        IOptions<SanitizationOptions> options,
        ILogger<SanitizationMiddleware> logger)
    {
        _next = next;
        _sanitizer = sanitizer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request, optionally sanitizing string fields in the body.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Pass through on JSON parse failures — middleware must not break non-JSON requests.")]
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableMiddleware)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!SanitizableMethods.Contains(context.Request.Method))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (IsRouteExcluded(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!IsJsonContentType(context.Request.ContentType))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body))
            {
                context.Request.Body.Position = 0;
                await _next(context).ConfigureAwait(false);
                return;
            }

            var jsonNode = JsonNode.Parse(body);
            if (jsonNode is null)
            {
                context.Request.Body.Position = 0;
                await _next(context).ConfigureAwait(false);
                return;
            }

            var fieldsModified = SanitizeJsonNode(jsonNode);

            if (fieldsModified > 0)
            {
                var sanitizedBody = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                var bytes = Encoding.UTF8.GetBytes(sanitizedBody);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentLength = bytes.Length;

                SanitizationLogMessages.LogMiddlewareSanitized(
                    _logger, context.Request.Path, context.Request.Method, fieldsModified);
            }
            else
            {
                context.Request.Body.Position = 0;
            }
        }
        catch (JsonException)
        {
            // Invalid JSON — pass through unmodified
            context.Request.Body.Position = 0;
        }
        catch (Exception)
        {
            // Any other parsing error — pass through unmodified
            context.Request.Body.Position = 0;
        }

        await _next(context).ConfigureAwait(false);
    }

    private int SanitizeJsonNode(JsonNode node)
    {
        var count = 0;

        if (node is JsonObject obj)
        {
            var keys = obj.Select(p => p.Key).ToList();
            foreach (var key in keys)
            {
                var value = obj[key];
                if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var str))
                {
                    var sanitized = _sanitizer.SanitizeCore(str, _options.DefaultProfile, "middleware");
                    if (!string.Equals(str, sanitized, StringComparison.Ordinal))
                    {
                        obj[key] = sanitized;
                        count++;
                    }
                }
                else if (value is JsonObject or JsonArray)
                {
                    count += SanitizeJsonNode(value!);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var element = array[i];
                if (element is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var str))
                {
                    var sanitized = _sanitizer.SanitizeCore(str, _options.DefaultProfile, "middleware");
                    if (!string.Equals(str, sanitized, StringComparison.Ordinal))
                    {
                        array[i] = sanitized;
                        count++;
                    }
                }
                else if (element is JsonObject or JsonArray)
                {
                    count += SanitizeJsonNode(element!);
                }
            }
        }

        return count;
    }

    private bool IsRouteExcluded(PathString path)
    {
        foreach (var pattern in _options.MiddlewareExcludedRoutes)
        {
            if (pattern.EndsWith('*'))
            {
                var prefix = pattern[..^1];
                if (path.HasValue && path.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (path.HasValue && path.Value.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase);
    }
}
