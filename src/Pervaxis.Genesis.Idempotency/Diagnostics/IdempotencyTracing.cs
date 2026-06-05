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

using System.Diagnostics;
using Pervaxis.Core.Observability.Tracing;

namespace Pervaxis.Genesis.Idempotency.Diagnostics;

/// <summary>
/// Distributed tracing instrumentation for the Genesis Idempotency module.
/// </summary>
internal static class IdempotencyTracing
{
    /// <summary>
    /// Starts a trace activity for idempotency key lookup.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key being processed.</param>
    /// <returns>The started activity (may be null if no listener configured).</returns>
    internal static Activity? StartLookupActivity(string idempotencyKey)
    {
        var activity = PervaxisActivitySource.StartActivity(
            "idempotency.lookup", ActivityKind.Client);
        activity?.SetTag("idempotency.key", idempotencyKey);
        return activity;
    }

    /// <summary>
    /// Starts a trace activity for idempotency record creation.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key being created.</param>
    /// <returns>The started activity (may be null if no listener configured).</returns>
    internal static Activity? StartCreateActivity(string idempotencyKey)
    {
        var activity = PervaxisActivitySource.StartActivity(
            "idempotency.create", ActivityKind.Client);
        activity?.SetTag("idempotency.key", idempotencyKey);
        return activity;
    }

    /// <summary>
    /// Starts a trace activity for idempotency record completion.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key being completed.</param>
    /// <returns>The started activity (may be null if no listener configured).</returns>
    internal static Activity? StartCompleteActivity(string idempotencyKey)
    {
        var activity = PervaxisActivitySource.StartActivity(
            "idempotency.complete", ActivityKind.Client);
        activity?.SetTag("idempotency.key", idempotencyKey);
        return activity;
    }
}
