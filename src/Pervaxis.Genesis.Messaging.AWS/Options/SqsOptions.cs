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

namespace Pervaxis.Genesis.Messaging.AWS.Options;

/// <summary>
/// AWS SQS-specific configuration settings.
/// </summary>
public sealed class SqsOptions
{
    /// <summary>
    /// Gets or sets the default queue URL used when no destination-specific mapping is found.
    /// Example: https://sqs.ap-south-1.amazonaws.com/123456789012/my-queue
    /// </summary>
    public string DefaultQueueUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets per-destination queue URL overrides.
    /// Key: logical destination name. Value: full SQS queue URL.
    /// </summary>
    public Dictionary<string, string> QueueUrlMappings { get; } = [];

    /// <summary>
    /// Gets or sets the maximum number of messages to retrieve in a single receive call.
    /// Valid range: 1–10 (SQS limit). Default is 10.
    /// </summary>
    public int MaxNumberOfMessages { get; set; } = 10;

    /// <summary>
    /// Gets or sets the long-polling wait time in seconds.
    /// 0 = short polling; 1–20 = long polling. Default is 20 (recommended).
    /// </summary>
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the visibility timeout in seconds for received messages.
    /// The message is hidden from other consumers for this duration. Default is 30.
    /// </summary>
    public int VisibilityTimeoutSeconds { get; set; } = 30;
}
