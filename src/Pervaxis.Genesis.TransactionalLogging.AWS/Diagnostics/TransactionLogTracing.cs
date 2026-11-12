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

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Diagnostics;

/// <summary>
/// Distributed tracing for the transactional logging module.
/// Uses PervaxisActivitySource per Genesis observability conventions.
/// </summary>
internal static class TransactionLogTracing
{
    /// <summary>
    /// Starts a trace activity for a transaction scope.
    /// </summary>
    public static Activity? StartScopeActivity(string transactionId, string? tenantId)
    {
        var activity = PervaxisActivitySource.StartActivity("txlog.scope", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("txlog.transaction_id", transactionId);

        if (tenantId is not null)
        {
            activity.SetTag("tenant.id", tenantId);
        }

        return activity;
    }

    /// <summary>
    /// Adds finalization tags to the scope activity.
    /// </summary>
    public static void FinalizeScopeActivity(Activity? activity, int entryCount, double durationMs,
        string status, string captureMode)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("txlog.entry_count", entryCount);
        activity.SetTag("txlog.duration_ms", durationMs);
        activity.SetTag("txlog.status", status);
        activity.SetTag("txlog.capture_mode", captureMode);
    }

    /// <summary>
    /// Starts a trace activity for a persistence operation.
    /// </summary>
    public static Activity? StartPersistActivity(string store)
    {
        var activity = PervaxisActivitySource.StartActivity("txlog.persist", ActivityKind.Internal);
        activity?.SetTag("txlog.store", store);
        return activity;
    }

    /// <summary>
    /// Finalizes a persist activity with the result.
    /// </summary>
    public static void FinalizePersistActivity(Activity? activity, string result)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("txlog.result", result);

        if (result == "error")
        {
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }
}
