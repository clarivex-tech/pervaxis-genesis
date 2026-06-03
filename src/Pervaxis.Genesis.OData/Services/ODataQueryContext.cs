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

using Pervaxis.Genesis.OData.Options;

namespace Pervaxis.Genesis.OData.Services;

/// <summary>
/// Per-request context containing parsed OData query options and computed metadata.
/// </summary>
public sealed class ODataQueryContext
{
    /// <summary>Raw $filter string (null if not provided).</summary>
    public string? RawFilter { get; init; }

    /// <summary>Raw $orderby string (null if not provided).</summary>
    public string? RawOrderBy { get; init; }

    /// <summary>List of $select property names (empty if not provided).</summary>
    public IReadOnlyList<string> SelectProperties { get; init; } = [];

    /// <summary>List of $expand property names (empty if not provided).</summary>
    public IReadOnlyList<string> ExpandProperties { get; init; } = [];

    /// <summary>Effective $top value (client-provided or DefaultPageSize).</summary>
    public int EffectiveTop { get; init; }

    /// <summary>$skip value (0 if not provided).</summary>
    public int Skip { get; init; }

    /// <summary>Whether $count=true was requested.</summary>
    public bool CountRequested { get; init; }

    /// <summary>Number of filter conditions (for complexity scoring).</summary>
    public int FilterConditionCount { get; init; }

    /// <summary>Maximum $expand nesting depth (for complexity scoring).</summary>
    public int ExpandDepth { get; init; }

    /// <summary>Number of $orderby properties (for complexity scoring).</summary>
    public int OrderByPropertyCount { get; init; }

    /// <summary>Number of $select properties (for complexity scoring).</summary>
    public int SelectPropertyCount { get; init; }

    /// <summary>Filter nesting depth (for validation).</summary>
    public int FilterDepth { get; init; }

    /// <summary>Computed complexity score.</summary>
    public int ComplexityScore { get; set; }

    /// <summary>Set of query options used in this request.</summary>
    public ODataQueryOptions UsedOptions { get; init; }

    /// <summary>The HTTP request path.</summary>
    public string RequestPath { get; init; } = string.Empty;

    /// <summary>The raw query string.</summary>
    public string RawQueryString { get; init; } = string.Empty;
}
