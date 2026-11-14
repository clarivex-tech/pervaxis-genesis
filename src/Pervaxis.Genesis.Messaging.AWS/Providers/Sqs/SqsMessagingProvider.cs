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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.Base.Validation;
using Pervaxis.Genesis.Messaging.AWS.Caching;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Security;

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
public sealed class SqsMessagingProvider : IMessaging, ISharedQueueConsumer, IDisposable
{
    private const int SqsMaxBatchSize = 10;

    private readonly MessagingOptions _options;
    private readonly ILogger<SqsMessagingProvider> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly TenantMessagingGuard _guard;
    private readonly bool _ownsClient;
    private bool _disposed;
    private readonly bool _sharedConsumer;
    private readonly IServiceScopeFactory? _serviceScopeFactory;
    private readonly ConcurrentDictionary<(string Queue, string Receipt), string> _receipts = new();
    private readonly ITenantScopeFactory? _tenantScopeFactory;
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
    /// <param name="tenantContext">Current tenant context; publication fails when it is unresolved.</param>
    /// <param name="tenantScopeFactory">Legacy parameter. Use AddGenesisSharedQueueConsumer for trusted shared consumption.</param>
    /// <param name="environment">Legacy compatibility parameter; isolation cannot be disabled in any environment.</param>
    /// <param name="timeProvider">Optional time provider for testing (defaults to system time).</param>
    public SqsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SqsMessagingProvider> logger,
        ITenantContext? tenantContext = null,
        ITenantScopeFactory? tenantScopeFactory = null,
        string? environment = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _guard = new TenantMessagingGuard(tenantContext);
        _tenantScopeFactory = tenantScopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions();

        _ownsClient = true;
        _sqsClient = new Lazy<IAmazonSQS>(() => CreateClient(_options));
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "SqsMessaging");

        _logger.LogInformation(
            "SqsMessagingProvider initialized for region {Region}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.Region,
            _tenantContext?.IsResolved == true,
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
        TimeProvider? timeProvider = null,
        ITenantScopeFactory? tenantScopeFactory = null,
        bool sharedConsumer = false,
        IServiceScopeFactory? serviceScopeFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(sqsClient);

        _sharedConsumer = sharedConsumer;
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _guard = new TenantMessagingGuard(tenantContext);
        _tenantScopeFactory = tenantScopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions();

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
        RequireTenant();
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

        RequireTenant();
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
    /// <remarks>
    /// Returns only messages owned by the current tenant. Invalid envelopes are quarantined.
    /// For automatic tenant scoping, use <see cref="ISharedQueueConsumer.ProcessAsync{T}"/> instead.
    /// For receipt handles, use <see cref="ReceiveWithMetadataAsync{T}"/> in the same provider scope.
    /// </remarks>
    public async Task<IEnumerable<T>> ReceiveAsync<T>(
        string queue, int maxMessages = 10, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        RequireTenant();
        if (_sharedConsumer)
        {
            throw new InvalidOperationException("Use the shared consumer's scoped processing API.");
        }
        var messages = await ReceiveWithMetadataAsync<T>(queue, maxMessages, cancellationToken);
        return messages.Select(m => m.Message).ToList();
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

        var tenant = RequireTenant();
        var queueUrl = ResolveQueueUrl(queue);

        if (!_receipts.TryGetValue((queueUrl, receiptHandle), out var owner)
            || !StringComparer.Ordinal.Equals(owner, tenant))
        {
            throw new GenesisException(nameof(SqsMessagingProvider),
                "Receipt handle was not received by this tenant in this provider scope.",
                "RECEIPT_TENANT_MISMATCH", null);
        }
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

            _receipts.TryRemove((queueUrl, receiptHandle), out _);
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
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsClient && _sqsClient.IsValueCreated)
        {
            _sqsClient.Value.Dispose();
            _logger.LogInformation("SqsMessagingProvider client disposed");
        }
    }

    private void ValidateOptions()
    {
        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(SqsMessagingProvider),
                "Invalid messaging configuration");
        }

    }

    /// <summary>
    /// Receives validated messages with tenant IDs and receipts. Tenant providers return
    /// only their current tenant; explicitly registered workers validate all tenants.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="queue">Queue name (logical identifier, not URL).</param>
    /// <param name="maxMessages">Maximum number of messages to receive (1-10, default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of received messages with metadata including tenant IDs.</returns>
    public async Task<IEnumerable<ReceivedMessage<T>>> ReceiveWithMetadataAsync<T>(
        string queue,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.receive_with_metadata", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "sqs");
        activity?.SetTag("messaging.destination", queue);
        activity?.SetTag("messaging.operation", "receive_with_metadata");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        ObjectDisposedException.ThrowIf(_disposed, this);
        string? currentTenant = null;
        if (_sharedConsumer)
        {
            _guard.RequireWorker(true);
        }
        else
        {
            currentTenant = RequireTenant();
        }
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
                MessageAttributeNames = new List<string> { "All" }
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _sqsClient.Value.ReceiveMessageAsync(request, ct).ConfigureAwait(false),
                cancellationToken);

            activity?.SetTag("messaging.message_count", (response.Messages?.Count ?? 0));

            if (_sharedConsumer)
            {
                _guard.RequireWorker(true);
            }
            else
            {
                RequireTenant();
            }
            var result = new List<ReceivedMessage<T>>();
            foreach (var message in response.Messages ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validation = ValidateAndExtractTenantId(message);
                if (!validation.IsValid)
                {
                    await SendToDlqAsync(message, validation.FailureReason!, queueUrl, cancellationToken);
                    await DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
                    continue;
                }
                if (currentTenant is not null && !StringComparer.Ordinal.Equals(currentTenant, validation.TenantId))
                {
                    await _sqsClient.Value.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = queueUrl, ReceiptHandle = message.ReceiptHandle, VisibilityTimeout = 0
                    }, cancellationToken);
                    continue;
                }
                T? payload;
                try
                {
                    payload = Deserialize<T>(message.Body);
                    if (payload is null)
                    {
                        throw new JsonException("Null message payload.");
                    }
                }
                catch (JsonException)
                {
                    await SendToDlqAsync(message, "invalid_payload", queueUrl, cancellationToken);
                    await DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(message.ReceiptHandle))
                {
                    throw new GenesisException(nameof(SqsMessagingProvider),
                        "Received message has no receipt handle.", "INVALID_RECEIPT_HANDLE", null);
                }
                _receipts[(queueUrl, message.ReceiptHandle)] = validation.TenantId!;
                result.Add(new ReceivedMessage<T>(payload, message.ReceiptHandle,
                    message.MessageId, validation.TenantId));
            }
            var tags = GetMetricTags("receive_with_metadata", "success", "sqs");
            _operationsCounter.Add(1, tags);
            _messagesReceived.Add(result.Count, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            if (!_sharedConsumer)
            {
                RequireTenant();
            }            return result;
        }
        catch (QueueDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SQS queue {QueueUrl} does not exist", queueUrl);
            InvalidateQueueCache(queue, MessageScope.Tenant);

            var errorTags = GetMetricTags("receive_with_metadata", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                $"Queue '{queueUrl}' not provisioned.",
                ex,
                "QUEUE_NOT_FOUND",
                new Dictionary<string, object> { ["QueueUrl"] = queueUrl });
        }
        catch (AmazonSQSException ex) when (ex.ErrorCode == "AccessDenied" || ex.ErrorCode == "ThrottlingException" || ex.ErrorCode == "Throttling")
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GenesisException)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "SQS receive with metadata operation failed");

            var errorTags = GetMetricTags("receive_with_metadata", "error", "sqs");
            _operationsCounter.Add(1, errorTags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, errorTags);

            throw new GenesisException(
                nameof(SqsMessagingProvider),
                "SQS receive with metadata operation failed.",
                ex);
        }
    }

    /// <summary>
    /// Legacy handler overload for explicitly registered workers. Each message receives
    /// tenant and DI scopes. Prefer the ISharedQueueConsumer overload to resolve scoped dependencies.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="queue">Queue name (logical identifier, not URL).</param>
    /// <param name="handler">Async handler function to process each message.</param>
    /// <param name="maxMessages">Maximum number of messages to receive (1-10, default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the async operation.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Per-message isolation by design: one failing handler is logged and left for SQS redelivery so the other messages still process.")]
    public Task ProcessAsync<T>(
        string queue, Func<T, Task> handler, int maxMessages = 10,
        CancellationToken cancellationToken = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return ProcessAsync<T>(queue, (message, _, _) => handler(message), maxMessages, cancellationToken);
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Failed messages remain unacknowledged for SQS redelivery; independent messages continue.")]
    public async Task ProcessAsync<T>(
        string queue, Func<T, IServiceProvider, CancellationToken, Task> handler,
        int maxMessages = 10, CancellationToken cancellationToken = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _guard.RequireWorker(_sharedConsumer);
        if (_tenantScopeFactory is null || _serviceScopeFactory is null)
        {
            throw new InvalidOperationException("Shared processing requires ITenantScopeFactory and IServiceScopeFactory.");
        }
        var queueUrl = ResolveQueueUrl(queue);
        var messages = await ReceiveWithMetadataAsync<T>(queue, maxMessages, cancellationToken);
        var tasks = messages.Select(async message =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var tenantScope = _tenantScopeFactory.BeginScope(new TenantId(message.TenantId!)))
                {
                    await using var services = _serviceScopeFactory.CreateAsyncScope();
                    var context = services.ServiceProvider.GetRequiredService<ITenantContext>();
                    if (!context.IsResolved || !StringComparer.Ordinal.Equals(context.TenantId.Value, message.TenantId))
                    {
                        throw new GenesisException(nameof(SqsMessagingProvider),
                            "Message handler tenant context does not match its envelope.", "TENANT_CONTEXT_MISMATCH", null);
                    }
                    await handler(message.Message, services.ServiceProvider, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!context.IsResolved || !StringComparer.Ordinal.Equals(context.TenantId.Value, message.TenantId))
                    {
                        throw new GenesisException(nameof(SqsMessagingProvider),
                            "Tenant changed while handling the message.", "TENANT_CONTEXT_CHANGED", null);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message {MessageId} failed for tenant {TenantId}; leaving it for redelivery.",
                    message.MessageId, message.TenantId);
            }
            finally
            {
                _receipts.TryRemove((queueUrl, message.ReceiptHandle), out _);
            }
        });
        await Task.WhenAll(tasks);
    }
    internal static IAmazonSQS CreateClient(MessagingOptions options)
    {
        options.Validate();
        var config = new AmazonSQSConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
        };

        if (options.UseLocalEmulator && options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = options.LocalEmulatorUrl.AbsoluteUri;
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
        if (attributes.Keys.Any(k => k.StartsWith(MessagingConstants.ReservedAttributePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new GenesisException(nameof(SqsMessagingProvider),
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

    /// <summary>
    /// Phase 1B: Validates and extracts tenant ID from message attributes.
    /// Uses TenantIdValidator to enforce platform-wide tenant ID rules.
    /// </summary>
    private (bool IsValid, string? TenantId, string? FailureReason) ValidateAndExtractTenantId(Message message)
    {
        // Check if pervaxis_tenant_id attribute exists
        if (message.MessageAttributes is null || !message.MessageAttributes.TryGetValue(MessagingConstants.TenantIdAttribute, out var tenantIdAttr))
        {
            _logger.LogWarning(
                "Message {MessageId} is missing required attribute {AttributeName}",
                message.MessageId, MessagingConstants.TenantIdAttribute);
            return (false, null, MessagingConstants.FailureReasons.MissingTenantId);
        }

        if (tenantIdAttr is null || tenantIdAttr.DataType != "String"
            || message.MessageAttributes.Keys.Any(k =>
                k.Equals(MessagingConstants.TenantIdAttribute, StringComparison.OrdinalIgnoreCase)
                && !k.Equals(MessagingConstants.TenantIdAttribute, StringComparison.Ordinal)))
        {
            return (false, null, MessagingConstants.FailureReasons.InvalidTenantId);
        }
        var tenantIdValue = tenantIdAttr.StringValue;

        // Reject platform messages in tenant-scoped consumers
        if (string.Equals(tenantIdValue, MessagingConstants.PlatformTenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Message {MessageId} has platform tenant ID in tenant consumer",
                message.MessageId);
            return (false, null, MessagingConstants.FailureReasons.PlatformNotAllowedInTenantConsumer);
        }

        // Validate tenant ID format using shared validator
        var validationResult = TenantIdValidator.Validate(tenantIdValue);
        if (!validationResult.IsValid || tenantIdValue.Any(char.IsControl))
        {
            _logger.LogWarning(
                "Message {MessageId} has invalid tenant ID '{TenantId}': {ValidationError}",
                message.MessageId, tenantIdValue, validationResult.ErrorMessage);
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
        if (StringComparer.Ordinal.Equals(_options.Sqs.DlqUrl, sourceQueueUrl))
        {
            throw new GenesisConfigurationException(nameof(SqsMessagingProvider),
                "The validation DLQ must differ from the source queue.");
        }
        if (string.IsNullOrWhiteSpace(_options.Sqs.DlqUrl))
        {
            _logger.LogError(
                "No DLQ configured. Invalid message {MessageId} remains unacknowledged. Reason: {FailureReason}",
                originalMessage.MessageId, failureReason);
            throw new GenesisException(
                nameof(SqsMessagingProvider),
                "DLQ not configured but required for message validation failures. Message cannot be safely discarded.",
                "DLQ_NOT_CONFIGURED",
                new Dictionary<string, object>
                {
                    ["MessageId"] = originalMessage.MessageId,
                    ["FailureReason"] = failureReason
                });
        }

        try
        {
            var dlqRequest = new SendMessageRequest
            {
                QueueUrl = _options.Sqs.DlqUrl,
                MessageBody = originalMessage.Body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>(originalMessage.MessageAttributes ?? new Dictionary<string, MessageAttributeValue>())
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
                "CRITICAL: Failed to send message {MessageId} to DLQ. Message will NOT be deleted from source queue to prevent data loss. Reason: {FailureReason}",
                originalMessage.MessageId, failureReason);
            throw; // Propagate to prevent message deletion
        }
    }

    /// <summary>
    /// Phase 1B: Deletes a message from the queue after processing or DLQ routing.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup: a failed delete is logged and the message becomes visible again for redelivery.")]
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to acknowledge a message from queue {QueueUrl}; leaving it for redelivery",
                queueUrl);
            // Don't throw - this is a best-effort cleanup
        }
    }
}
