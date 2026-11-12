/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Collections.Concurrent;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Models;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

/// <summary>
/// In-memory implementation of <see cref="ITransactionLogStore"/> for unit testing
/// and local development fallback.
/// </summary>
internal sealed class InMemoryTransactionLogStore : ITransactionLogStore
{
    private readonly ConcurrentDictionary<string, TransactionContext> _store = new();

    internal IReadOnlyDictionary<string, TransactionContext> Store => _store;

    public Task PersistAsync(TransactionContext context, CancellationToken cancellationToken = default)
    {
        _store.TryAdd(context.TransactionId, context);
        return Task.CompletedTask;
    }

    public Task<TransactionLogQueryResult> QueryAsync(
        TransactionLogQuery query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TransactionLogQueryResult());
    }
}
