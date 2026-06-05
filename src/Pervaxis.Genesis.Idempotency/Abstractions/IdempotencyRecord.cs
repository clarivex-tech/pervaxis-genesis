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

namespace Pervaxis.Genesis.Idempotency.Abstractions;

/// <summary>
/// Represents a stored idempotency record containing the cached response.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>The idempotency key value.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>The tenant-scoped composite storage key (tenantId#key).</summary>
    public required string CompositeKey { get; init; }

    /// <summary>Request fingerprint (method|route|bodyHash).</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Whether the record is completed (response stored) or in-flight.</summary>
    public required bool IsCompleted { get; init; }

    /// <summary>HTTP status code of the cached response. Null if in-flight.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Serialized response headers (JSON). Null if in-flight.</summary>
    public string? ResponseHeaders { get; init; }

    /// <summary>Response body bytes (Base64-encoded for DynamoDB). Null if in-flight.</summary>
    public string? ResponseBody { get; init; }

    /// <summary>UTC timestamp when the record was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Unix epoch seconds when the record expires (DynamoDB TTL attribute).</summary>
    public required long ExpiresAtEpoch { get; init; }
}
