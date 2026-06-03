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

namespace Pervaxis.Genesis.Idempotency.Options;

/// <summary>
/// Configuration for the idempotency middleware route/method targeting.
/// </summary>
public sealed class IdempotencyMiddlewareOptions
{
    /// <summary>
    /// Gets the route patterns to enable idempotency on (e.g., "/api/orders/{id}").
    /// </summary>
    public IList<string> RoutePatterns { get; } = new List<string>();

    /// <summary>
    /// Gets the HTTP methods to apply idempotency to.
    /// Default: POST, PATCH.
    /// </summary>
    public ISet<string> HttpMethods { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "POST",
        "PATCH"
    };
}
