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
using System.Globalization;
using System.Text.Json;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
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

    /// <summary>
    /// Initializes a new instance of <see cref="SqsMessagingProvider"/> for production use.
    /// </summary>
    /// <param name="options">Messaging configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public SqsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SqsMessagingProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        ValidateOptions();

        _sqsClient = new Lazy<IAmazonSQS>(CreateClient);
        _jsonOptions = BuildJsonOptions();

        _logger.LogInformation(
            "SqsMessagingProvider initialized for region {Region}, tenant isolation: {TenantIsolation}",
            _options.Region,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SqsMessagingProvider"/> with an explicit
    /// SQS client — intended for unit testing only.
    /// </summary>
    internal SqsMessagingProvider(
        IOptions<MessagingOptions> options,
        ILogger<SqsMessagingProvider> logger,
        IAmazonSQS sqsClient,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(sqsClient);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        ValidateOptions();

        _sqsClient = new Lazy<IAmazonSQS>(() => sqsClient);
        _jsonOptions = BuildJsonOptions();
    }

    /// <inheritdoc/>
    public async Task<string> PublishAsync<T>(
        string destination,
        T message,
        CancellationToken cancellationToken = default)
    {
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
                MessageBody = body
            };

            AddTenantAttributes(request.MessageAttributes);

            var response = await _sqsClient.Value
                .SendMessageAsync(request, cancellationToken)
                .ConfigureAwait(false);

            activity?.SetTag("messaging.message_id", response.MessageId);
            _logger.LogDebug(
                "Published message {MessageId} to SQS queue {QueueUrl}",
                response.MessageId, queueUrl);

            return response.MessageId;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to publish message to SQS queue {QueueUrl}", queueUrl);
            throw new GenesisException(nameof(SqsMessagingProvider), "SQS publish operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<string>> PublishBatchAsync<T>(
        string destination,
        IEnumerable<T> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(messages);

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return [];
        }

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
                            MessageBody = Serialize(msg)
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

                var response = await _sqsClient.Value
                    .SendMessageBatchAsync(request, cancellationToken)
                    .ConfigureAwait(false);

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

            return allMessageIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch to SQS queue {QueueUrl}", queueUrl);
            throw new GenesisException(nameof(SqsMessagingProvider), "SQS batch publish operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<T>> ReceiveAsync<T>(
        string queue,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
    {
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
                VisibilityTimeout = _options.Sqs.VisibilityTimeoutSeconds
            };

            var response = await _sqsClient.Value
                .ReceiveMessageAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Received {Count} messages from SQS queue {QueueUrl}",
                response.Messages.Count, queueUrl);

            return response.Messages
                .Select(m => Deserialize<T>(m.Body))
                .Where(m => m is not null)
                .Select(m => m!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive messages from SQS queue {QueueUrl}", queueUrl);
            throw new GenesisException(nameof(SqsMessagingProvider), "SQS receive operation failed", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        string queue,
        string receiptHandle,
        CancellationToken cancellationToken = default)
    {
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

            await _sqsClient.Value
                .DeleteMessageAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Deleted message from SQS queue {QueueUrl}", queueUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete message from SQS queue {QueueUrl}", queueUrl);
            throw new GenesisException(nameof(SqsMessagingProvider), "SQS delete operation failed", ex);
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

    private void ValidateOptions()
    {
        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(SqsMessagingProvider),
                "Invalid messaging configuration");
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
        if (_options.Sqs.QueueUrlMappings.TryGetValue(destination, out var mappedUrl))
        {
            return mappedUrl;
        }

        return string.IsNullOrWhiteSpace(_options.Sqs.DefaultQueueUrl)
            ? destination
            : _options.Sqs.DefaultQueueUrl;
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
}
