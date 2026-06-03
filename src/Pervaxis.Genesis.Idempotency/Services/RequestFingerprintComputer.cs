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

using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pervaxis.Genesis.Idempotency.Services;

/// <summary>
/// Computes a deterministic fingerprint from HTTP method, route template, and body hash.
/// Format: "{METHOD}|{routeTemplate}|{SHA256(body)}"
/// </summary>
internal sealed class RequestFingerprintComputer : IRequestFingerprintComputer
{
    /// <inheritdoc/>
    public async Task<string> ComputeAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var method = context.Request.Method;
        var routeTemplate = GetRouteTemplate(context);
        var bodyHash = await ComputeBodyHashAsync(context.Request, cancellationToken);

        return $"{method}|{routeTemplate}|{bodyHash}";
    }

    private static string GetRouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is RouteEndpoint routeEndpoint)
        {
            return routeEndpoint.RoutePattern.RawText ?? context.Request.Path.Value ?? string.Empty;
        }

        return context.Request.Path.Value ?? string.Empty;
    }

    private static async Task<string> ComputeBodyHashAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        // Enable buffering so the body can be read multiple times
        request.EnableBuffering();

        try
        {
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(request.Body, cancellationToken);
            return Convert.ToHexStringLower(hashBytes);
        }
        finally
        {
            // Reset the stream position so downstream handlers can read the body
            request.Body.Position = 0;
        }
    }
}
