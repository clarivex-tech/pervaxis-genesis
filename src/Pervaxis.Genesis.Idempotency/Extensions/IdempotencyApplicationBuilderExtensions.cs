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

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Idempotency.Middleware;
using Pervaxis.Genesis.Idempotency.Options;

namespace Pervaxis.Genesis.Idempotency.Extensions;

/// <summary>
/// Extension methods for configuring idempotency middleware in the ASP.NET Core pipeline.
/// </summary>
public static class IdempotencyApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the idempotency middleware to the request pipeline.
    /// The middleware matches requests against configured route patterns and HTTP methods.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">Action to configure middleware options (route patterns, HTTP methods).</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseGenesisIdempotency(
        this IApplicationBuilder app,
        Action<IdempotencyMiddlewareOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new IdempotencyMiddlewareOptions();
        configure(options);

        app.ApplicationServices.GetRequiredService<IOptions<IdempotencyMiddlewareOptions>>();

        // Register the configured options
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options);

        app.UseMiddleware<IdempotencyMiddleware>(optionsWrapper);
        return app;
    }
}
