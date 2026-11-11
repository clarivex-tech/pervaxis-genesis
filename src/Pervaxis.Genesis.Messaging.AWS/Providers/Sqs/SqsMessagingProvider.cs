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
using Amazon.SQS;
using Amazon.SQS.Model;
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

namespace Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;

/// <summary>
/// SQS implementation of the <see cref="IMessaging"/> abstraction.
/// Supports single and batch publish, receive with long polling, and message deletion.
/// </summary>
/// <remarks>
/// <see cref="SubscribeAsync"/> is not supported by SQS and will throw <see cref="NotSupportedException"/>.
/// For the full consumer pattern (receive then delete), use the receipt handle returned
/// in the SQS message; call <see cref="DeleteAsync"/> with the queue name and that handle.
/// </remarks>
public sealed class SqsMessagingProvider : IMessaging, IDisposable
{
    private const int SqsMaxBatchSize = 10;

    private readonly MessagingOptions _options;
    private readonly ILogger<SqsMessagingProvider> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IAmazonSQS> _sqsClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly TimeProvider _timeProvider;

    // URL cache: thread-safe, keyed by (destination, tenant, scope)
    // Entries expire after configured TTL to prevent unbounded growth
    private readonly ConcurrentDictionary<MessagingResourceCacheKey, MessagingResourceCacheEntry> _urlCache = new();

    // Metrics
    private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.operations",
        "1",
        "Total number of messaging operations");

    private static readonly Counter<long> _messagesSent = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.messages.sent",
        "1",
        "Total number of messages sent");

    private static readonly Counter<long> _messagesReceived = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.messages.received",
        "1",
        "Total number of messages received");

    private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.messaging.operation.duration",
        "ms",
        "Duration of messaging operations in milliseconds");

    private static readonly Counter<long> _cacheHits = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.cache.hits",
        "1",
        "Number of cache hits for URL resolution");

    private static readonly Counter<long> _cacheMisses = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.cache.misses",
        "1",
        "Number of cache misses for URL resolution");

    private static readonly Counter<long> _cacheInvalidations = PervaxisMeter.CreateCounter<long>(
        "genesis.messaging.cache.invalidations",
        "1",
        "Number of cache invalidations due to not-found errors");

    /// <summary>
    /// Initializes a new instance of <see cref="SqsMessagingProvider"/> for production use.
    /// </summary>
    /// <param name="options">Messaging configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    /// <param name="environment">Optional environment name for validation (defaults to Production).</param>
    /// <param name="timeProvider">Optional time provider for testing (defaults to system time).</param>
    public SqsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SqsMessagingProvider> logger,
        ITenantContext? tenantContext = null,
        string? environment = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions(environment);

        _sqsClient = new Lazy<IAmazonSQS>(CreateClient);
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "SqsMessaging");

        _logger.LogInformation(
            "SqsMessagingProvider initialized for region {Region}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.Region,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SqsMessagingProvider"/> with an explicit
    /// SQS client — intended for unit testing only.
    /// </summary>
    internal SqsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SqsMessagingProvider> logger,
        IAmazonSQS sqsClient,
        ITenantContext? tenantContext = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(sqsClient);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions("Test"); // Internal constructor always allows disabled isolation

        _sqsClient = new Lazy<IAmazonSQS>(() => sqsClient);
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "SqsMessaging");
    }

    /// <inheritdoc/>
    public async Task<string> PublishAsync<T>(
        string destination,
        T message,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "sqs");
        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.operation", "publish");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(message);

        var queueUrl = ResolveQueueUrl(destination);
        var body = Serialize(message);

        try
        {
            var request = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>()
            };

            AddTenantAttributes(request.MessageAttributes);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _sqsClient.Value.SendMessageAsync(request, ct).ConfigureAwait(false),
                cancellationToken);

            activity?.SetTag("messaging.message_id", response.MessageId);
            _logger.LogDebug(
                "Published message {MessageId} to SQS queue {QueueUrl}",
                response.MessageId, queueUrl);

            // Record metrics
            var tags = GetMetricTags("publish", "success", "sqs");
            _operationsCounter.Add(1, tags);
            _messagesSent.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return response.MessageId;
        }
        catch (QueueDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SQS queue {QueueUrl} does not exist. Resource must be provisioned.", queueUrl);

            // Invalidate cache on not-found error
            InvalidateQueueCache(destination, MessageScope.Tenant);

            var errorTags = GetMetricTags("publish", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                $"Queue '{queueUrl}' not provisioned. Ensure tenant messaging resources are created before use.",
                ex,
                "QUEUE_NOT_FOUND",
                new Dictionary<string, object> { ["QueueUrl"] = queueUrl });
        }
        catch (AmazonSQSException ex) when (ex.ErrorCode == "AccessDenied" || ex.ErrorCode == "ThrottlingException" || ex.ErrorCode == "Throttling")
        {
            // Pass through access denied and throttling unchanged for IAM policy detection
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
            _logger.LogError(ex, "SQS publish operation failed");

            var errorTags = GetMetricTags("publish", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                "SQS publish operation failed. See inner exception for details.",
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
        activity?.SetTag("messaging.system", "sqs");
        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.operation", "publish_batch");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(messages);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return [];
        }

        activity?.SetTag("messaging.message_count", messageList.Count);
        var queueUrl = ResolveQueueUrl(destination);
        var allMessageIds = new List<string>();

        try
        {
            foreach (var chunk in messageList.Chunk(SqsMaxBatchSize))
            {
                var entries = chunk
                    .Select((msg, idx) =>
                    {
                        var entry = new SendMessageBatchRequestEntry
                        {
                            Id = idx.ToString(CultureInfo.InvariantCulture),
                            MessageBody = Serialize(msg),
                            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
                        };
                        AddTenantAttributes(entry.MessageAttributes);
                        return entry;
                    })
                    .ToList();

                var request = new SendMessageBatchRequest
                {
                    QueueUrl = queueUrl,
                    Entries = entries
                };

                var response = await _resiliencePipeline.ExecuteAsync(
                    async ct => await _sqsClient.Value.SendMessageBatchAsync(request, ct).ConfigureAwait(false),
                    cancellationToken);

                if (response.Failed.Count > 0)
                {
                    _logger.LogError(
                        "SQS batch publish had {FailureCount} failures to queue {QueueUrl}. Failed IDs: {FailedIds}",
                        response.Failed.Count, queueUrl,
                        string.Join(", ", response.Failed.Select(f => f.Id)));
                }

                allMessageIds.AddRange(response.Successful.Select(s => s.MessageId));

                _logger.LogDebug(
                    "Published batch of {Count} messages to SQS queue {QueueUrl}",
                    response.Successful.Count, queueUrl);
            }

            activity?.SetTag("messaging.success_count", allMessageIds.Count);

            // Record metrics
            var tags = GetMetricTags("publish_batch", "success", "sqs");
            _operationsCounter.Add(1, tags);
            _messagesSent.Add(allMessageIds.Count, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return allMessageIds;
        }
        catch (QueueDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SQS queue {QueueUrl} does not exist. Resource must be provisioned.", queueUrl);

            // Invalidate cache on not-found error
            InvalidateQueueCache(destination, MessageScope.Tenant);

            var errorTags = GetMetricTags("publish_batch", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                $"Queue '{queueUrl}' not provisioned. Ensure tenant messaging resources are created before use.",
                ex,
                "QUEUE_NOT_FOUND",
                new Dictionary<string, object> { ["QueueUrl"] = queueUrl });
        }
        catch (AmazonSQSException ex) when (ex.ErrorCode == "AccessDenied" || ex.ErrorCode == "ThrottlingException" || ex.ErrorCode == "Throttling")
        {
            // Pass through access denied and throttling unchanged for IAM policy detection
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
            _logger.LogError(ex, "SQS batch publish operation failed");

            var errorTags = GetMetricTags("publish_batch", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                "SQS batch publish operation failed. See inner exception for details.",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<T>> ReceiveAsync<T>(
        string queue,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.receive", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "sqs");
        activity?.SetTag("messaging.destination", queue);
        activity?.SetTag("messaging.operation", "receive");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        var queueUrl = ResolveQueueUrl(queue);
        var effectiveMax = Math.Clamp(maxMessages, 1, SqsMaxBatchSize);

        try
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = effectiveMax,
                WaitTimeSeconds = _options.Sqs.WaitTimeSeconds,
                VisibilityTimeout = _options.Sqs.VisibilityTimeoutSeconds,
                MessageAttributeNames = new List<string> { "All" }  // Phase 1B: Receive all attributes including pervaxis_tenant_id
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _sqsClient.Value.ReceiveMessageAsync(request, ct).ConfigureAwait(false),
                cancellationToken);

            activity?.SetTag("messaging.message_count", response.Messages.Count);
            _logger.LogDebug(
                "Received {Count} messages from SQS queue {QueueUrl}",
                response.Messages.Count, queueUrl);

            // Phase 1B: Process messages with tenant isolation validation
            var result = new List<T>();
            foreach (var message in response.Messages)
            {
                var deserialized = Deserialize<T>(message.Body);
                if (deserialized is null)
                {
                    _logger.LogWarning("Failed to deserialize message {MessageId}, skipping", message.MessageId);
                    continue;
                }

                // Phase 1B: Extract and validate tenant ID from message attributes
                if (_options.EnableTenantIsolation)
                {
                    var validationResult = ValidateAndExtractTenantId(message);
                    if (!validationResult.IsValid)
                    {
                        // Send to DLQ and continue to next message
                        await SendToDlqAsync(message, validationResult.FailureReason!, queueUrl, cancellationToken);
                        
                        // Delete the invalid message from the original queue
                        await DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
                        continue;
                    }

                    // Valid message: tenant ID extracted successfully
                    _logger.LogDebug(
                        "Message {MessageId} validated with tenant {TenantId}",
                        message.MessageId, validationResult.TenantId);
                }

                result.Add(deserialized);
            }

            // Record metrics
            var tags = GetMetricTags("receive", "success", "sqs");
            _operationsCounter.Add(1, tags);
            _messagesReceived.Add(result.Count, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return result;
        }
        catch (QueueDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SQS queue {QueueUrl} does not exist. Resource must be provisioned.", queueUrl);

            // Invalidate cache on not-found error
            InvalidateQueueCache(queue, MessageScope.Tenant);

            var errorTags = GetMetricTags("receive", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                $"Queue '{queueUrl}' not provisioned. Ensure tenant messaging resources are created before use.",
                ex,
                "QUEUE_NOT_FOUND",
                new Dictionary<string, object> { ["QueueUrl"] = queueUrl });
        }
        catch (AmazonSQSException ex) when (ex.ErrorCode == "AccessDenied" || ex.ErrorCode == "ThrottlingException" || ex.ErrorCode == "Throttling")
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
            _logger.LogError(ex, "SQS receive operation failed");

            var errorTags = GetMetricTags("receive", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                "SQS receive operation failed. See inner exception for details.",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        string queue,
        string receiptHandle,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.delete", ActivityKind.Client);
        activity?.SetTag("messaging.system", "sqs");
        activity?.SetTag("messaging.destination", queue);
        activity?.SetTag("messaging.operation", "delete");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptHandle);

        var queueUrl = ResolveQueueUrl(queue);

        try
        {
            var request = new DeleteMessageRequest
            {
                QueueUrl = queueUrl,
                ReceiptHandle = receiptHandle
            };

            await _resiliencePipeline.ExecuteAsync(
                async ct => await _sqsClient.Value.DeleteMessageAsync(request, ct).ConfigureAwait(false),
                cancellationToken);

            activity?.SetTag("messaging.success", true);
            _logger.LogDebug("Deleted message from SQS queue {QueueUrl}", queueUrl);

            // Record metrics
            var tags = GetMetricTags("delete", "success", "sqs");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return true;
        }
        catch (QueueDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SQS queue {QueueUrl} does not exist. Resource must be provisioned.", queueUrl);

            // Invalidate cache on not-found error
            InvalidateQueueCache(queue, MessageScope.Tenant);

            var errorTags = GetMetricTags("delete", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                $"Queue '{queueUrl}' not provisioned. Ensure tenant messaging resources are created before use.",
                ex,
                "QUEUE_NOT_FOUND",
                new Dictionary<string, object> { ["QueueUrl"] = queueUrl });
        }
        catch (AmazonSQSException ex) when (ex.ErrorCode == "AccessDenied" || ex.ErrorCode == "ThrottlingException" || ex.ErrorCode == "Throttling")
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
            _logger.LogError(ex, "SQS delete operation failed");

            var errorTags = GetMetricTags("delete", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                "SQS delete operation failed. See inner exception for details.",
                ex);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// SQS does not support topic subscriptions. Use <see cref="Providers.Sns.SnsMessagingProvider"/> for pub/sub.
    /// </exception>
    public Task<string> SubscribeAsync(
        string topic,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "SQS does not support topic subscriptions. Use SnsMessagingProvider for pub/sub.");
    }

    /// <summary>
    /// Disposes the SQS client if it was created.
    /// </summary>
    public void Dispose()
    {
        if (_sqsClient.IsValueCreated)
        {
            _sqsClient.Value.Dispose();
            _logger.LogInformation("SqsMessagingProvider client disposed");
        }
    }

    private void ValidateOptions(string? environment = null)
    {
        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(SqsMessagingProvider),
                "Invalid messaging configuration");
        }

        // SECURITY: Tenant isolation can ONLY be disabled in Development and Test environments (allow-list)
        if (!_options.EnableTenantIsolation)
        {
            var env = environment ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty;
            
            // Allow-list: only Development and Test can disable isolation
            var isAllowed = env.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
                           env.Equals("Test", StringComparison.OrdinalIgnoreCase);
            
            if (!isAllowed)
            {
                // Fail closed: missing env or any non-allowed environment is rejected
                var envDisplay = string.IsNullOrEmpty(env) ? "(not set)" : env;
                throw new GenesisConfigurationException(
                    nameof(SqsMessagingProvider),
                    $"Tenant isolation cannot be disabled in environment '{envDisplay}'. " +
                    "EnableTenantIsolation=false is ONLY allowed in 'Development' or 'Test' environments.");
            }
        }
    }

    private IAmazonSQS CreateClient()
    {
        var config = new AmazonSQSConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region)
        };

        if (_options.UseLocalEmulator && _options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = _options.LocalEmulatorUrl.AbsoluteUri;
            _logger.LogInformation("SqsMessagingProvider using local emulator: {Url}", _options.LocalEmulatorUrl);
        }

        return new AmazonSQSClient(config);
    }

    private string ResolveQueueUrl(string destination)
    {
        return ResolveQueueUrl(destination, MessageScope.Tenant);
    }

    /// <summary>
    /// Resolves queue URL with tenant isolation support and caching.
    /// SECURITY: Never trusts external tenant ID - always resolves from ITenantContext.
    /// Cache key includes (destination, tenantId, scope) to prevent cross-tenant leakage.
    /// Tenant IDs are normalized to lowercase to prevent duplicate entries.
    /// </summary>
    private string ResolveQueueUrl(string destination, MessageScope scope)
    {
        // Caching disabled if TTL is zero
        if (_options.CacheTtl == TimeSpan.Zero)
        {
            return ResolveQueueUrlInternal(destination, scope);
        }

        // Build cache key: (destination, tenant, scope) with normalized tenant ID
        var tenantId = scope == MessageScope.Tenant && _tenantContext?.IsResolved == true
            ? _tenantContext.TenantId.Value
            : string.Empty;
        
        var cacheKey = new MessagingResourceCacheKey(destination, tenantId, scope);

        // Try cache first - check if entry exists and is not expired
        if (_urlCache.TryGetValue(cacheKey, out var cachedEntry))
        {
            if (!cachedEntry.IsExpired())
            {
                cachedEntry.Touch(); // Update last accessed time for LRU
                _cacheHits.Add(1, new TagList { { "resource_type", "queue" } });
                _logger.LogDebug("Cache hit for queue URL: {Destination} (tenant: {TenantId}, scope: {Scope})",
                    destination, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope);
                return cachedEntry.Value;
            }

            // Entry expired - remove it
            _urlCache.TryRemove(cacheKey, out _);
            _logger.LogDebug("Expired cache entry removed for queue: {Destination} (tenant: {TenantId}, scope: {Scope})",
                destination, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope);
        }

        _cacheMisses.Add(1, new TagList { { "resource_type", "queue" } });

        // Cache miss or expired - resolve the URL
        var resolvedUrl = ResolveQueueUrlInternal(destination, scope);

        // Cache the resolved URL with TTL
        var entry = new MessagingResourceCacheEntry(resolvedUrl, _options.CacheTtl, _timeProvider);
        _urlCache.TryAdd(cacheKey, entry);

        // Evict LRU entries if cache size exceeds max
        if (_options.MaxCacheSize > 0 && _urlCache.Count > _options.MaxCacheSize)
        {
            EvictLruEntries(_urlCache, _options.MaxCacheSize);
        }
        
        _logger.LogDebug("Resolved and cached queue URL: {Destination} → {Url} (tenant: {TenantId}, scope: {Scope}, TTL: {Ttl})",
            destination, resolvedUrl, string.IsNullOrEmpty(tenantId) ? "none" : tenantId, scope, _options.CacheTtl);

        return resolvedUrl;
    }

    /// <summary>
    /// Internal method to resolve queue URL without caching.
    /// Separated from caching logic for clarity and testability.
    /// Phase 1: Uses shared queues with message-attribute based tenant isolation.
    /// Per-tenant resources (separate queues per tenant) are not yet implemented.
    /// </summary>
    private string ResolveQueueUrlInternal(string destination, MessageScope scope)
    {
        // First check if there's an explicit mapping (for backward compatibility)
        if (_options.Sqs.QueueUrlMappings.TryGetValue(destination, out var mappedUrl))
        {
            return mappedUrl;
        }

        // Phase 1: Shared queue model - all tenants use the same physical queue
        // Tenant isolation is enforced via message attributes (pervaxis_tenant_id)
        // Format: {queueName}-shared
        string queueName = _options.Sqs.SharedQueueNameFormat
            .Replace("{queueName}", destination, StringComparison.Ordinal);

        _logger.LogDebug(
            "Resolved queue name (shared model): {Destination} → {QueueName}",
            destination, queueName);

        // If queue name looks like a full URL, return it as-is
        if (queueName.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            queueName.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return queueName;
        }

        // Otherwise, construct queue URL from queue name
        // Format: https://sqs.{region}.amazonaws.com/{accountId}/{queueName}
        string resolvedUrl;
        if (!string.IsNullOrWhiteSpace(_options.Sqs.DefaultQueueUrl))
        {
            // Replace the queue name in default URL
            var baseUrl = _options.Sqs.DefaultQueueUrl;
            var lastSlash = baseUrl.LastIndexOf('/');
            if (lastSlash > 0)
            {
                resolvedUrl = baseUrl[..lastSlash] + "/" + queueName;
            }
            else
            {
                resolvedUrl = queueName; // Fallback
            }
        }
        else
        {
            // Fallback: return queue name (works with LocalStack)
            resolvedUrl = queueName;
        }

        return resolvedUrl;
    }

    /// <summary>
    /// Invalidates the cache for a specific queue URL after a not-found error.
    /// </summary>
    private void InvalidateQueueCache(string destination, MessageScope scope)
    {
        var tenantId = scope == MessageScope.Tenant && _tenantContext?.IsResolved == true
            ? _tenantContext.TenantId.Value
            : string.Empty;
        
        var cacheKey = new MessagingResourceCacheKey(destination, tenantId, scope);

        if (_urlCache.TryRemove(cacheKey, out _))
        {
            _cacheInvalidations.Add(1, new TagList { { "resource_type", "queue" } });
            _logger.LogInformation("Invalidated cache for queue: {Destination} (tenant: {TenantId}, scope: {Scope})",
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

    private T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, _jsonOptions);

    private static JsonSerializerOptions BuildJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private void AddTenantAttributes(Dictionary<string, MessageAttributeValue> attributes)
    {
        // SECURITY: First, reject any caller-supplied reserved attributes (pervaxis_* prefix)
        // This prevents callers from spoofing tenant IDs or other platform-controlled metadata
        var reservedKeys = attributes.Keys
            .Where(k => k.StartsWith(MessagingConstants.ReservedAttributePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (reservedKeys.Count > 0)
        {
            _logger.LogError(
                "Caller attempted to supply reserved message attributes: {ReservedKeys}. " +
                "Attributes with prefix '{Prefix}' are platform-controlled and will be rejected.",
                string.Join(", ", reservedKeys),
                MessagingConstants.ReservedAttributePrefix);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                $"Reserved message attributes cannot be set by callers. " +
                $"The following reserved attributes were rejected: {string.Join(", ", reservedKeys)}. " +
                $"Attributes prefixed with '{MessagingConstants.ReservedAttributePrefix}' are platform-controlled.",
                MessagingConstants.FailureReasons.ReservedAttributeSupplied,
                new Dictionary<string, object>
                {
                    ["ReservedKeys"] = reservedKeys,
                    ["ReservedPrefix"] = MessagingConstants.ReservedAttributePrefix
                });
        }

        // SECURITY: Fail closed - if tenant isolation is enabled but context not resolved, reject
        if (_options.EnableTenantIsolation)
        {
            if (_tenantContext?.IsResolved != true)
            {
                _logger.LogError(
                    "Tenant isolation is enabled but ITenantContext is not resolved. " +
                    "Cannot publish message without tenant context.");

                throw new GenesisException(
                    nameof(SqsMessagingProvider),
                    "Tenant context not resolved. Cannot publish messages when tenant isolation is enabled. " +
                    "Ensure ITenantContext is properly configured and a tenant has been resolved for this request.",
                    "TENANT_CONTEXT_NOT_RESOLVED",
                    null);
            }

            // Inject tenant ID from ITenantContext (NEVER trust caller-supplied values)
            attributes[MessagingConstants.TenantIdAttribute] = new MessageAttributeValue
            {
                DataType = "String",
                StringValue = _tenantContext.TenantId.Value
            };

            _logger.LogDebug(
                "Stamped message with tenant ID: {TenantId}",
                _tenantContext.TenantId.Value);
        }
    }

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
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

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }

        return tags;
    }

    /// <summary>
    /// Phase 1B: Validates and extracts tenant ID from message attributes.
    /// </summary>
    private (bool IsValid, string? TenantId, string? FailureReason) ValidateAndExtractTenantId(Message message)
    {
        // Check if pervaxis_tenant_id attribute exists
        if (!message.MessageAttributes.TryGetValue(MessagingConstants.TenantIdAttribute, out var tenantIdAttr))
        {
            _logger.LogWarning(
                "Message {MessageId} is missing required attribute {AttributeName}",
                message.MessageId, MessagingConstants.TenantIdAttribute);
            return (false, null, MessagingConstants.FailureReasons.MissingTenantId);
        }

        var tenantIdValue = tenantIdAttr.StringValue;

        // Validate tenant ID format - must be a valid GUID in lowercase hyphenated format
        if (string.IsNullOrWhiteSpace(tenantIdValue) || 
            !Guid.TryParseExact(tenantIdValue, "D", out var guid) || 
            guid == Guid.Empty)
        {
            _logger.LogWarning(
                "Message {MessageId} has invalid tenant ID format: {TenantId}",
                message.MessageId, tenantIdValue);
            return (false, null, MessagingConstants.FailureReasons.InvalidTenantId);
        }

        return (true, tenantIdValue, null);
    }

    /// <summary>
    /// Phase 1B: Sends a message to the dead-letter queue with failure reason.
    /// </summary>
    private async Task SendToDlqAsync(
        Message originalMessage,
        string failureReason,
        string sourceQueueUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Sqs.DlqUrl))
        {
            _logger.LogWarning(
                "No DLQ configured. Dropping invalid message {MessageId} with reason: {FailureReason}",
                originalMessage.MessageId, failureReason);
            return;
        }

        try
        {
            var dlqRequest = new SendMessageRequest
            {
                QueueUrl = _options.Sqs.DlqUrl,
                MessageBody = originalMessage.Body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>(originalMessage.MessageAttributes)
                {
                    [MessagingConstants.FailureReasonAttribute] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = failureReason
                    }
                }
            };

            await _sqsClient.Value.SendMessageAsync(dlqRequest, cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "Sent invalid message {MessageId} to DLQ with reason: {FailureReason}",
                originalMessage.MessageId, failureReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send message {MessageId} to DLQ. Message will remain in source queue.",
                originalMessage.MessageId);
            throw;
        }
    }

    /// <summary>
    /// Phase 1B: Deletes a message from the queue after processing or DLQ routing.
    /// </summary>
    private async Task DeleteMessageAsync(string queueUrl, string receiptHandle, CancellationToken cancellationToken)
    {
        try
        {
            await _sqsClient.Value.DeleteMessageAsync(
                new DeleteMessageRequest
                {
                    QueueUrl = queueUrl,
                    ReceiptHandle = receiptHandle
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to delete message with receipt handle {ReceiptHandle} from queue {QueueUrl}",
                receiptHandle, queueUrl);
            // Don't throw - this is a best-effort cleanup
        }
    }
}
