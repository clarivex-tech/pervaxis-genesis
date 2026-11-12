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

using Microsoft.AspNetCore.Http;

namespace Pervaxis.Genesis.Idempotency.Services;

/// <summary>
/// Computes a deterministic fingerprint from HTTP method, route template, and body hash.
/// Uses SHA-256 for body hashing.
/// </summary>
public interface IRequestFingerprintComputer
{
    /// <summary>
    /// Computes fingerprint as: "{METHOD}|{routeTemplate}|{SHA256(body)}".
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A deterministic fingerprint string for the request.</returns>
    Task<string> ComputeAsync(HttpContext context, CancellationToken cancellationToken = default);
}
