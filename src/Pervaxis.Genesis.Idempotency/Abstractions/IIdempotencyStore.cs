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

namespace Pervaxis.Genesis.Idempotency.Abstractions;

/// <summary>
/// Abstraction for persisting and retrieving idempotency records.
/// All implementations must be thread-safe for concurrent calls with different keys.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Retrieves an idempotency record if one exists and has not expired.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (or "__global__" when no tenant).</param>
    /// <param name="idempotencyKey">The idempotency key value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record if found and not expired; null otherwise.</returns>
    Task<IdempotencyRecord?> TryGetRecordAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates an in-flight record if no unexpired record exists.
    /// Expired records are treated as nonexistent.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (or "__global__" when no tenant).</param>
    /// <param name="idempotencyKey">The idempotency key value.</param>
    /// <param name="fingerprint">The request fingerprint for validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if created successfully; false if a record already exists.</returns>
    Task<bool> CreateInFlightRecordAsync(
        string tenantId,
        string idempotencyKey,
        string fingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing in-flight record with the completed response.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (or "__global__" when no tenant).</param>
    /// <param name="idempotencyKey">The idempotency key value.</param>
    /// <param name="record">The completed record with response data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated; false if no in-flight record exists.</returns>
    Task<bool> CompleteRecordAsync(
        string tenantId,
        string idempotencyKey,
        IdempotencyRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a record. No-op if record doesn't exist.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (or "__global__" when no tenant).</param>
    /// <param name="idempotencyKey">The idempotency key value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteRecordAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
