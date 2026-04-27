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

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.Caching.AWS.Options;
using StackExchange.Redis;

namespace Pervaxis.Genesis.Caching.AWS.Providers.ElastiCache;

/// <summary>
/// ElastiCache (Redis) implementation of the caching provider.
/// Supports AWS ElastiCache Redis clusters and local Redis instances.
/// </summary>
public sealed class ElastiCacheProvider : ICache, IDisposable
{
    private readonly CachingOptions _options;
    private readonly ILogger<ElastiCacheProvider> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IConnectionMultiplexer> _connection;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResiliencePipeline _resiliencePipeline;

    // Metrics
    private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.cache.operations",
        "1",
        "Total number of cache operations");

    private static readonly Counter<long> _hitsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.cache.hits",
        "1",
        "Total number of cache hits");

    private static readonly Counter<long> _missesCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.cache.misses",
        "1",
        "Total number of cache misses");

    private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.cache.operation.duration",
        "ms",
        "Duration of cache operations in milliseconds");

    /// <summary>
    /// Initializes a new instance of the <see cref="ElastiCacheProvider"/> class.
    /// </summary>
    /// <param name="options">Caching configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public ElastiCacheProvider(
        IOptions<CachingOptions> options,
        ILogger<ElastiCacheProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        ValidateOptions();

        _connection = new Lazy<IConnectionMultiplexer>(CreateConnection);
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "ElastiCache");

        _logger.LogInformation(
            "ElastiCacheProvider initialized with database {Database}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.Database,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElastiCacheProvider"/> class
    /// with an explicit connection — intended for unit testing only.
    /// </summary>
    internal ElastiCacheProvider(
        IOptions<CachingOptions> options,
        ILogger<ElastiCacheProvider> logger,
        IConnectionMultiplexer connection,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connection);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        ValidateOptions();

        _connection = new Lazy<IConnectionMultiplexer>(() => connection);
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "ElastiCache");
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.get", ActivityKind.Client);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.operation", "get");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();

        try
        {
            var value = await _resiliencePipeline.ExecuteAsync(
                async ct => await db.StringGetAsync(fullKey),
                cancellationToken);

            if (!value.HasValue)
            {
                activity?.SetTag("cache.hit", false);
                _logger.LogDebug("Cache miss for key: {Key}", fullKey);

                // Record metrics
                _operationsCounter.Add(1, GetMetricTags("get", "miss"));
                _missesCounter.Add(1, GetMetricTags("get", "miss"));
                _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("get", "miss"));

                return default;
            }

            activity?.SetTag("cache.hit", true);
            _logger.LogDebug("Cache hit for key: {Key}", fullKey);

            // Record metrics
            _operationsCounter.Add(1, GetMetricTags("get", "hit"));
            _hitsCounter.Add(1, GetMetricTags("get", "hit"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("get", "hit"));

            return Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get cached value for key: {Key}", fullKey);

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("get", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("get", "error"));

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
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.set", ActivityKind.Client);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.operation", "set");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        try
        {
            var serialized = Serialize(value);
            var result = await _resiliencePipeline.ExecuteAsync(
                async ct => await db.StringSetAsync(fullKey, serialized, effectiveExpiry),
                cancellationToken);

            activity?.SetTag("cache.success", result);
            if (result)
            {
                _logger.LogDebug(
                    "Cached value for key: {Key} with expiry: {Expiry}",
                    fullKey,
                    effectiveExpiry);
            }

            // Record metrics
            var resultTag = result ? "success" : "failure";
            _operationsCounter.Add(1, GetMetricTags("set", resultTag));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("set", resultTag));

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to cache value for key: {Key}", fullKey);

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("set", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("set", "error"));

            throw new GenesisException(nameof(ElastiCacheProvider), "Cache set operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.remove", ActivityKind.Client);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.operation", "remove");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();

        try
        {
            var result = await _resiliencePipeline.ExecuteAsync(
                async ct => await db.KeyDeleteAsync(fullKey),
                cancellationToken);

            activity?.SetTag("cache.success", result);
            if (result)
            {
                _logger.LogDebug("Removed cached value for key: {Key}", fullKey);
            }

            // Record metrics
            var resultTag = result ? "success" : "not_found";
            _operationsCounter.Add(1, GetMetricTags("remove", resultTag));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("remove", resultTag));

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to remove cached value for key: {Key}", fullKey);

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("remove", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("remove", "error"));

            throw new GenesisException(nameof(ElastiCacheProvider), "Cache remove operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.exists", ActivityKind.Client);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.operation", "exists");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();

        try
        {
            var result = await _resiliencePipeline.ExecuteAsync(
                async ct => await db.KeyExistsAsync(fullKey),
                cancellationToken);
            activity?.SetTag("cache.exists", result);

            // Record metrics
            var resultTag = result ? "exists" : "not_found";
            _operationsCounter.Add(1, GetMetricTags("exists", resultTag));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("exists", resultTag));

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to check existence for key: {Key}", fullKey);

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("exists", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("exists", "error"));

            throw new GenesisException(nameof(ElastiCacheProvider), "Cache exists operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.get_many", ActivityKind.Client);
        activity?.SetTag("cache.operation", "get_many");
        AddTenantTags(activity);

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
            var values = await _resiliencePipeline.ExecuteAsync(
                async ct => await db.StringGetAsync(redisKeys),
                cancellationToken);
            var result = new Dictionary<string, T?>();

            for (var i = 0; i < keyList.Count; i++)
            {
                result[keyList[i]] = values[i].HasValue
                    ? Deserialize<T>(values[i]!)
                    : default;
            }

            activity?.SetTag("cache.key_count", keyList.Count);
            _logger.LogDebug("Retrieved {Count} keys from cache", keyList.Count);

            // Record metrics
            _operationsCounter.Add(1, GetMetricTags("get_many", "success"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("get_many", "success"));

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get multiple cached values");

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("get_many", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("get_many", "error"));

            throw new GenesisException(nameof(ElastiCacheProvider), "Cache get many operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SetManyAsync<T>(
        IDictionary<string, T> items,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.set_many", ActivityKind.Client);
        activity?.SetTag("cache.operation", "set_many");
        AddTenantTags(activity);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return true;
        }

        var db = GetDatabase();
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        try
        {
            var allSucceeded = await _resiliencePipeline.ExecuteAsync(async ct =>
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

                return tasks.All(t => t.Result);
            }, cancellationToken);

            activity?.SetTag("cache.key_count", items.Count);
            activity?.SetTag("cache.success", allSucceeded);
            if (allSucceeded)
            {
                _logger.LogDebug(
                    "Cached {Count} values with expiry: {Expiry}",
                    items.Count,
                    effectiveExpiry);
            }

            // Record metrics
            var resultTag = allSucceeded ? "success" : "partial_failure";
            _operationsCounter.Add(1, GetMetricTags("set_many", resultTag));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("set_many", resultTag));

            return allSucceeded;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to cache multiple values");

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("set_many", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("set_many", "error"));

            throw new GenesisException(nameof(ElastiCacheProvider), "Cache set many operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RefreshAsync(
        string key,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("cache.refresh", ActivityKind.Client);
        activity?.SetTag("cache.key", key);
        activity?.SetTag("cache.operation", "refresh");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullKey = GetFullKey(key);
        var db = GetDatabase();
        var effectiveExpiry = expiry ?? _options.DefaultExpiry;

        try
        {
            var result = await _resiliencePipeline.ExecuteAsync(
                async ct => await db.KeyExpireAsync(fullKey, effectiveExpiry),
                cancellationToken);

            activity?.SetTag("cache.success", result);
            if (result)
            {
                _logger.LogDebug(
                    "Refreshed expiry for key: {Key} with duration: {Expiry}",
                    fullKey,
                    effectiveExpiry);
            }

            // Record metrics
            var resultTag = result ? "success" : "not_found";
            _operationsCounter.Add(1, GetMetricTags("refresh", resultTag));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("refresh", resultTag));

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to refresh expiry for key: {Key}", fullKey);

            // Record failure metric
            _operationsCounter.Add(1, GetMetricTags("refresh", "error"));
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, GetMetricTags("refresh", "error"));

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

    private void ValidateOptions()
    {
        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(ElastiCacheProvider),
                "Invalid caching configuration");
        }
    }

    private IConnectionMultiplexer CreateConnection()
    {
        var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
        configOptions.ConnectTimeout = _options.ConnectTimeoutMs;
        configOptions.SyncTimeout = _options.SyncTimeoutMs;
        configOptions.AbortOnConnectFail = _options.AbortOnConnectFail;
        configOptions.Ssl = _options.UseSsl;

        if (_options.UseLocalEmulator)
        {
            configOptions.Ssl = false;
            _logger.LogInformation("Using local emulator mode - SSL disabled");
        }

        _logger.LogInformation("Connecting to Redis: {Endpoint}", configOptions.EndPoints[0]);

        return ConnectionMultiplexer.Connect(configOptions);
    }

    private IDatabase GetDatabase() => _connection.Value.GetDatabase(_options.Database);

    private string GetFullKey(string key)
    {
        var parts = new List<string>();

        // Add environment/key prefix if configured
        if (!string.IsNullOrEmpty(_options.KeyPrefix))
        {
            parts.Add(_options.KeyPrefix);
        }

        // Add tenant prefix if isolation is enabled and tenant context is available
        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            parts.Add($"tenant:{_tenantContext.TenantId.Value}");
        }

        // Add the actual key
        parts.Add(key);

        return string.Join(":", parts);
    }

    private string Serialize<T>(T value) => JsonSerializer.Serialize(value, _jsonOptions);

    private T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, _jsonOptions);

    private static JsonSerializerOptions BuildJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }

    private TagList GetMetricTags(string operation, string result)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "result", result }
        };

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }

        return tags;
    }
}
