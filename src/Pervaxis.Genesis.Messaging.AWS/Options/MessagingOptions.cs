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

namespace Pervaxis.Genesis.Messaging.AWS.Options;

/// <summary>
/// Configuration options for the Genesis Messaging provider (SQS + SNS).
/// </summary>
public sealed class MessagingOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the SQS-specific configuration.
    /// Required when using <see cref="Providers.Sqs.SqsMessagingProvider"/>.
    /// </summary>
    public SqsOptions Sqs { get; set; } = new();

    /// <summary>
    /// Gets or sets the SNS-specific configuration.
    /// Required when using <see cref="Providers.Sns.SnsMessagingProvider"/>.
    /// </summary>
    public SnsOptions Sns { get; set; } = new();

    /// <inheritdoc/>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        return true;
    }
}
