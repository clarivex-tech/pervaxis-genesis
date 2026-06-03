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

namespace Pervaxis.Genesis.Idempotency.Diagnostics;

/// <summary>
/// Metrics instrumentation for the Genesis Idempotency module.
/// </summary>
internal static class IdempotencyMetrics
{
    /// <summary>Total number of idempotency-enabled requests processed.</summary>
    internal static readonly Counter<long> RequestsProcessed = PervaxisMeter.CreateCounter<long>(
        "genesis.idempotency.requests",
        "1",
        "Total number of idempotency-enabled requests processed");

    /// <summary>Total number of cached responses replayed.</summary>
    internal static readonly Counter<long> ResponsesReplayed = PervaxisMeter.CreateCounter<long>(
        "genesis.idempotency.responses.replayed",
        "1",
        "Total number of cached responses replayed");

    /// <summary>Total number of in-flight conflicts detected.</summary>
    internal static readonly Counter<long> ConflictsDetected = PervaxisMeter.CreateCounter<long>(
        "genesis.idempotency.conflicts",
        "1",
        "Total number of in-flight key conflicts detected");

    /// <summary>Total number of fingerprint mismatches (key reuse).</summary>
    internal static readonly Counter<long> FingerprintMismatches = PervaxisMeter.CreateCounter<long>(
        "genesis.idempotency.fingerprint.mismatches",
        "1",
        "Total number of fingerprint mismatches (key reuse attempts)");

    /// <summary>Total number of store failures (fail-open events).</summary>
    internal static readonly Counter<long> StoreFailures = PervaxisMeter.CreateCounter<long>(
        "genesis.idempotency.store.failures",
        "1",
        "Total number of idempotency store failures (fail-open events)");

    /// <summary>Duration of store operations in milliseconds.</summary>
    internal static readonly Histogram<double> StoreOperationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.idempotency.store.duration",
        "ms",
        "Duration of idempotency store operations in milliseconds");
}
