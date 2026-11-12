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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Idempotency.Options;

namespace Pervaxis.Genesis.Idempotency.Middleware;

/// <summary>
/// Middleware that applies idempotency processing to requests matching
/// configured route patterns and HTTP methods.
/// Works in conjunction with the <see cref="Filters.IdempotencyActionFilter"/>.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IdempotencyMiddlewareOptions _middlewareOptions;
    private readonly IdempotencyOptions _idempotencyOptions;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly List<RouteTemplate> _routeTemplates;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyMiddleware"/> class.
    /// </summary>
    public IdempotencyMiddleware(
        RequestDelegate next,
        IOptions<IdempotencyMiddlewareOptions> middlewareOptions,
        IOptions<IdempotencyOptions> idempotencyOptions,
        ILogger<IdempotencyMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(middlewareOptions);
        ArgumentNullException.ThrowIfNull(idempotencyOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _middlewareOptions = middlewareOptions.Value;
        _idempotencyOptions = idempotencyOptions.Value;
        _logger = logger;
        _routeTemplates = _middlewareOptions.RoutePatterns
            .Select(p => TemplateParser.Parse(p))
            .ToList();
    }

    /// <summary>
    /// Processes the HTTP request through the idempotency middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ShouldApplyIdempotency(context))
        {
            await _next(context);
            return;
        }

        // Check if header is present — if not, this is a non-idempotent request on a matched route
        var headerValues = context.Request.Headers[_idempotencyOptions.HeaderName];
        if (headerValues.Count == 0)
        {
            // Middleware mode: header is optional (unlike attribute mode where it's required)
            await _next(context);
            return;
        }

        // The actual idempotency logic is handled by IdempotencyActionFilter
        // The middleware's role is just route matching and ensuring the filter is applied
        await _next(context);
    }

    private bool ShouldApplyIdempotency(HttpContext context)
    {
        // Check HTTP method
        if (!_middlewareOptions.HttpMethods.Contains(context.Request.Method))
        {
            return false;
        }

        // Check route patterns
        if (_routeTemplates.Count == 0)
        {
            return false;
        }

        var requestPath = context.Request.Path.Value ?? string.Empty;

        foreach (var template in _routeTemplates)
        {
            var matcher = new TemplateMatcher(template, new RouteValueDictionary());
            var routeValues = new RouteValueDictionary();

            if (matcher.TryMatch(requestPath, routeValues))
            {
                return true;
            }
        }

        return false;
    }
}
