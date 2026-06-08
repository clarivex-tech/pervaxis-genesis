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

namespace Pervaxis.Genesis.Sanitization.Diagnostics;

/// <summary>
/// Metrics instrumentation for the Genesis Sanitization module.
/// </summary>
internal static class SanitizationMetrics
{
    /// <summary>Total number of sanitization operations performed.</summary>
    internal static readonly Counter<long> OperationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.sanitization.operations",
        "1",
        "Total number of sanitization operations performed");

    /// <summary>Total number of inputs where dangerous content was detected and stripped.</summary>
    internal static readonly Counter<long> ThreatsDetectedCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.sanitization.threats_detected",
        "1",
        "Total number of inputs where dangerous content was detected and stripped");

    /// <summary>Duration of sanitization operations in milliseconds.</summary>
    internal static readonly Histogram<double> DurationHistogram = PervaxisMeter.CreateHistogram<double>(
        "genesis.sanitization.duration_ms",
        "ms",
        "Duration of sanitization operations in milliseconds");
}
