/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Core.Abstractions.Genesis;

namespace Pervaxis.Genesis.Parameters.AWS.Options;

/// <summary>Settings for shared AWS Parameter Store reads.</summary>
public sealed class ParameterStoreOptions : GenesisOptionsBase
{
    /// <summary>Legacy setting. False is rejected; tenant isolation is mandatory.</summary>
    public bool EnableTenantIsolation { get; set; } = true;
    /// <summary>Whether successful single-value reads are cached in process.</summary>
    public bool EnableCaching { get; set; } = true;
    /// <summary>Cache lifetime in seconds. Defaults to five minutes.</summary>
    public int CacheTtlSeconds { get; set; } = 300;
    /// <summary>Decrypt SecureString values when reading. Defaults to true.</summary>
    public bool WithDecryption { get; set; } = true;
    /// <summary>Page size for path reads, between one and ten.</summary>
    public int MaxResults { get; set; } = 10;

    /// <inheritdoc />
    public override bool Validate() => base.Validate() && EnableTenantIsolation && CacheTtlSeconds > 0 && MaxResults is >= 1 and <= 10;
}
