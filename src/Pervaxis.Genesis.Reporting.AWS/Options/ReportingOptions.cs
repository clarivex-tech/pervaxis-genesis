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

namespace Pervaxis.Genesis.Reporting.AWS.Options;

/// <summary>
/// Configuration options for Metabase reporting provider.
/// </summary>
public sealed class ReportingOptions : GenesisOptionsBase
{
    /// <summary>
    /// Base URL of the Metabase instance (e.g., "https://metabase.example.com").
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authentication.
    /// Generate from Metabase Admin > Settings > Authentication > API Keys.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Database ID for query execution.
    /// Optional - can be specified per query.
    /// </summary>
    public int? DatabaseId { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// Default: 30 (queries can be slow).
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retries for transient failures.
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether to enable tenant isolation.
    /// When enabled, queries include tenant filters for data isolation.
    /// Default is true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Validates the reporting options.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return false;
        }

        if (RequestTimeoutSeconds <= 0)
        {
            return false;
        }

        if (MaxRetries < 0)
        {
            return false;
        }

        return true;
    }
}
