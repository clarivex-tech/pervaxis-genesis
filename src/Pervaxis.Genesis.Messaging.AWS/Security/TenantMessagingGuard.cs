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

using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Validation;

namespace Pervaxis.Genesis.Messaging.AWS.Security;

/// <summary>Fail-closed tenant validation for one messaging provider scope.</summary>
internal sealed class TenantMessagingGuard(ITenantContext? context)
{
    private readonly object _sync = new();
    private string? _boundTenant;

    public string RequireTenant()
    {
        if (context?.IsResolved != true)
        {
            throw Failure("Tenant context not resolved.", "TENANT_CONTEXT_NOT_RESOLVED");
        }
        var tenant = context.TenantId.Value;
        var validation = TenantIdValidator.Validate(tenant);
        if (!validation.IsValid || tenant.Any(char.IsControl))
        {
            throw Failure($"Invalid tenant ID '{tenant}': {validation.ErrorMessage}", "INVALID_TENANT_ID");
        }
        lock (_sync)
        {
            if (_boundTenant is not null && !StringComparer.Ordinal.Equals(_boundTenant, tenant))
            {
                throw Failure("Tenant context changed during the provider scope.", "TENANT_CONTEXT_CHANGED");
            }
            _boundTenant = tenant;
        }
        return tenant;
    }

    public void RequireWorker(bool enabled)
    {
        lock (_sync)
        {
            if (!enabled || context?.IsResolved == true || _boundTenant is not null)
            {
                throw Failure("Shared queue consumption requires an explicitly registered worker outside any tenant scope.", "SHARED_CONSUMER_REQUIRED");
            }
        }
    }

    private static GenesisException Failure(string message, string code)
        => new(nameof(TenantMessagingGuard), message, code, null);
}
