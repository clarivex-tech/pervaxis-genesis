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

using System.Collections.Concurrent;
using Pervaxis.Core.Abstractions.Genesis.Modules;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Context;

/// <summary>
/// Scoped context that accumulates operation entries for one logical transaction.
/// Created per HTTP request by the middleware, or explicitly via BeginTransactionAsync.
/// Thread-safe for concurrent entry additions.
/// </summary>
public sealed class TransactionContext
{
    private readonly ConcurrentBag<TransactionLogEntry> _entries = new();
    private readonly ConcurrentDictionary<string, string> _businessKeys = new();

    /// <summary>Unique transaction identifier (format: txn_{Guid:N}).</summary>
    public string TransactionId { get; } = $"txn_{Guid.NewGuid():N}";

    /// <summary>Distributed trace ID from Activity.Current.</summary>
    public string? TraceId { get; init; }

    /// <summary>Tenant ID from ITenantContext.</summary>
    public string? TenantId { get; init; }

    /// <summary>HTTP method of the request.</summary>
    public string? HttpMethod { get; init; }

    /// <summary>Request path.</summary>
    public string? RequestPath { get; init; }

    /// <summary>Idempotency key from request header (when present).</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Correlation ID from X-Correlation-Id header (when present).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>UTC timestamp when the transaction started.</summary>
    public DateTimeOffset StartTimestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when the transaction ended.</summary>
    public DateTimeOffset? EndTimestamp { get; private set; }

    /// <summary>Total duration in milliseconds.</summary>
    public double? DurationMs { get; private set; }

    /// <summary>HTTP status code of the response.</summary>
    public int? HttpStatusCode { get; private set; }

    /// <summary>Transaction status.</summary>
    public TransactionLogStatus Status { get; private set; } = TransactionLogStatus.InProgress;

    /// <summary>Exception type if the transaction failed.</summary>
    public string? ErrorType { get; private set; }

    /// <summary>Exception message if the transaction failed.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>All recorded entries in this transaction.</summary>
    public IReadOnlyCollection<TransactionLogEntry> Entries => _entries.ToArray();

    /// <summary>Business keys attached for queryability.</summary>
    public IReadOnlyDictionary<string, string> BusinessKeys =>
        _businessKeys.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Adds an operation entry to the transaction.</summary>
    public void AddEntry(TransactionLogEntry entry) => _entries.Add(entry);

    /// <summary>Attaches a business key for query support.</summary>
    public void AddBusinessKey(string key, string value) =>
        _businessKeys.TryAdd(key, value);

    /// <summary>Finalizes the transaction with completion information.</summary>
    public void Finalize(int? statusCode, TransactionLogStatus status,
        string? errorType = null, string? errorMessage = null)
    {
        EndTimestamp = DateTimeOffset.UtcNow;
        DurationMs = (EndTimestamp.Value - StartTimestamp).TotalMilliseconds;
        HttpStatusCode = statusCode;
        Status = status;
        ErrorType = errorType;
        ErrorMessage = errorMessage;
    }
}
