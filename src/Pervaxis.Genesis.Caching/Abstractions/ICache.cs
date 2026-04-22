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

namespace Pervaxis.Genesis.Caching.Abstractions;

/// <summary>
/// Defines methods for distributed caching operations using AWS ElastiCache (Redis).
/// </summary>
public interface ICache
{
    /// <summary>
    /// Gets a cached value by key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value, or default if not found.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a cached value with optional expiry.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="expiry">Optional expiry duration. If null, uses default expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the value was set successfully.</returns>
    Task<bool> SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached value by key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key was removed.</returns>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a key exists in the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple cached values by keys.
    /// </summary>
    /// <typeparam name="T">The type of the cached values.</typeparam>
    /// <param name="keys">The cache keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of keys to values. Missing keys will have default values.</returns>
    Task<IDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets multiple cached values with optional expiry.
    /// </summary>
    /// <typeparam name="T">The type of the values to cache.</typeparam>
    /// <param name="items">Dictionary of keys to values.</param>
    /// <param name="expiry">Optional expiry duration. If null, uses default expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if all values were set successfully.</returns>
    Task<bool> SetManyAsync<T>(
        IDictionary<string, T> items,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the expiry time for a key without changing the value.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="expiry">New expiry duration. If null, uses default expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the expiry was refreshed.</returns>
    Task<bool> RefreshAsync(
        string key,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);
}
