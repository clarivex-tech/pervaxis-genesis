using System.Text;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Validation;

namespace Pervaxis.Genesis.Caching.AWS.Security;

/// <summary>
/// Builds and validates tenant cache keys for application-enforced isolation.
/// This is not an IAM security boundary. There is no platform or unscoped fallback.
/// </summary>
/// <remarks>
/// The injected context must represent the current execution scope. Tenant identity
/// is read on every call and is never retained. Redis keys are literal strings:
/// dots, percent sequences and glob characters are preserved, without path normalization.
/// No scan or pattern API is exposed by the existing cache interface.
/// </remarks>
internal sealed class TenantCacheKeyResolver(ITenantContext? tenantContext)
{
    internal const string TenantKeyPrefix = "tenants:";
    private const int MaxKeyBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Validates a logical key and adds the current tenant's namespace.</summary>
    public string ResolveKey(string key)
    {
        var prefix = GetTenantPrefix();
        ValidateCallerKey(key);
        var fullKey = prefix + key;
        ValidateLength(fullKey);
        return fullKey;
    }

    /// <summary>Checks ownership and returns a validated logical key.</summary>
    public string StripPrefix(string fullKey)
    {
        var prefix = GetTenantPrefix();
        if (fullKey is null || !fullKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw Failure("Cache key does not belong to the current tenant.", "TenantKeyMismatch");
        }

        ValidateLength(fullKey);
        var key = fullKey[prefix.Length..];
        ValidateCallerKey(key);
        return key;
    }

    private string GetTenantPrefix()
    {
        if (tenantContext?.IsResolved != true)
        {
            throw Failure("Tenant context not resolved.", "TenantContextNotResolved");
        }

        var tenantId = tenantContext.TenantId.Value;
        if (!TenantIdValidator.IsValid(tenantId) || tenantId.Any(char.IsControl))
        {
            throw Failure("Invalid tenant ID.", "InvalidTenantId");
        }

        return TenantKeyPrefix + tenantId + ":";
    }

    private static void ValidateCallerKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl)
            || key.Contains('{', StringComparison.Ordinal) || key.Contains('}', StringComparison.Ordinal)
            || key.StartsWith(TenantKeyPrefix, StringComparison.Ordinal))
        {
            throw Failure("Invalid cache key: empty, control characters, hash tags or tenant namespaces are not allowed.", "InvalidCacheKey");
        }
    }

    private static void ValidateLength(string fullKey)
    {
        try
        {
            if (StrictUtf8.GetByteCount(fullKey) > MaxKeyBytes)
            {
                throw Failure("Cache key exceeds 1024 UTF-8 bytes including the tenant prefix.", "InvalidCacheKey");
            }
        }
        catch (EncoderFallbackException)
        {
            throw Failure("Cache key contains invalid Unicode.", "InvalidCacheKey");
        }
    }

    private static GenesisException Failure(string message, string errorCode)
        => new(nameof(TenantCacheKeyResolver), message, errorCode, null);
}
