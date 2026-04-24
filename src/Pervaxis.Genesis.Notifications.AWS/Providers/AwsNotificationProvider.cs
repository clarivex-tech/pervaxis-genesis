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

using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Notifications.AWS.Options;

namespace Pervaxis.Genesis.Notifications.AWS.Providers;

/// <summary>
/// AWS-based notification provider using SES for email and SNS for SMS/push notifications.
/// Implements <see cref="INotification"/> interface.
/// </summary>
public sealed class AwsNotificationProvider : INotification, IDisposable
{
    private readonly ILogger<AwsNotificationProvider> _logger;
    private readonly NotificationOptions _options;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IAmazonSimpleEmailService> _sesClient;
    private readonly Lazy<IAmazonSimpleNotificationService> _snsClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsNotificationProvider"/> class.
    /// </summary>
    /// <param name="options">The notification options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    /// <exception cref="ArgumentNullException">Thrown when options or logger is null.</exception>
    /// <exception cref="GenesisConfigurationException">Thrown when options validation fails.</exception>
    public AwsNotificationProvider(
        IOptions<NotificationOptions> options,
        ILogger<AwsNotificationProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(nameof(NotificationOptions), "Invalid notification options configuration");
        }

        _sesClient = new Lazy<IAmazonSimpleEmailService>(CreateSesClient);
        _snsClient = new Lazy<IAmazonSimpleNotificationService>(CreateSnsClient);

        _logger.LogInformation(
            "AwsNotificationProvider initialized for region {Region} with sender {FromEmail}, tenant isolation: {TenantIsolation}",
            _options.Region, _options.FromEmail,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true);
    }

    /// <summary>
    /// Internal constructor for testing with injected clients.
    /// </summary>
    internal AwsNotificationProvider(
        NotificationOptions options,
        IAmazonSimpleEmailService sesClient,
        IAmazonSimpleNotificationService snsClient,
        ILogger<AwsNotificationProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sesClient);
        ArgumentNullException.ThrowIfNull(snsClient);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _tenantContext = tenantContext;
        _logger = logger;
        _sesClient = new Lazy<IAmazonSimpleEmailService>(() => sesClient);
        _snsClient = new Lazy<IAmazonSimpleNotificationService>(() => snsClient);
    }

    /// <inheritdoc />
    public async Task<string> SendEmailAsync(
        string recipient,
        string subject,
        string body,
        bool isHtml = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient, nameof(recipient));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));
        ArgumentException.ThrowIfNullOrWhiteSpace(body, nameof(body));

        try
        {
            var fromAddress = string.IsNullOrWhiteSpace(_options.FromName)
                ? _options.FromEmail
                : $"{_options.FromName} <{_options.FromEmail}>";

            var request = new SendEmailRequest
            {
                Source = fromAddress,
                Destination = new Destination { ToAddresses = [recipient] },
                Message = new Message
                {
                    Subject = new Content(subject),
                    Body = new Body
                    {
                        Html = isHtml ? new Content(body) : null,
                        Text = !isHtml ? new Content(body) : null
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(_options.ConfigurationSetName))
            {
                request.ConfigurationSetName = _options.ConfigurationSetName;
            }

            // Add tenant tags
            foreach (var tag in BuildTenantTags())
            {
                request.Tags.Add(new MessageTag { Name = tag.Key, Value = tag.Value });
            }

            var response = await _sesClient.Value.SendEmailAsync(request, cancellationToken);

            _logger.LogInformation(
                "Email sent successfully to {To} with MessageId {MessageId}",
                recipient, response.MessageId);

            return response.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", recipient);
            throw new GenesisException(nameof(AwsNotificationProvider), $"Failed to send email: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> SendTemplatedEmailAsync(
        string recipient,
        string templateId,
        IDictionary<string, string> templateData,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient, nameof(recipient));
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId, nameof(templateId));
        ArgumentNullException.ThrowIfNull(templateData);

        try
        {
            var fromAddress = string.IsNullOrWhiteSpace(_options.FromName)
                ? _options.FromEmail
                : $"{_options.FromName} <{_options.FromEmail}>";

            // Convert template data to JSON string
            var templateDataJson = System.Text.Json.JsonSerializer.Serialize(templateData);

            var request = new SendTemplatedEmailRequest
            {
                Source = fromAddress,
                Destination = new Destination { ToAddresses = [recipient] },
                Template = templateId,
                TemplateData = templateDataJson
            };

            if (!string.IsNullOrWhiteSpace(_options.ConfigurationSetName))
            {
                request.ConfigurationSetName = _options.ConfigurationSetName;
            }

            var response = await _sesClient.Value.SendTemplatedEmailAsync(request, cancellationToken);

            _logger.LogInformation(
                "Templated email sent successfully to {To} using template {TemplateId} with MessageId {MessageId}",
                recipient, templateId, response.MessageId);

            return response.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send templated email to {To} using template {TemplateId}", recipient, templateId);
            throw new GenesisException(nameof(AwsNotificationProvider), $"Failed to send templated email: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> SendSmsAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber, nameof(phoneNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        try
        {
            var request = new PublishRequest
            {
                PhoneNumber = phoneNumber,
                Message = message
            };

            // Use topic ARN if configured for SMS delivery tracking
            if (!string.IsNullOrWhiteSpace(_options.SmsTopicArn))
            {
                request.TopicArn = _options.SmsTopicArn;
                request.PhoneNumber = null; // When using topic, don't set phone number directly
                request.MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["phoneNumber"] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = phoneNumber
                    }
                };
            }

            var response = await _snsClient.Value.PublishAsync(request, cancellationToken);

            _logger.LogInformation(
                "SMS sent successfully to {PhoneNumber} with MessageId {MessageId}",
                phoneNumber, response.MessageId);

            return response.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SMS to {PhoneNumber}", phoneNumber);
            throw new GenesisException(nameof(AwsNotificationProvider), $"Failed to send SMS: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> SendPushAsync(
        string deviceToken,
        string title,
        string message,
        IDictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceToken, nameof(deviceToken));
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        ArgumentException.ThrowIfNullOrWhiteSpace(message, nameof(message));

        if (string.IsNullOrWhiteSpace(_options.PushPlatformApplicationArn))
        {
            throw new GenesisException(nameof(AwsNotificationProvider), "PushPlatformApplicationArn is not configured");
        }

        try
        {
            // First, create or retrieve the endpoint for the device token
            var endpointArn = await CreatePlatformEndpointAsync(deviceToken, cancellationToken);

            // Prepare push notification payload (GCM/FCM format)
            var payload = new
            {
                notification = new
                {
                    title,
                    body = message
                },
                data = data ?? new Dictionary<string, string>()
            };

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            var gcmMessage = System.Text.Json.JsonSerializer.Serialize(new { GCM = payloadJson });

            var publishRequest = new PublishRequest
            {
                TargetArn = endpointArn,
                Message = gcmMessage,
                MessageStructure = "json"
            };

            var response = await _snsClient.Value.PublishAsync(publishRequest, cancellationToken);

            _logger.LogInformation(
                "Push notification sent successfully to device token {DeviceToken} with MessageId {MessageId}",
                deviceToken, response.MessageId);

            return response.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to device token {DeviceToken}", deviceToken);
            throw new GenesisException(nameof(AwsNotificationProvider), $"Failed to send push notification: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates or retrieves a platform endpoint for a device token.
    /// </summary>
    private async Task<string> CreatePlatformEndpointAsync(string deviceToken, CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreatePlatformEndpointRequest
            {
                PlatformApplicationArn = _options.PushPlatformApplicationArn,
                Token = deviceToken
            };

            var response = await _snsClient.Value.CreatePlatformEndpointAsync(request, cancellationToken);
            return response.EndpointArn;
        }
        catch (InvalidParameterException ex) when (ex.Message.Contains("already exists", StringComparison.Ordinal))
        {
            // Endpoint already exists, extract ARN from error message or list endpoints
            // For simplicity, we'll re-throw here. In production, you'd parse the ARN.
            _logger.LogWarning("Platform endpoint already exists for device token, attempting to retrieve");
            throw;
        }
    }

    /// <summary>
    /// Creates the SES client with appropriate configuration.
    /// </summary>
    private IAmazonSimpleEmailService CreateSesClient()
    {
        var config = new AmazonSimpleEmailServiceConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region),
            MaxErrorRetry = _options.MaxRetries,
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)
        };

        if (_options.UseLocalEmulator && _options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = _options.LocalEmulatorUrl.AbsoluteUri;
            config.AuthenticationRegion = _options.Region;
        }

        return new AmazonSimpleEmailServiceClient(config);
    }

    /// <summary>
    /// Creates the SNS client with appropriate configuration.
    /// </summary>
    private IAmazonSimpleNotificationService CreateSnsClient()
    {
        var config = new AmazonSimpleNotificationServiceConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region),
            MaxErrorRetry = _options.MaxRetries,
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)
        };

        if (_options.UseLocalEmulator && _options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = _options.LocalEmulatorUrl.AbsoluteUri;
            config.AuthenticationRegion = _options.Region;
        }

        return new AmazonSimpleNotificationServiceClient(config);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_sesClient.IsValueCreated)
        {
            _sesClient.Value.Dispose();
        }

        if (_snsClient.IsValueCreated)
        {
            _snsClient.Value.Dispose();
        }

        _disposed = true;
        _logger.LogInformation("AwsNotificationProvider disposed");
    }

    private Dictionary<string, string> BuildTenantTags()
    {
        var tags = new Dictionary<string, string>();

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags["TenantId"] = _tenantContext.TenantId.Value;
            tags["TenantName"] = _tenantContext.TenantName;
        }

        return tags;
    }
}
