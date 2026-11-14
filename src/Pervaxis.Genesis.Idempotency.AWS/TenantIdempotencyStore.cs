using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.MultiTenancy;
using Pervaxis.Genesis.Idempotency.Abstractions;

namespace Pervaxis.Genesis.Idempotency.AWS;

/// <summary>Scoped tenant boundary around the shared persistence backend.</summary>
internal sealed class TenantIdempotencyStore(IIdempotencyStore inner, ITenantContext? context) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> TryGetRecordAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var tenant = Validate(tenantId, idempotencyKey);
        var record = await inner.TryGetRecordAsync(tenant, idempotencyKey, cancellationToken);
        if (record is not null && record.CompositeKey != $"{tenant}#{idempotencyKey}")
        {
            throw new GenesisException(nameof(TenantIdempotencyStore), "Record does not belong to this tenant/key.", "TENANT_MISMATCH", null);
        }
        return record;
    }

    public Task<bool> CreateInFlightRecordAsync(string tenantId, string idempotencyKey, string fingerprint, CancellationToken cancellationToken = default)
        => inner.CreateInFlightRecordAsync(Validate(tenantId, idempotencyKey), idempotencyKey, fingerprint, cancellationToken);

    public Task<bool> CompleteRecordAsync(string tenantId, string idempotencyKey, IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        var tenant = Validate(tenantId, idempotencyKey);
        ArgumentNullException.ThrowIfNull(record);
        if (record.CompositeKey != $"{tenant}#{idempotencyKey}")
        {
            throw new GenesisException(nameof(TenantIdempotencyStore), "Cannot complete a foreign record.", "TENANT_MISMATCH", null);
        }
        return inner.CompleteRecordAsync(tenant, idempotencyKey, record, cancellationToken);
    }

    public Task DeleteRecordAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
        => inner.DeleteRecordAsync(Validate(tenantId, idempotencyKey), idempotencyKey, cancellationToken);

    private string Validate(string tenantId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return TenantIsolation.RequireTenant(context, tenantId);
    }
}
