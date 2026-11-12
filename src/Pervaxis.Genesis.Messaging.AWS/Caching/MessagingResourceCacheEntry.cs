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

namespace Pervaxis.Genesis.Messaging.AWS.Caching;

/// <summary>
/// Cache entry for resolved messaging resources (queue URLs, topic ARNs).
/// Includes TTL-based expiration to prevent stale entries.
/// </summary>
public sealed class MessagingResourceCacheEntry
{
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastAccessedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingResourceCacheEntry"/> class.
    /// </summary>
    /// <param name="value">Resolved resource identifier (queue URL or topic ARN).</param>
    /// <param name="ttl">Time-to-live duration for this cache entry.</param>
    /// <param name="timeProvider">Time provider for testable expiration checks.</param>
    public MessagingResourceCacheEntry(string value, TimeSpan ttl, TimeProvider timeProvider)
    {
        Value = value;
        Ttl = ttl;
        _timeProvider = timeProvider;
        CreatedAt = timeProvider.GetUtcNow();
        _lastAccessedAt = CreatedAt;
    }

    /// <summary>
    /// Resolved resource value (queue URL or topic ARN).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Time-to-live for this cache entry.
    /// </summary>
    public TimeSpan Ttl { get; }

    /// <summary>
    /// Timestamp when this entry was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Timestamp when this entry was last accessed.
    /// </summary>
    public DateTimeOffset LastAccessedAt => _lastAccessedAt;

    /// <summary>
    /// Checks if this cache entry has expired based on TTL.
    /// </summary>
    /// <returns>True if expired, false otherwise.</returns>
    public bool IsExpired() => _timeProvider.GetUtcNow() - CreatedAt > Ttl;

    /// <summary>
    /// Updates the last accessed timestamp (LRU tracking).
    /// </summary>
    public void Touch()
    {
        _lastAccessedAt = _timeProvider.GetUtcNow();
    }
}
