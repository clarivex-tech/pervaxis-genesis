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
/// AWS SNS-specific configuration settings.
/// </summary>
public sealed class SnsOptions
{
    /// <summary>
    /// Gets or sets the default topic ARN used when no destination-specific mapping is found.
    /// Example: arn:aws:sns:ap-south-1:123456789012:my-topic
    /// </summary>
    public string DefaultTopicArn { get; set; } = string.Empty;

    /// <summary>
    /// Gets per-destination topic ARN overrides.
    /// Key: logical destination name. Value: full SNS topic ARN.
    /// </summary>
    public Dictionary<string, string> TopicArnMappings { get; } = [];
}
