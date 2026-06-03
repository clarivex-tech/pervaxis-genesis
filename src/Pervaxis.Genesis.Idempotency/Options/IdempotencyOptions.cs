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

namespace Pervaxis.Genesis.Idempotency.Options;

/// <summary>
/// Configuration for the Genesis Idempotency module.
/// Bound from the "Genesis:Idempotency" configuration section.
/// </summary>
public sealed class IdempotencyOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the DynamoDB table name for storing idempotency records.
    /// Max 255 characters. Default: "genesis-idempotency".
    /// </summary>
    public string TableName { get; set; } = "genesis-idempotency";

    /// <summary>
    /// Gets or sets the record time-to-live in minutes.
    /// Valid range: 1–10080 (7 days). Default: 1440 (24 hours).
    /// </summary>
    public int TtlMinutes { get; set; } = 1440;

    /// <summary>
    /// Gets or sets the HTTP header name for the idempotency key.
    /// Max 128 characters. Default: "Idempotency-Key".
    /// </summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>
    /// Gets or sets whether to enable per-tenant record isolation.
    /// When true, records are scoped by tenant ID. Default: true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate that reused keys correspond to the same request.
    /// When true, fingerprint mismatches return HTTP 422. Default: true.
    /// </summary>
    public bool ValidateRequestFingerprint { get; set; } = true;

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

        if (!UseLocalEmulator && string.IsNullOrWhiteSpace(TableName))
        {
            return false;
        }

        if (TableName?.Length > 255)
        {
            return false;
        }

        if (TtlMinutes is < 1 or > 10080)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(HeaderName))
        {
            return false;
        }

        if (HeaderName.Length > 128)
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
