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
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Options;

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

    /// <summary>
    /// Legacy setting. Tenant isolation is mandatory; false is rejected in every environment.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Gets or sets the time-to-live (TTL) for cached queue URLs and topic ARNs.
    /// Prevents unbounded cache growth and handles queue/topic deletions.
    /// Default is 1 hour. Set to TimeSpan.Zero to disable caching.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of entries in the URL/ARN cache.
    /// When exceeded, least recently used entries are evicted.
    /// Default is 10,000 entries. Set to 0 for unlimited (not recommended).
    /// </summary>
    public int MaxCacheSize { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the resilience policy configuration.
    /// Configures retry, circuit breaker, and timeout strategies for handling transient failures.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether per-tenant AWS resources are used (separate queues/topics per tenant).
    /// Phase 1: NOT YET IMPLEMENTED. Setting this to true will throw a configuration exception.
    /// Phase 2+: When implemented, this will switch from shared queues to dedicated per-tenant resources.
    /// Default is false (shared queues with message-attribute based isolation).
    /// </summary>
    public bool EnablePerTenantResources { get; set; }

    /// <inheritdoc/>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (!Resilience.Validate())
        {
            return false;
        }

        if (!EnableTenantIsolation)
        {
            throw new GenesisConfigurationException(nameof(MessagingOptions),
                "Tenant isolation cannot be disabled for shared messaging resources.");
        }
        // Phase 1: Per-tenant resources not yet implemented
        if (EnablePerTenantResources)
        {
            throw new GenesisConfigurationException(
                nameof(MessagingOptions),
                "Per-tenant AWS resources (EnablePerTenantResources=true) are not yet implemented. " +
                "Phase 1 only supports shared queues with message-attribute based tenant isolation.");
        }

        return true;
    }
}
