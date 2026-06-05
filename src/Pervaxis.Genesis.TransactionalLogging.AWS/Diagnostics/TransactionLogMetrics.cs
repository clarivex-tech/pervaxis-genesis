/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Diagnostics;

/// <summary>
/// Static metrics for the transactional logging module.
/// </summary>
internal static class TransactionLogMetrics
{
    private static readonly Counter<long> TransactionsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.txlog.transactions", "1", "Total number of finalized transaction logs");

    private static readonly Counter<long> EntriesCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.txlog.entries", "1", "Total number of recorded transaction log entries");

    private static readonly Histogram<double> PersistDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.txlog.persist.duration", "ms", "Duration of transaction log persistence in milliseconds");

    private static readonly Counter<long> PersistFailures = PervaxisMeter.CreateCounter<long>(
        "genesis.txlog.persist.failures", "1", "Total number of persistence failures");

    public static void RecordTransaction(TransactionContext context)
    {
        try
        {
            EntriesCounter.Add(context.Entries.Count, new TagList
            {
                { "tenant_id", context.TenantId ?? "__global__" }
            });

            TransactionsCounter.Add(1, new TagList
            {
                { "status", context.Status.ToString().ToLowerInvariant() },
                { "tenant_id", context.TenantId ?? "__global__" }
            });
        }
        catch
        {
            // Metric emission failures must never affect request processing
        }
    }

    public static void RecordPersistDuration(double durationMs, string result)
    {
        try
        {
            PersistDuration.Record(durationMs, new TagList { { "result", result } });
        }
        catch
        {
            // Suppress
        }
    }

    public static void RecordPersistFailure(string reason)
    {
        try
        {
            PersistFailures.Add(1, new TagList { { "reason", reason } });
        }
        catch
        {
            // Suppress
        }
    }
}
