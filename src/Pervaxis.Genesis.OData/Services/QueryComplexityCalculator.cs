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

namespace Pervaxis.Genesis.OData.Services;

/// <summary>
/// Computes query complexity using the formula:
/// filterConditions * 2 + expandDepth * 10 + orderByProperties * 3 + (count ? 5 : 0) + max(0, selectProperties - 5).
/// </summary>
public sealed class QueryComplexityCalculator
{
    /// <summary>
    /// Computes the complexity score for the given query context.
    /// </summary>
    /// <param name="queryContext">The parsed query context.</param>
    /// <returns>The computed complexity score.</returns>
    public int ComputeScore(ODataQueryContext queryContext)
    {
        ArgumentNullException.ThrowIfNull(queryContext);

        var score = 0;
        score += queryContext.FilterConditionCount * 2;
        score += queryContext.ExpandDepth * 10;
        score += queryContext.OrderByPropertyCount * 3;
        score += queryContext.CountRequested ? 5 : 0;
        score += Math.Max(0, queryContext.SelectPropertyCount - 5);
        return score;
    }
}
