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
using Pervaxis.Genesis.Messaging.AWS.Options;

namespace Pervaxis.Genesis.Messaging.AWS.Providers.Sns;

/// <summary>
/// SNS implementation of the <see cref="IMessaging"/> abstraction.
/// Supports single and batch publish to topics, and topic subscription management.
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
    private readonly Lazy<IAmazonSimpleNotificationService> _snsClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResiliencePipeline _resiliencePipeline;

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

    /// <summary>
    /// Initializes a new instance of <see cref="SnsMessagingProvider"/> for production use.
    /// </summary>
    /// <param name="options">Messaging configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public SnsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SnsMessagingProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        ValidateOptions();

        _snsClient = new Lazy<IAmazonSimpleNotificationService>(CreateClient);
        _jsonOptions = BuildJsonOptions();
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "SnsMessaging");

        _logger.LogInformation(
            "SnsMessagingProvider initialized for region {Region}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.Region,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
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
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(snsClient);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

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
        var body = Serialize(message);

        try
        {
            var request = new PublishRequest
            {
                TopicArn = topicArn,
                Message = body
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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to publish message to SNS topic {TopicArn}", topicArn);

            var tags = GetMetricTags("publish", "error", "sns");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(SnsMessagingProvider), "SNS publish operation failed", ex);
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
                            Message = Serialize(msg)
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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to publish batch to SNS topic {TopicArn}", topicArn);

            var tags = GetMetricTags("publish_batch", "error", "sns");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(SnsMessagingProvider), "SNS batch publish operation failed", ex);
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
    public async Task<string> SubscribeAsync(
        string topic,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("messaging.subscribe", ActivityKind.Client);
        activity?.SetTag("messaging.system", "sns");
        activity?.SetTag("messaging.destination", topic);
        activity?.SetTag("messaging.operation", "subscribe");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var topicArn = ResolveTopicArn(topic);

        try
        {
            // Infer protocol from endpoint format
            var protocol = InferProtocol(endpoint);

            var request = new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = protocol,
                Endpoint = endpoint
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _snsClient.Value.SubscribeAsync(request, ct).ConfigureAwait(false),
                cancellationToken);

            activity?.SetTag("messaging.subscription_arn", response.SubscriptionArn);
            _logger.LogInformation(
                "Subscribed endpoint {Endpoint} to SNS topic {TopicArn} (SubscriptionArn={SubscriptionArn})",
                endpoint, topicArn, response.SubscriptionArn);

            var tags = GetMetricTags("subscribe", "success", "sns");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return response.SubscriptionArn;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to subscribe {Endpoint} to SNS topic {TopicArn}", endpoint, topicArn);

            var tags = GetMetricTags("subscribe", "error", "sns");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(SnsMessagingProvider), "SNS subscribe operation failed", ex);
        }
    }

    /// <summary>
    /// Disposes the SNS client if it was created.
    /// </summary>
    public void Dispose()
    {
        if (_snsClient.IsValueCreated)
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

    private IAmazonSimpleNotificationService CreateClient()
    {
        var config = new AmazonSimpleNotificationServiceConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region)
        };

        if (_options.UseLocalEmulator && _options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = _options.LocalEmulatorUrl.AbsoluteUri;
            _logger.LogInformation("SnsMessagingProvider using local emulator: {Url}", _options.LocalEmulatorUrl);
        }

        return new AmazonSimpleNotificationServiceClient(config);
    }

    private string ResolveTopicArn(string destination)
    {
        if (_options.Sns.TopicArnMappings.TryGetValue(destination, out var mappedArn))
        {
            return mappedArn;
        }

        return string.IsNullOrWhiteSpace(_options.Sns.DefaultTopicArn)
            ? destination
            : _options.Sns.DefaultTopicArn;
    }

    private static string InferProtocol(string endpoint)
    {
        if (endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https";
        }

        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "http";
        }

        if (endpoint.StartsWith("arn:aws:sqs:", StringComparison.OrdinalIgnoreCase))
        {
            return "sqs";
        }

        if (endpoint.Contains('@', StringComparison.Ordinal))
        {
            return "email";
        }

        return "sqs";
    }

    private string Serialize<T>(T value) => JsonSerializer.Serialize(value, _jsonOptions);

    private static JsonSerializerOptions BuildJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private void AddTenantAttributes(Dictionary<string, MessageAttributeValue> attributes)
    {
        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            attributes["TenantId"] = new MessageAttributeValue
            {
                DataType = "String",
                StringValue = _tenantContext.TenantId.Value
            };
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
}
