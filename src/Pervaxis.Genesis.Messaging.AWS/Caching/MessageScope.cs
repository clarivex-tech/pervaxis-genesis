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
/// Defines the scope of messaging operations for caching and resource resolution.
/// </summary>
public enum MessageScope
{
    /// <summary>
    /// Tenant-scoped messaging (default). Resources are resolved per-tenant.
    /// </summary>
    Tenant = 0,

    /// <summary>
    /// Platform-scoped messaging. Resources are shared across all tenants.
    /// Requires IPlatformMessagingProvider and specific authorization.
    /// </summary>
    Platform = 1
}
