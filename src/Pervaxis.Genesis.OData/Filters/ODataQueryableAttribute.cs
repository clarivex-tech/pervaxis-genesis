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

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Genesis.OData.Options;

namespace Pervaxis.Genesis.OData.Filters;

/// <summary>
/// Enables OData query processing on a controller action.
/// Per-endpoint configuration overrides global ODataOptions settings.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ODataQueryableAttribute : Attribute, IFilterFactory
{
    /// <summary>
    /// Gets or sets the per-endpoint $top maximum override.
    /// 0 means use global ODataOptions.MaxTop. Valid range when non-zero: 1-10000.
    /// </summary>
    public int MaxTop { get; set; }

    /// <summary>
    /// Gets or sets the per-endpoint $expand depth override.
    /// -1 means use global ODataOptions.MaxExpandDepth. Valid range when non-negative: 0-5.
    /// </summary>
    public int MaxExpandDepth { get; set; } = -1;

    /// <summary>
    /// Gets or sets the per-endpoint allowed query options override.
    /// Default: All (use global setting).
    /// </summary>
    public ODataQueryOptions AllowedQueryOptions { get; set; } = ODataQueryOptions.All;

    /// <inheritdoc/>
    public bool IsReusable => true;

    /// <inheritdoc/>
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetRequiredService<ODataQueryFilter>();
    }
}
