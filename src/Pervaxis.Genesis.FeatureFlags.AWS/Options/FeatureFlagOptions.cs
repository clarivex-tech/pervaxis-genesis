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

namespace Pervaxis.Genesis.FeatureFlags.AWS.Options;

/// <summary>
/// Configuration options for the Genesis Feature Flags module (AWS AppConfig).
/// Follows the <c>{Domain}.{Feature}</c> naming convention for flag identifiers.
/// Example: <c>"Billing.NewInvoiceFlow"</c>
/// </summary>
public sealed class FeatureFlagOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the AWS AppConfig parameter path.
    /// Supports <c>{env}</c> placeholder replaced at runtime with the hosting environment name.
    /// Example: "/pervaxis/{env}/feature-flags"
    /// </summary>
    public string AppConfigPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the polling interval in seconds for AppConfig changes.
    /// Valid range: 10–300. Default: 30.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the resilience policy configuration for AppConfig polling.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to enable tenant isolation in metric tags.
    /// Default: true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <inheritdoc/>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (!UseLocalEmulator && string.IsNullOrWhiteSpace(AppConfigPath))
        {
            return false;
        }

        if (PollingIntervalSeconds is < 10 or > 300)
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
