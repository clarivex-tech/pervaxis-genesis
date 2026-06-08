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
using Pervaxis.Genesis.Sanitization.Middleware;

namespace Pervaxis.Genesis.Sanitization.Extensions;

/// <summary>
/// Extension methods for adding Genesis Sanitization middleware to the request pipeline.
/// </summary>
public static class SanitizationApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the Genesis Sanitization middleware to the request pipeline.
    /// The middleware auto-sanitizes string fields in POST/PUT/PATCH request bodies
    /// when <c>SanitizationOptions.EnableMiddleware</c> is true.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseGenesisSanitization(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<SanitizationMiddleware>();
        return app;
    }
}
