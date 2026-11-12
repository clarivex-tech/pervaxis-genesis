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

using Pervaxis.Core.Abstractions.Genesis;

namespace Pervaxis.Genesis.OData.Options;

/// <summary>
/// Configuration for the Genesis OData module.
/// Bound from the "Genesis:OData" configuration section.
/// </summary>
public sealed class ODataOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the maximum allowed $top value. Default: 100. Range: 1-10000.
    /// </summary>
    public int MaxTop { get; set; } = 100;

    /// <summary>
    /// Gets or sets the default page size when $top not specified. Default: 20. Range: 1-MaxTop.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum $expand nesting depth. Default: 2. Range: 0-5.
    /// </summary>
    public int MaxExpandDepth { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum $filter logical operator nesting depth. Default: 3. Range: 1-10.
    /// </summary>
    public int MaxFilterDepth { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum properties in $orderby. Default: 3. Range: 1-10.
    /// </summary>
    public int MaxOrderByProperties { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether to enable tenant-scoped query isolation. Default: true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the $count query option is permitted. Default: true.
    /// </summary>
    public bool EnableCount { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum query complexity score. Default: 50. Range: 1-200.
    /// </summary>
    public int MaxQueryComplexityScore { get; set; } = 50;

    /// <summary>
    /// Gets or sets the allowed query options (flags). Default: All.
    /// </summary>
    public ODataQueryOptions AllowedQueryOptions { get; set; } = ODataQueryOptions.All;

    /// <inheritdoc/>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (MaxTop is < 1 or > 10000)
        {
            return false;
        }

        if (DefaultPageSize < 1 || DefaultPageSize > MaxTop)
        {
            return false;
        }

        if (MaxExpandDepth is < 0 or > 5)
        {
            return false;
        }

        if (MaxFilterDepth is < 1 or > 10)
        {
            return false;
        }

        if (MaxOrderByProperties is < 1 or > 10)
        {
            return false;
        }

        if (MaxQueryComplexityScore is < 1 or > 200)
        {
            return false;
        }

        return true;
    }
}
