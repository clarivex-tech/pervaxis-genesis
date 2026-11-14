using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Validation;

namespace Pervaxis.Genesis.Base.MultiTenancy;

/// <summary>Basic application-level tenant validation for shared Genesis resources.</summary>
public static class TenantIsolation
{
    /// <summary>Requires a valid trusted tenant and rejects a conflicting caller-supplied tenant.</summary>
    /// <param name="context">Trusted execution tenant context.</param>
    /// <param name="claimedTenant">Optional tenant claimed by a method argument or stored record.</param>
    /// <returns>The trusted tenant identifier.</returns>
    public static string RequireTenant(ITenantContext? context, string? claimedTenant = null)
    {
        if (context?.IsResolved != true)
        {
            throw new GenesisException(nameof(TenantIsolation), "Tenant context not resolved.", "TENANT_CONTEXT_NOT_RESOLVED", null);
        }
        var tenant = context.TenantId.Value;
        if (!TenantIdValidator.IsValid(tenant) || tenant.Any(char.IsControl))
        {
            throw new GenesisException(nameof(TenantIsolation), "Invalid tenant ID.", "INVALID_TENANT_ID", null);
        }
        if (claimedTenant is not null && !StringComparer.Ordinal.Equals(tenant, claimedTenant))
        {
            throw new GenesisException(nameof(TenantIsolation), "Tenant does not match the execution context.", "TENANT_MISMATCH", null);
        }
        return tenant;
    }
}
