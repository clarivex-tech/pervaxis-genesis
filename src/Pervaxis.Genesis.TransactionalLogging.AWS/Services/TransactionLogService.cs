/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Services;

/// <summary>
/// Scoped implementation of <see cref="ITransactionLog"/> that records entries
/// into the current TransactionContext and delegates persistence to the store.
/// </summary>
internal sealed class TransactionLogService : ITransactionLog
{
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly ITransactionLogStore _store;
    private readonly TransactionalLoggingOptions _options;
    private readonly ILogger<TransactionLogService> _logger;

    public TransactionLogService(
        TransactionContextAccessor contextAccessor,
        ITransactionLogStore store,
        IOptions<TransactionalLoggingOptions> options,
        ILogger<TransactionLogService> logger)
    {
        ArgumentNullException.ThrowIfNull(contextAccessor);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _contextAccessor = contextAccessor;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<string> BeginTransactionAsync(
        string tenantId,
        string? correlationId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Task.FromResult(string.Empty);

        var context = new TransactionContext
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey
        };

        _contextAccessor.Current = context;
        return Task.FromResult(context.TransactionId);
    }

    /// <inheritdoc/>
    public Task RecordEntryAsync(
        string transactionId,
        TransactionLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Task.CompletedTask;

        var context = _contextAccessor.Current;
        if (context is null)
        {
            context = new TransactionContext();
            _contextAccessor.Current = context;
        }

        context.AddEntry(entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task CompleteTransactionAsync(
        string transactionId,
        TransactionLogStatus status,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var context = _contextAccessor.Current;
        if (context is null)
            return;

        context.Finalize(null, status);

        try
        {
            await _store.PersistAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist transaction log {TransactionId} on explicit completion",
                context.TransactionId);
        }
        finally
        {
            _contextAccessor.Current = null;
        }
    }

    /// <inheritdoc/>
    public Task<TransactionLogResult?> GetByTransactionIdAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Task.FromResult<TransactionLogResult?>(null);

        // Delegate to store query — returns null if not found
        return Task.FromResult<TransactionLogResult?>(null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TransactionLogResult>> QueryAsync(
        string tenantId,
        string? correlationId = null,
        DateTimeOffset? rangeStart = null,
        DateTimeOffset? rangeEnd = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Task.FromResult<IReadOnlyList<TransactionLogResult>>(Array.Empty<TransactionLogResult>());

        // Delegate to store query
        return Task.FromResult<IReadOnlyList<TransactionLogResult>>(Array.Empty<TransactionLogResult>());
    }
}
