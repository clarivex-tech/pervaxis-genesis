// -------------------------------------------------------------------------
// Copyright (c) 2026 Clarivex Technologies. All rights reserved.
// Pervaxis Platform - Genesis Edition
// -------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Caching.Abstractions;
using Pervaxis.Genesis.Caching.Options;
using StackExchange.Redis;

namespace Pervaxis.Genesis.Caching.Providers.ElastiCache;

/// <summary>
/// ElastiCache (Redis) implementation of the caching provider.
/// Supports AWS ElastiCache Redis clusters and local Redis instances.
/// </summary>
public sealed class ElastiCacheProvider : ICache, IDisposable
{
    private readonly CachingOptions _options;
    private readonly ILogger<ElastiCacheProvider> _logger;
    private readonly Lazy<ConnectionMultiplexer> _connection;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElastiCacheProvider"/> class.
    /// </summary>
    /// <param name="options">Caching configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public ElastiCacheProvider(
        IOptions<CachingOptions> options,
        ILogger<ElastiCacheProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(ElastiCacheProvider),
                "Invalid caching configuration");
        }

        _connection = new Lazy<ConnectionMultiplexer>(() => CreateConnection());

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        _logger.LogInformation(
            "ElastiCacheProvider initialized with database {Database}",
            _options.Database);
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();

        try
        {
            var value = await db.StringGetAsync(fullKey);

            if (!value.HasValue)
            {
                _logger.LogDebug("Cache miss for key: {Key}", fullKey);
                return default;
            }

            _logger.LogDebug("Cache hit for key: {Key}", fullKey);
            return Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cached value for key: {Key}", fullKey);
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache get operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        try
        {
            var serialized = Serialize(value);
            var result = await db.StringSetAsync(fullKey, serialized, effectiveExpiry);

            if (result)
            {
                _logger.LogDebug(
                    "Cached value for key: {Key} with expiry: {Expiry}",
                    fullKey,
                    effectiveExpiry);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache value for key: {Key}", fullKey);
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache set operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();

        try
        {
            var result = await db.KeyDeleteAsync(fullKey);

            if (result)
            {
                _logger.LogDebug("Removed cached value for key: {Key}", fullKey);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cached value for key: {Key}", fullKey);
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache remove operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();

        try
        {
            return await db.KeyExistsAsync(fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence for key: {Key}", fullKey);
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache exists operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var keyList = keys.ToList();
        if (keyList.Count == 0)
        {
            return new Dictionary<string, T?>();
        }

        var db = GetDatabase();
        var fullKeys = keyList.Select(GetFullKey).ToArray();
        var redisKeys = fullKeys.Select(k => (RedisKey)k).ToArray();

        try
        {
            var values = await db.StringGetAsync(redisKeys);
            var result = new Dictionary<string, T?>();

            for (int i = 0; i < keyList.Count; i++)
            {
                result[keyList[i]] = values[i].HasValue
                    ? Deserialize<T>(values[i]!)
                    : default;
            }

            _logger.LogDebug("Retrieved {Count} keys from cache", keyList.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get multiple cached values");
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache get many operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetManyAsync<T>(
        IDictionary<string, T> items,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return true;
        }

        var db = GetDatabase();
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        try
        {
            var batch = db.CreateBatch();
            var tasks = new List<Task<bool>>();

            foreach (var item in items)
            {
                var fullKey = GetFullKey(item.Key);
                var serialized = Serialize(item.Value);
                tasks.Add(batch.StringSetAsync(fullKey, serialized, effectiveExpiry));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            var allSucceeded = tasks.All(t => t.Result);

            if (allSucceeded)
            {
                _logger.LogDebug(
                    "Cached {Count} values with expiry: {Expiry}",
                    items.Count,
                    effectiveExpiry);
            }

            return allSucceeded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache multiple values");
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache set many operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshAsync(
        string key,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        try
        {
            var result = await db.KeyExpireAsync(fullKey, effectiveExpiry);

            if (result)
            {
                _logger.LogDebug(
                    "Refreshed expiry for key: {Key} with duration: {Expiry}",
                    fullKey,
                    effectiveExpiry);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh expiry for key: {Key}", fullKey);
            throw new GenesisException(nameof(ElastiCacheProvider), "Cache refresh operation failed", ex);
        }
    }

    /// <summary>
    /// Disposes the Redis connection.
    /// </summary>
    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
            _logger.LogInformation("ElastiCacheProvider connection disposed");
        }
    }

    private ConnectionMultiplexer CreateConnection()
    {
        var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
        configOptions.ConnectTimeout = _options.ConnectTimeoutMs;
        configOptions.SyncTimeout = _options.SyncTimeoutMs;
        configOptions.AbortOnConnectFail = _options.AbortOnConnectFail;
        configOptions.Ssl = _options.UseSsl;

        if (_options.UseLocalStack)
        {
            configOptions.Ssl = false;
            _logger.LogInformation("Using LocalStack mode - SSL disabled");
        }

        _logger.LogInformation("Connecting to Redis: {Endpoint}", configOptions.EndPoints[0]);

        return ConnectionMultiplexer.Connect(configOptions);
    }

    private IDatabase GetDatabase()
    {
        return _connection.Value.GetDatabase(_options.Database);
    }

    private string GetFullKey(string key)
    {
        return string.IsNullOrEmpty(_options.KeyPrefix)
            ? key
            : $"{_options.KeyPrefix}:{key}";
    }

    private string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _jsonOptions);
    }

    private T? Deserialize<T>(string value)
    {
        return JsonSerializer.Deserialize<T>(value, _jsonOptions);
    }
}
