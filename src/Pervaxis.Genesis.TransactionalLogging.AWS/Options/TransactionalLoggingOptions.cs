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

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Options;

/// <summary>
/// Configuration options for the Genesis Transactional Logging module.
/// Controls capture behavior, storage targets, retention, and sanitization.
/// Bound from the "Genesis:TransactionalLogging" configuration section.
/// </summary>
public sealed class TransactionalLoggingOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets whether the module is active. When false, all operations no-op.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether provider interceptors automatically record operations.
    /// Default: true.
    /// </summary>
    public bool ImplicitCapture { get; set; } = true;

    /// <summary>
    /// Gets or sets which Genesis providers to capture implicitly.
    /// Empty list means all providers. Case-insensitive matching.
    /// </summary>
    public List<string> CaptureProviders { get; set; } = new();

    /// <summary>
    /// Gets or sets which Genesis providers to exclude from implicit capture.
    /// Case-insensitive matching. Takes precedence over CaptureProviders.
    /// </summary>
    public List<string> ExcludeProviders { get; set; } = new();

    /// <summary>
    /// Gets or sets which operations to exclude from implicit capture.
    /// Format: "provider.operation" (e.g., "cache.get"). Case-insensitive.
    /// </summary>
    public List<string> ExcludeOperations { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum operation duration (ms) before an entry is captured.
    /// 0 means capture all operations. Default: 0.
    /// </summary>
    public int MinimumDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the DynamoDB table name for the hot store.
    /// Default: "genesis-transaction-logs".
    /// </summary>
    public string TableName { get; set; } = "genesis-transaction-logs";

    /// <summary>
    /// Gets or sets the S3 bucket name for cold store archival.
    /// Default: "genesis-transaction-logs-archive".
    /// </summary>
    public string BucketName { get; set; } = "genesis-transaction-logs-archive";

    /// <summary>
    /// Gets or sets how many days records stay in DynamoDB before archival.
    /// Valid range: 1–365. Default: 30.
    /// </summary>
    public int HotRetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets how many days records stay in S3.
    /// Valid range: 30–3650. Must be >= HotRetentionDays. Default: 2555 (7 years).
    /// </summary>
    public int ColdRetentionDays { get; set; } = 2555;

    /// <summary>
    /// Gets or sets whether to enable tenant isolation in storage keys.
    /// Default: true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to sanitize sensitive parameters before storage.
    /// Default: true.
    /// </summary>
    public bool SanitizeParameters { get; set; } = true;

    /// <summary>
    /// Gets or sets custom sensitive key patterns for sanitization.
    /// Added to built-in patterns (password, secret, token, key, credential, auth, connectionstring).
    /// </summary>
    public List<string> SensitiveKeys { get; set; } = new();

    /// <summary>
    /// Gets or sets route patterns to suppress from transaction logging.
    /// Example: "/health", "/metrics".
    /// </summary>
    public List<string> SuppressRoutes { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to enable S3 Object Lock for compliance.
    /// Default: false.
    /// </summary>
    public bool EnableObjectLock { get; set; }

    /// <summary>
    /// Gets or sets the resilience policy configuration for store operations.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <inheritdoc/>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (Enabled && !UseLocalEmulator && string.IsNullOrWhiteSpace(TableName))
        {
            return false;
        }

        if (HotRetentionDays is < 1 or > 365)
        {
            return false;
        }

        if (ColdRetentionDays is < 30 or > 3650)
        {
            return false;
        }

        if (ColdRetentionDays < HotRetentionDays)
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
