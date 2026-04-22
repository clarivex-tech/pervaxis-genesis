// Copyright © Clarivex Technologies. All rights reserved.

using Pervaxis.Genesis.Base.Options;

namespace Pervaxis.Genesis.Caching.Options;

/// <summary>
/// Configuration options for the Genesis Caching provider (ElastiCache Redis).
/// </summary>
public sealed class CachingOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the Redis connection string.
    /// For ElastiCache: clustername.cache.amazonaws.com:6379
    /// For LocalStack: localhost:6379
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default expiry duration for cached items.
    /// Default is 1 hour.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the key prefix for all cache keys.
    /// Useful for multi-tenancy or environment isolation.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Redis database number to use.
    /// Default is 0.
    /// </summary>
    public int Database { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use SSL/TLS for Redis connection.
    /// Required for ElastiCache in-transit encryption.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Gets or sets the connection timeout in milliseconds.
    /// Default is 5000ms (5 seconds).
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the sync timeout in milliseconds for Redis operations.
    /// Default is 5000ms (5 seconds).
    /// </summary>
    public int SyncTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets a value indicating whether to abort connection on connection failure.
    /// Default is false for better resilience.
    /// </summary>
    public bool AbortOnConnectFail { get; set; }

    /// <summary>
    /// Validates the caching options configuration.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return false;
        }

        if (DefaultExpiry <= TimeSpan.Zero)
        {
            return false;
        }

        if (Database < 0)
        {
            return false;
        }

        if (ConnectTimeoutMs <= 0)
        {
            return false;
        }

        if (SyncTimeoutMs <= 0)
        {
            return false;
        }

        return true;
    }
}
