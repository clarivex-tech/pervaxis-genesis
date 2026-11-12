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
using Pervaxis.Genesis.Idempotency.Abstractions;

namespace Pervaxis.Genesis.Idempotency.AWS.Fallback;

/// <summary>
/// In-memory implementation of <see cref="IIdempotencyStore"/> for local development and testing.
/// NOT suitable for production use — records are not persisted across restarts
/// and do not support distributed scenarios.
/// </summary>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new();

    /// <inheritdoc/>
    public Task<IdempotencyRecord?> TryGetRecordAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);

        if (_records.TryGetValue(compositeKey, out var record))
        {
            // Check expiration
            if (record.ExpiresAtEpoch <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                _records.TryRemove(compositeKey, out _);
                return Task.FromResult<IdempotencyRecord?>(null);
            }

            return Task.FromResult<IdempotencyRecord?>(record);
        }

        return Task.FromResult<IdempotencyRecord?>(null);
    }

    /// <inheritdoc/>
    public Task<bool> CreateInFlightRecordAsync(
        string tenantId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);
        var now = DateTimeOffset.UtcNow;

        var record = new IdempotencyRecord
        {
            IdempotencyKey = idempotencyKey,
            CompositeKey = compositeKey,
            Fingerprint = fingerprint,
            IsCompleted = false,
            CreatedAt = now,
            ExpiresAtEpoch = now.AddMinutes(30).ToUnixTimeSeconds() // Default TTL for in-memory
        };

        // Atomically try to add — fails if key already exists
        if (_records.TryGetValue(compositeKey, out var existing))
        {
            // Check if expired
            if (existing.ExpiresAtEpoch <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                _records.TryRemove(compositeKey, out _);
            }
            else
            {
                return Task.FromResult(false);
            }
        }

        var added = _records.TryAdd(compositeKey, record);
        return Task.FromResult(added);
    }

    /// <inheritdoc/>
    public Task<bool> CompleteRecordAsync(
        string tenantId,
        string idempotencyKey,
        IdempotencyRecord record,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);

        if (_records.TryGetValue(compositeKey, out var existing) && !existing.IsCompleted)
        {
            _records[compositeKey] = record;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task DeleteRecordAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var compositeKey = BuildCompositeKey(tenantId, idempotencyKey);
        _records.TryRemove(compositeKey, out _);
        return Task.CompletedTask;
    }

    private static string BuildCompositeKey(string tenantId, string idempotencyKey)
    {
        return $"{tenantId}#{idempotencyKey}";
    }
}
