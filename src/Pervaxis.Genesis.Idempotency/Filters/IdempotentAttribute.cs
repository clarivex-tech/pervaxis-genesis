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

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Pervaxis.Genesis.Idempotency.Filters;

/// <summary>
/// Enables idempotency handling on a controller action.
/// When applied, requests with a valid Idempotency-Key header will have their
/// responses cached and replayed for duplicate requests.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    /// <summary>
    /// Gets or sets the per-endpoint TTL override in minutes.
    /// 0 means use global setting. Valid range when non-zero: 1-10080.
    /// </summary>
    public int TtlMinutes { get; set; }

    /// <summary>
    /// Gets or sets the per-endpoint fingerprint validation override.
    /// Null means use global setting.
    /// </summary>
    public bool? ValidateFingerprint { get; set; }

    /// <inheritdoc/>
    public bool IsReusable => true;

    /// <inheritdoc/>
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetRequiredService<IdempotencyActionFilter>();
    }
}
