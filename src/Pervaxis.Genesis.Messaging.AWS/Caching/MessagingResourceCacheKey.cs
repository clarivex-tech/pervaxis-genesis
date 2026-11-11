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

namespace Pervaxis.Genesis.Messaging.AWS.Caching;

/// <summary>
/// Cache key for messaging resource resolution (queues, topics).
/// Combines destination name, tenant ID, and scope to ensure proper isolation.
/// </summary>
/// <param name="Destination">Queue or topic name (logical identifier).</param>
/// <param name="TenantId">Normalized tenant ID (lowercase) or empty string for platform scope.</param>
/// <param name="Scope">Message scope (Tenant or Platform).</param>
public sealed record MessagingResourceCacheKey(
    string Destination,
    string TenantId,
    MessageScope Scope);
