/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Models;

/// <summary>
/// Query parameters for searching transaction logs in the hot store.
/// </summary>
internal sealed record TransactionLogQuery
{
    public string? TransactionId { get; init; }
    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public string? IdempotencyKey { get; init; }
    public DateTimeOffset? RangeStart { get; init; }
    public DateTimeOffset? RangeEnd { get; init; }
    public int MaxResults { get; init; } = 50;
}

/// <summary>
/// Result of a transaction log query.
/// </summary>
internal sealed record TransactionLogQueryResult
{
    public IReadOnlyList<TransactionLogSummary> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public string? ContinuationToken { get; init; }
}

/// <summary>
/// Summary of a single transaction log returned in query results.
/// </summary>
internal sealed record TransactionLogSummary
{
    public string TransactionId { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string? RequestPath { get; init; }
    public string Status { get; init; } = string.Empty;
    public double? DurationMs { get; init; }
    public int EntryCount { get; init; }
    public DateTimeOffset StartTimestamp { get; init; }
}
