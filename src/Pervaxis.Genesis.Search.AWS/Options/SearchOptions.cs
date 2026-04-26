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

namespace Pervaxis.Genesis.Search.AWS.Options;

/// <summary>
/// Configuration options for AWS OpenSearch provider.
/// </summary>
public sealed class SearchOptions : GenesisOptionsBase
{
    /// <summary>
    /// OpenSearch domain endpoint URL (e.g., "https://my-domain.us-east-1.es.amazonaws.com").
    /// </summary>
    public string DomainEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Optional index prefix for all indices (e.g., "prod-").
    /// Note: For tenant isolation, use <see cref="EnableTenantIsolation"/> instead.
    /// </summary>
    public string IndexPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to enable tenant isolation.
    /// When enabled, index names are prefixed with tenant ID and documents include tenant field.
    /// Default is true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Default number of search results to return (default: 10).
    /// </summary>
    public int DefaultPageSize { get; set; } = 10;

    /// <summary>
    /// Request timeout in seconds (default: 30).
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retries for failed requests (default: 3).
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Enable debug mode for detailed logging (default: false).
    /// </summary>
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// Username for basic authentication (optional, for non-AWS OpenSearch).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for basic authentication (optional, for non-AWS OpenSearch).
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the resilience policy configuration.
    /// Configures retry, circuit breaker, and timeout strategies for handling transient failures.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Validates the search configuration.
    /// </summary>
    public override bool Validate()
    {
        var baseValid = base.Validate();

        ArgumentException.ThrowIfNullOrWhiteSpace(Region, nameof(Region));
        ArgumentException.ThrowIfNullOrWhiteSpace(DomainEndpoint, nameof(DomainEndpoint));

        if (!Uri.TryCreate(DomainEndpoint, UriKind.Absolute, out _))
        {
            throw new GenesisConfigurationException(
                "SearchOptions",
                $"{nameof(DomainEndpoint)} must be a valid absolute URL.");
        }

        if (DefaultPageSize <= 0)
        {
            throw new GenesisConfigurationException(
                "SearchOptions",
                $"{nameof(DefaultPageSize)} must be greater than 0.");
        }

        if (RequestTimeoutSeconds <= 0)
        {
            throw new GenesisConfigurationException(
                "SearchOptions",
                $"{nameof(RequestTimeoutSeconds)} must be greater than 0.");
        }

        if (MaxRetries < 0)
        {
            throw new GenesisConfigurationException(
                "SearchOptions",
                $"{nameof(MaxRetries)} must be greater than or equal to 0.");
        }

        if (!Resilience.Validate())
        {
            return false;
        }

        return baseValid;
    }
}
