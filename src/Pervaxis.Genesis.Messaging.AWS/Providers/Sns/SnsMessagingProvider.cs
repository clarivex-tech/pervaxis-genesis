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

using System.Globalization;
using System.Text.Json;
using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
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

        _logger.LogInformation(
            "SnsMessagingProvider initialized for region {Region}, tenant isolation: {TenantIsolation}",
            _options.Region,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true);
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
    }

    /// <inheritdoc/>
    public async Task<string> PublishAsync<T>(
        string destination,
        T message,
        CancellationToken cancellationToken = default)
    {
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

            var response = await _snsClient.Value
                .PublishAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Published message {MessageId} to SNS topic {TopicArn}",
                response.MessageId, topicArn);

            return response.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to SNS topic {TopicArn}", topicArn);
            throw new GenesisException(nameof(SnsMessagingProvider), "SNS publish operation failed", ex);
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

                var response = await _snsClient.Value
                    .PublishBatchAsync(request, cancellationToken)
                    .ConfigureAwait(false);

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

            return allMessageIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish batch to SNS topic {TopicArn}", topicArn);
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

            var response = await _snsClient.Value
                .SubscribeAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Subscribed endpoint {Endpoint} to SNS topic {TopicArn} (SubscriptionArn={SubscriptionArn})",
                endpoint, topicArn, response.SubscriptionArn);

            return response.SubscriptionArn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe {Endpoint} to SNS topic {TopicArn}", endpoint, topicArn);
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
}
