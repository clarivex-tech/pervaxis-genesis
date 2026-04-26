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

using Pervaxis.Core.Abstractions.Genesis;
using Pervaxis.Genesis.Base.Options;

namespace Pervaxis.Genesis.Notifications.AWS.Options;

/// <summary>
/// Configuration options for AWS SES and SNS notification services.
/// </summary>
public sealed class NotificationOptions : GenesisOptionsBase
{
    /// <summary>
    /// The verified sender email address for SES.
    /// Required for email operations.
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// Optional display name for the sender email.
    /// </summary>
    public string? FromName { get; set; }

    /// <summary>
    /// Optional SES configuration set name for tracking email metrics.
    /// </summary>
    public string? ConfigurationSetName { get; set; }

    /// <summary>
    /// SNS topic ARN for SMS notifications.
    /// Required if sending SMS messages.
    /// </summary>
    public string? SmsTopicArn { get; set; }

    /// <summary>
    /// SNS platform application ARN for push notifications.
    /// Required if sending push notifications.
    /// </summary>
    public string? PushPlatformApplicationArn { get; set; }

    /// <summary>
    /// Maximum number of retries for failed operations.
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Request timeout in seconds.
    /// Default: 30.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets a value indicating whether to enable tenant isolation.
    /// When enabled, notifications include tenant metadata for tracking.
    /// Default is true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Gets or sets the resilience policy configuration.
    /// Configures retry, circuit breaker, and timeout strategies for handling transient failures.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Validates the notification options.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FromEmail))
        {
            return false;
        }

        if (!FromEmail.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        if (MaxRetries < 0)
        {
            return false;
        }

        if (RequestTimeoutSeconds <= 0)
        {
            return false;
        }

        if (!Resilience.Validate())
        {
            return false;
        }

        return true;
    }
}
