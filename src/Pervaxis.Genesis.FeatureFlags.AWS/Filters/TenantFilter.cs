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

using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;
using Pervaxis.Core.Abstractions.MultiTenancy;

namespace Pervaxis.Genesis.FeatureFlags.AWS.Filters;

/// <summary>
/// Feature filter that evaluates flag state based on the current tenant identity.
/// Matches the current tenant ID against a configured list of allowed tenants
/// using case-insensitive ordinal comparison.
/// </summary>
[FilterAlias("Tenant")]
public sealed class TenantFilter : IFeatureFilter
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantFilter"/> class.
    /// </summary>
    /// <param name="tenantContext">The tenant context (optional, may be null).</param>
    public TenantFilter(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Evaluates whether the current tenant is in the AllowedTenants list.
    /// </summary>
    /// <param name="context">The feature filter evaluation context.</param>
    /// <returns>True if the current tenant is allowed; false otherwise.</returns>
    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_tenantContext is null || !_tenantContext.IsResolved)
        {
            return Task.FromResult(false);
        }

        var tenantId = _tenantContext.TenantId.Value.ToString();
        var allowedTenants = context.Parameters
            .GetSection("AllowedTenants")
            .Get<string[]>() ?? [];

        if (allowedTenants.Length == 0)
        {
            return Task.FromResult(false);
        }

        var isAllowed = allowedTenants.Contains(
            tenantId, StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(isAllowed);
    }
}
