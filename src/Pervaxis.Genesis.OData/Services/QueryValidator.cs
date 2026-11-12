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
/// Validates OData query options against configured limits.
/// </summary>
public sealed class QueryValidator
{
    private readonly QueryComplexityCalculator _complexityCalculator;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryValidator"/> class.
    /// </summary>
    public QueryValidator(QueryComplexityCalculator complexityCalculator)
    {
        ArgumentNullException.ThrowIfNull(complexityCalculator);
        _complexityCalculator = complexityCalculator;
    }

    /// <summary>
    /// Validates the query context against the effective limits.
    /// </summary>
    /// <param name="queryContext">The parsed query context.</param>
    /// <param name="options">The OData options with global limits.</param>
    /// <param name="allowedOptions">The effective allowed query options (may be per-endpoint override).</param>
    /// <param name="maxTop">The effective max top (may be per-endpoint override).</param>
    /// <param name="maxExpandDepth">The effective max expand depth (may be per-endpoint override).</param>
    /// <returns>The validation result.</returns>
    public QueryValidationResult Validate(
        ODataQueryContext queryContext,
        ODataOptions options,
        ODataQueryOptions allowedOptions,
        int maxTop,
        int maxExpandDepth)
    {
        ArgumentNullException.ThrowIfNull(queryContext);
        ArgumentNullException.ThrowIfNull(options);

        // Check disabled query options
        if (queryContext.RawFilter is not null && !allowedOptions.HasFlag(ODataQueryOptions.Filter))
        {
            return QueryValidationResult.Failed("ODATA_QUERY_OPTION_DISABLED", "The $filter query option is not allowed on this endpoint.");
        }

        if (queryContext.RawOrderBy is not null && !allowedOptions.HasFlag(ODataQueryOptions.OrderBy))
        {
            return QueryValidationResult.Failed("ODATA_QUERY_OPTION_DISABLED", "The $orderby query option is not allowed on this endpoint.");
        }

        if (queryContext.SelectProperties.Count > 0 && !allowedOptions.HasFlag(ODataQueryOptions.Select))
        {
            return QueryValidationResult.Failed("ODATA_QUERY_OPTION_DISABLED", "The $select query option is not allowed on this endpoint.");
        }

        if (queryContext.ExpandProperties.Count > 0 && !allowedOptions.HasFlag(ODataQueryOptions.Expand))
        {
            return QueryValidationResult.Failed("ODATA_QUERY_OPTION_DISABLED", "The $expand query option is not allowed on this endpoint.");
        }

        if (queryContext.CountRequested && !allowedOptions.HasFlag(ODataQueryOptions.Count))
        {
            return QueryValidationResult.Failed("ODATA_QUERY_OPTION_DISABLED", "The $count query option is not allowed on this endpoint.");
        }

        // Check $top limit
        if (queryContext.EffectiveTop > maxTop)
        {
            return QueryValidationResult.Failed("ODATA_TOP_EXCEEDED",
                $"The $top value exceeds the maximum allowed value of {maxTop}.");
        }

        // Check $expand depth
        if (queryContext.ExpandDepth > maxExpandDepth)
        {
            return QueryValidationResult.Failed("ODATA_EXPAND_DEPTH_EXCEEDED",
                $"The $expand depth exceeds the maximum allowed depth of {maxExpandDepth}.");
        }

        // Check $filter depth
        if (queryContext.FilterDepth > options.MaxFilterDepth)
        {
            return QueryValidationResult.Failed("ODATA_FILTER_DEPTH_EXCEEDED",
                $"The $filter nesting depth exceeds the maximum allowed depth of {options.MaxFilterDepth}.");
        }

        // Check $orderby property count
        if (queryContext.OrderByPropertyCount > options.MaxOrderByProperties)
        {
            return QueryValidationResult.Failed("ODATA_ORDERBY_EXCEEDED",
                $"The $orderby clause exceeds the maximum allowed properties of {options.MaxOrderByProperties}.");
        }

        // Check complexity score
        var complexityScore = _complexityCalculator.ComputeScore(queryContext);
        queryContext.ComplexityScore = complexityScore;

        if (complexityScore > options.MaxQueryComplexityScore)
        {
            return QueryValidationResult.Failed("ODATA_QUERY_TOO_COMPLEX",
                $"The query complexity score ({complexityScore}) exceeds the maximum allowed score of {options.MaxQueryComplexityScore}.");
        }

        return QueryValidationResult.Passed();
    }
}
