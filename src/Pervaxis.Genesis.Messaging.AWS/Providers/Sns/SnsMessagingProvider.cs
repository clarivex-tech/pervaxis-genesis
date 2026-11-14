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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.Messaging.AWS.Caching;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Security;

namespace Pervaxis.Genesis.Messaging.AWS.Providers.Sns;

/// <summary>
/// SNS implementation of the <see cref="IMessaging"/> abstraction.
/// Supports tenant-stamped single and batch publication to shared topics.
/// </summary>
/// <remarks>
/// <see cref="ReceiveAsync{T}"/> and <see cref="DeleteAsync"/> are not supported by SNS
/// (SNS is push-based). Use <see cref="Providers.Sqs.SqsMessagingProvider"/> for
/// queue-based receive and delete operations.
/// </remarks>
public sealed class SnsMessagingProvider : IMessaging, IDisposable
{
    private const int SnsMaxBatchSize = 10;

    private readonly MessagingOptions _options;
    private readonly ILogger<SnsMessagingProvider> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly TenantMessagingGuard _guard;
    private readonly bool _ownsClient;
    private bool _disposed;
    private readonly Lazy<IAmazonSimpleNotificationService> _snsClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly TimeProvider _timeProvider;

    // ARN cache: thread-safe, keyed by (destination, tenant, scope)
    // Entries expire after configured TTL to prevent unbounded growth
    private readonly ConcurrentDictionary<MessagingResourceCacheKey, MessagingResourceCacheEntry> _arnCache = new();

    // Metrics (shared with SQS via static fields)
    private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.operations",
        "1",
        "Total number of messaging operations");

    private static readonly Counter<long> _messagesSent = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.messages.sent",
        "1",
        "Total number of messages sent");

    private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.messaging.operation.duration",
        "ms",
        "Duration of messaging operations in milliseconds");

    private static readonly Counter<long> _cacheHits = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.cache.hits",
        "1",
        "Number of cache hits for ARN resolution");

    private static readonly Counter<long> _cacheMisses = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.cache.misses",
        "1",
        "Number of cache misses for ARN resolution");

    private static readonly Counter<long> _cacheInvalidations = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.cache.invalidations",
        "1",
        "Number of cache invalidations due to not-found errors");

    /// <summary>
    /// Initializes a new instance of <see cref="SnsMessagingProvider"/> for production use.
    /// </summary>
    /// <param name="options">Messaging configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Current tenant context; publication fails when it is unresolved.</param>
    /// <param name="environment">Legacy compatibility parameter; isolation cannot be disabled in any environment.</param>
    /// <param name="timeProvider">Optional time provider for testing (defaults to system time).</param>
    public SnsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SnsMessagingProvider> logger,
        ITenantContext? tenantContext = null,
        string? environment = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _guard = new TenantMessagingGuard(tenantContext);
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions();

        _ownsClient = true;
        _snsClient = new Lazy<IAmazonSimpleNotificationService>(() => CreateClient(_options));
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "SnsMessaging");

        _logger.LogInformation(
            "SnsMessagingProvider initialized for region {Region}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.Region,
            _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SnsMessagingProvider"/> with an explicit
    /// SNS client — intended for unit testing only.
    /// </summary>
    internal SnsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SnsMessagingProvider> logger,
        IAmazonSimpleNotificationService snsClient,
        ITenantContext? tenantContext = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(snsClient);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _guard = new TenantMessagingGuard(tenantContext);
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions();

        _snsClient = new Lazy<IAmazonSimpleNotificationService>(() => snsClient);
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "SnsMessaging");
    }

    /// <inheritdoc/>
    public async Task<string> PublishAsync<T>(
        string destination,
        T message,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "sns");
        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.operation", "publish");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(message);

        var topicArn = ResolveTopicArn(destination);
        RequireTenant();
        var body = Serialize(message);

        try
        {
            var request = new PublishRequest
            {
                TopicArn = topicArn,
                Message = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>()
            };

            AddTenantAttributes(request.MessageAttributes);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _snsClient.Value.PublishAsync(request, ct).ConfigureAwait(false),
                cancellationToken);

            activity?.SetTag("messaging.message_id", response.MessageId);
            _logger.LogDebug(
                "Published message {MessageId} to SNS topic {TopicArn}",
                response.MessageId, topicArn);

            var tags = GetMetricTags("publish", "success", "sns");
            _operationsCounter.Add(1, tags);
            _messagesSent.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return response.MessageId;
        }
        catch (NotFoundException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SNS topic {TopicArn} does not exist. Resource must be provisioned.", topicArn);

            // Invalidate cache on not-found error
            InvalidateTopicCache(destination, MessageScope.Tenant);

            var errorTags = GetMetricTags("publish", "error", "sns");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SnsMessagingProvider),
                $"Topic '{topicArn}' not provisioned. Ensure tenant messaging resources are created before use.",
                ex,
                "TOPIC_NOT_FOUND",
                new Dictionary<string, object> { ["TopicArn"] = topicArn });
        }
        catch (Exception ex) when (ex is AuthorizationErrorException or ThrottledException)
        {
            // Pass through access denied and throttling unchanged for IAM policy detection
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GenesisException)
        {
            // Pass through Genesis exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SNS publish operation failed");

            var errorTags = GetMetricTags("publish", "error", "sns");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SnsMessagingProvider),
                "SNS publish operation failed. See inner exception for details.",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> PublishBatchAsync<T>(
        string destination,
        IEnumerable<T> messages,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.publish_batch", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "sns");
        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.operation", "publish_batch");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(messages);

        RequireTenant();
        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return [];
        }

        activity?.SetTag("messaging.message_count", messageList.Count);
        var topicArn = ResolveTopicArn(destination);
        var allMessageIds = new List<string>();

        try
        {
            foreach (var chunk in messageList.Chunk(SnsMaxBatchSize))
            {
                var entries = chunk
                    .Select((msg, idx) =>
                    {
                        var entry = new PublishBatchRequestEntry
                        {
                            Id = idx.ToString(CultureInfo.InvariantCulture),
                            Message = Serialize(msg),
                            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
                        };
                        AddTenantAttributes(entry.MessageAttributes);
                        return entry;
                    })
                    .ToList();

                var request = new PublishBatchRequest
                {
                    TopicArn = topicArn,
                    PublishBatchRequestEntries = entries
                };

                var response = await _resiliencePipeline.ExecuteAsync(
                    async ct => await _snsClient.Value.PublishBatchAsync(request, ct).ConfigureAwait(false),
                    cancellationToken);

                if (response.Failed.Count > 0)
                {
                    _logger.LogError(
                        "SNS batch publish had {FailureCount} failures to topic {TopicArn}. Failed IDs: {FailedIds}",
                        response.Failed.Count, topicArn,
                        string.Join(", ", response.Failed.Select(f => f.Id)));
                }

                allMessageIds.AddRange(response.Successful.Select(s => s.MessageId));

                _logger.LogDebug(
                    "Published batch of {Count} messages to SNS topic {TopicArn}",
                    response.Successful.Count, topicArn);
            }

            activity?.SetTag("messaging.success_count", allMessageIds.Count);

            var tags = GetMetricTags("publish_batch", "success", "sns");
            _operationsCounter.Add(1, tags);
            _messagesSent.Add(allMessageIds.Count, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return allMessageIds;
        }
        catch (NotFoundException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SNS topic {TopicArn} does not exist. Resource must be provisioned.", topicArn);

            // Invalidate cache on not-found error
            InvalidateTopicCache(destination, MessageScope.Tenant);

            var errorTags = GetMetricTags("publish_batch", "error", "sns");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SnsMessagingProvider),
                $"Topic '{topicArn}' not provisioned. Ensure tenant messaging resources are created before use.",
                ex,
                "TOPIC_NOT_FOUND",
                new Dictionary<string, object> { ["TopicArn"] = topicArn });
        }
        catch (Exception ex) when (ex is AuthorizationErrorException or ThrottledException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GenesisException)
        {
            // Pass through Genesis exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SNS batch publish operation failed");

            var errorTags = GetMetricTags("publish_batch", "error", "sns");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SnsMessagingProvider),
                "SNS batch publish operation failed. See inner exception for details.",
                ex);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// SNS is push-based and does not support polling. Use <see cref="Providers.Sqs.SqsMessagingProvider"/>
    /// for queue-based receive operations.
    /// </exception>
    public Task<IEnumerable<T>> ReceiveAsync<T>(
        string queue,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "SNS is push-based and does not support polling. Use SqsMessagingProvider for receive operations.");
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// SNS does not support message deletion. Use <see cref="Providers.Sqs.SqsMessagingProvider"/>
    /// for queue-based delete operations.
    /// </exception>
    public Task<bool> DeleteAsync(
        string queue,
        string receiptHandle,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "SNS does not support message deletion. Use SqsMessagingProvider for delete operations.");
    }

    /// <inheritdoc/>
    public Task<string> SubscribeAsync(
        string topic, string endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        RequireTenant();
        throw new NotSupportedException(
            "Shared-topic subscriptions must be provisioned by the platform, not tenant messaging code.");
    }
    /// <summary>
    /// Disposes the SNS client if it was created.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsClient && _snsClient.IsValueCreated)
        {
            _snsClient.Value.Dispose();
            _logger.LogInformation("SnsMessagingProvider client disposed");
        }
    }

    private void ValidateOptions()
    {
        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(SnsMessagingProvider),
                "Invalid messaging configuration");
        }

    }

    internal static IAmazonSimpleNotificationService CreateClient(MessagingOptions options)
    {
        options.Validate();
        var config = new AmazonSimpleNotificationServiceConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
        };

        if (options.UseLocalEmulator && options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = options.LocalEmulatorUrl.AbsoluteUri;
        }

        return new AmazonSimpleNotificationServiceClient(config);
    }

    private string ResolveTopicArn(string destination)
    {
        return ResolveTopicArn(destination, MessageScope.Tenant);
    }

    /// <summary>
    /// Resolves topic ARN with tenant isolation support and caching.
    /// SECURITY: Never trusts external tenant ID - always resolves from ITenantContext.
    /// Cache key includes (destination, tenantId, scope) to prevent cross-tenant leakage.
    /// Tenant IDs are normalized to lowercase to prevent duplicate entries.
    /// </summary>
    private string ResolveTopicArn(string destination, MessageScope scope)
    {
        // Caching disabled if TTL is zero
        if (_options.CacheTtl == TimeSpan.Zero)
        {
            return ResolveTopicArnInternal(destination, scope);
        }

        // Build cache key: (destination, tenant, scope) with normalized tenant ID
        var tenantId = scope == MessageScope.Tenant && _tenantContext?.IsResolved == true
            ? _tenantContext.TenantId.Value
            : string.Empty;
        
        var cacheKey = new MessagingResourceCacheKey(destination, tenantId, scope);

        // Try cache first - check if entry exists and is not expired
        if (_arnCache.TryGetValue(cacheKey, out var cachedEntry))
        {
            if (!cachedEntry.IsExpired())
            {
                cachedEntry.Touch(); // Update last accessed time for LRU
                _cacheHits.Add(1, new TagList { { "resource_type", "topic" } });
                _logger.LogDebug("Cache hit for topic ARN: {Destination} (tenant: {TenantId}, scope: {Scope})",
                    destination, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope);
                return cachedEntry.Value;
            }

            // Entry expired - remove it
            _arnCache.TryRemove(cacheKey, out _);
            _logger.LogDebug("Expired cache entry removed for topic: {Destination} (tenant: {TenantId}, scope: {Scope})",
                destination, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope);
        }

        _cacheMisses.Add(1, new TagList { { "resource_type", "topic" } });

        // Cache miss or expired - resolve the ARN
        var resolvedArn = ResolveTopicArnInternal(destination, scope);

        // Cache the resolved ARN with TTL
        var entry = new MessagingResourceCacheEntry(resolvedArn, _options.CacheTtl, _timeProvider);
        _arnCache.TryAdd(cacheKey, entry);

        // Evict LRU entries if cache size exceeds max
        if (_options.MaxCacheSize > 0 && _arnCache.Count > _options.MaxCacheSize)
        {
            EvictLruEntries(_arnCache, _options.MaxCacheSize);
        }
        
        _logger.LogDebug("Resolved and cached topic ARN: {Destination} → {Arn} (tenant: {TenantId}, scope: {Scope}, TTL: {Ttl})",
            destination, resolvedArn, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope, _options.CacheTtl);

        return resolvedArn;
    }

    /// <summary>
    /// Internal method to resolve topic ARN without caching.
    /// Separated from caching logic for clarity and testability.
    /// Phase 1: Uses shared topics with message-attribute based tenant isolation.
    /// Per-tenant resources (separate topics per tenant) are not yet implemented.
    /// </summary>
    private string ResolveTopicArnInternal(string destination, MessageScope scope)
    {
        // First check if there's an explicit mapping (for backward compatibility)
        if (_options.Sns.TopicArnMappings.TryGetValue(destination, out var mappedArn))
        {
            return mappedArn;
        }

        // Phase 1: Shared topic model - all tenants use the same physical topic
        // Tenant isolation is enforced via message attributes (pervaxis_tenant_id)
        // TODO Phase 2: Add SharedTopicNameFormat to SnsOptions (currently using TenantTopicNameTemplate as default)
        string topicName = _options.Sns.TenantTopicNameTemplate
            .Replace("{topicName}", destination, StringComparison.Ordinal)
            .Replace("{tenantId}", "shared", StringComparison.Ordinal); // Temporary: use "shared" suffix

        _logger.LogDebug(
            "Resolved topic name (shared model): {Destination} → {TopicName}",
            destination, topicName);

        // If topic name looks like a full ARN, return it as-is
        string resolvedArn;
        if (topicName.StartsWith("arn:aws:sns:", StringComparison.OrdinalIgnoreCase))
        {
            resolvedArn = topicName;
        }
        // Otherwise, construct topic ARN from topic name
        // Format: arn:aws:sns:{region}:{accountId}:{topicName}
        else if (!string.IsNullOrWhiteSpace(_options.Sns.DefaultTopicArn))
        {
            // Replace the topic name in default ARN
            var baseArn = _options.Sns.DefaultTopicArn;
            var lastColon = baseArn.LastIndexOf(':');
            if (lastColon > 0)
            {
                resolvedArn = baseArn[..lastColon] + ":" + topicName;
            }
            else
            {
                resolvedArn = topicName; // Fallback
            }
        }
        else
        {
            // Fallback: return topic name (works with LocalStack)
            resolvedArn = topicName;
        }

        return resolvedArn;
    }

    /// <summary>
    /// Invalidates the cache for a specific topic ARN after a not-found error.
    /// </summary>
    private void InvalidateTopicCache(string destination, MessageScope scope)
    {
        var tenantId = scope == MessageScope.Tenant && _tenantContext?.IsResolved == true
            ? _tenantContext.TenantId.Value
            : string.Empty;
        
        var cacheKey = new MessagingResourceCacheKey(destination, tenantId, scope);

        if (_arnCache.TryRemove(cacheKey, out _))
        {
            _cacheInvalidations.Add(1, new TagList { { "resource_type", "topic" } });
            _logger.LogInformation("Invalidated cache for topic: {Destination} (tenant: {TenantId}, scope: {Scope})",
                destination, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope);
        }
    }

    /// <summary>
    /// Evicts least recently used entries when cache size exceeds maximum.
    /// Removes 10% of entries (sorted by LastAccessedAt) to reduce thrashing.
    /// THREAD-SAFE: Snapshot is taken before eviction to avoid concurrent modification issues.
    /// Only removes entries that are still old after snapshot to avoid removing just-added entries.
    /// 
    /// NOTE: The 1-second recently-added protection window allows cache to temporarily exceed max size
    /// under high burst load. This is by design - prevents race conditions where just-added entries
    /// are immediately evicted. Cache will shrink below max size once entries age past 1 second.
    /// </summary>
    private void EvictLruEntries(
        ConcurrentDictionary<MessagingResourceCacheKey, MessagingResourceCacheEntry> cache,
        int maxSize)
    {
        var currentSize = cache.Count;
        if (currentSize <= maxSize)
        {
            return;
        }

        // Remove 10% of entries to avoid frequent evictions
        var targetRemoveCount = Math.Max(1, currentSize / 10);

        // SAFETY: Take snapshot of entries to avoid enumeration issues under concurrent access
        // ToArray() creates a defensive copy so concurrent modifications won't throw
        var snapshot = cache.ToArray();

        // Sort by LastAccessedAt and take oldest entries from snapshot
        var entriesToRemove = snapshot
            .OrderBy(kvp => kvp.Value.LastAccessedAt)
            .Take(targetRemoveCount)
            .ToList();

        var removedCount = 0;
        var now = _timeProvider.GetUtcNow();

        foreach (var kvp in entriesToRemove)
        {
            // SAFETY: Double-check the entry is still old before removing
            // This prevents removing entries that were just added or accessed by another thread
            if (cache.TryGetValue(kvp.Key, out var currentEntry))
            {
                // Only remove if entry hasn't been accessed recently (within last second)
                var timeSinceLastAccess = now - currentEntry.LastAccessedAt;
                if (timeSinceLastAccess.TotalSeconds >= 1.0)
                {
                    // TryRemove on ConcurrentDictionary never throws
                    if (cache.TryRemove(kvp.Key, out _))
                    {
                        removedCount++;
                    }
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation(
                "Evicted {Count} LRU cache entries (cache size: {CurrentSize} → {NewSize}, max: {MaxSize})",
                removedCount, currentSize, cache.Count, maxSize);
        }
    }

    private string Serialize<T>(T value) => JsonSerializer.Serialize(value, _jsonOptions);

    private static JsonSerializerOptions BuildJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private void AddTenantAttributes(Dictionary<string, MessageAttributeValue> attributes)
    {
        if (attributes.Keys.Any(k => k.StartsWith(MessagingConstants.ReservedAttributePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new GenesisException(nameof(SnsMessagingProvider),
                "Reserved message attributes cannot be set by callers.",
                MessagingConstants.FailureReasons.ReservedAttributeSupplied, null);
        }
        attributes[MessagingConstants.TenantIdAttribute] = new MessageAttributeValue
        {
            DataType = "String", StringValue = RequireTenant()
        };
    }

    private string RequireTenant()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _guard.RequireTenant();
    }
    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }

    private TagList GetMetricTags(string operation, string result, string provider)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "result", result },
            { "provider", provider }
        };
        if (_tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }
        return tags;
    }
}
