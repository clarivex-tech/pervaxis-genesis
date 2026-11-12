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

using System.Diagnostics.Metrics;
using Pervaxis.Core.Observability.Metrics;

namespace Pervaxis.Genesis.OData.Diagnostics;

/// <summary>
/// Metrics instrumentation for the Genesis OData module.
/// </summary>
internal static class ODataMetrics
{
    /// <summary>Total OData-enabled requests processed.</summary>
    internal static readonly Counter<long> RequestsProcessed = PervaxisMeter.CreateCounter<long>(
        "genesis.odata.requests", "1",
        "Total number of OData-enabled requests processed");

    /// <summary>Duration of OData query processing in milliseconds.</summary>
    internal static readonly Histogram<double> QueryDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.odata.query.duration", "ms",
        "Duration of OData query processing in milliseconds");

    /// <summary>Result count per query.</summary>
    internal static readonly Histogram<double> ResultCount = PervaxisMeter.CreateHistogram<double>(
        "genesis.odata.query.result_count", "1",
        "Number of results returned per OData query");

    /// <summary>Query complexity score per request.</summary>
    internal static readonly Histogram<double> ComplexityScore = PervaxisMeter.CreateHistogram<double>(
        "genesis.odata.query.complexity", "1",
        "Computed query complexity score per OData request");
}
