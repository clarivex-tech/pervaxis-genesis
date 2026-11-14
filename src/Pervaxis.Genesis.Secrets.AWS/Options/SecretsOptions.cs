/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Core.Abstractions.Genesis;

namespace Pervaxis.Genesis.Secrets.AWS.Options;

/// <summary>Settings for shared AWS Secrets Manager reads.</summary>
public sealed class SecretsOptions : GenesisOptionsBase
{
    /// <summary>Legacy setting. False is rejected; tenant isolation is mandatory.</summary>
    public bool EnableTenantIsolation { get; set; } = true;
    /// <summary>Whether successful single-value reads are cached in process.</summary>
    public bool EnableCaching { get; set; } = true;
    /// <summary>Cache lifetime in seconds. Defaults to five minutes.</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <inheritdoc />
    public override bool Validate() => base.Validate() && EnableTenantIsolation && CacheTtlSeconds > 0;
}
