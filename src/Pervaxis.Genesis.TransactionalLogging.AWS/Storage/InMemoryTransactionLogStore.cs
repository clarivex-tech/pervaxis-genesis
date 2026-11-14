/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.MultiTenancy;
using System.Collections.Concurrent;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Models;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

/// <summary>
/// In-memory implementation of <see cref="ITransactionLogStore"/> for unit testing
/// and local development fallback.
/// </summary>
internal sealed class InMemoryTransactionLogStore(ITenantContext? tenantContext = null) : ITransactionLogStore
{
    private readonly ConcurrentDictionary<string, TransactionContext> _store = new();

    internal IReadOnlyDictionary<string, TransactionContext> Store => _store;

    public Task PersistAsync(TransactionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TenantId);
        var tenant = TenantIsolation.RequireTenant(tenantContext, context.TenantId);
        _store.TryAdd(tenant + "#" + context.TransactionId, context);
        return Task.CompletedTask;
    }

    public Task<TransactionLogQueryResult> QueryAsync(
        TransactionLogQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = TenantIsolation.RequireTenant(tenantContext, query.TenantId);
        var matches = _store.Values.Where(c => c.TenantId == tenant
            && (query.TransactionId is null || query.TransactionId == c.TransactionId))
            .Take(query.MaxResults > 0 ? query.MaxResults : 50)
            .Select(c => new TransactionLogSummary
            {
                TransactionId = c.TransactionId, TenantId = c.TenantId,
                RequestPath = c.RequestPath, Status = c.Status.ToString(),
                DurationMs = c.DurationMs, EntryCount = c.Entries.Count, StartTimestamp = c.StartTimestamp
            }).ToList();
        return Task.FromResult(new TransactionLogQueryResult { Items = matches, TotalCount = matches.Count });
    }
}
