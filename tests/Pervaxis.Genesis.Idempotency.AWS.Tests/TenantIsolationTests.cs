using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Idempotency.Abstractions;
using Pervaxis.Genesis.Idempotency.AWS.Fallback;
using Pervaxis.Genesis.Idempotency.AWS.Providers.DynamoDb;
using Pervaxis.Genesis.Idempotency.Options;

namespace Pervaxis.Genesis.Idempotency.AWS.Tests;

public sealed class TenantIsolationTests
{
    private static ITenantContext Tenant(string id)
    {
        var context = Substitute.For<ITenantContext>();
        context.IsResolved.Returns(true);
        context.TenantId.Returns(new TenantId(id));
        return context;
    }

    private static IdempotencyRecord Completed(string tenant) => new()
    {
        IdempotencyKey = "same", CompositeKey = tenant + "#same", Fingerprint = "request",
        IsCompleted = true, CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAtEpoch = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
        ResponseBody = tenant
    };

    [Fact]
    public async Task SharedMemoryBackend_SeparatesSameKeyAcrossTenants()
    {
        var backend = new InMemoryIdempotencyStore();
        var acme = new TenantIdempotencyStore(backend, Tenant("acme"));
        var globex = new TenantIdempotencyStore(backend, Tenant("globex"));
        Assert.True(await acme.CreateInFlightRecordAsync("acme", "same", "request"));
        Assert.True(await globex.CreateInFlightRecordAsync("globex", "same", "request"));
        Assert.True(await acme.CompleteRecordAsync("acme", "same", Completed("acme")));
        Assert.True(await globex.CompleteRecordAsync("globex", "same", Completed("globex")));
        Assert.Equal("acme", (await acme.TryGetRecordAsync("acme", "same"))!.ResponseBody);
        Assert.Equal("globex", (await globex.TryGetRecordAsync("globex", "same"))!.ResponseBody);
        await acme.DeleteRecordAsync("acme", "same");
        Assert.NotNull(await globex.TryGetRecordAsync("globex", "same"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("acme")]
    [InlineData("INVALID")]
    public async Task MissingInvalidOrForeignTenant_IsBlockedBeforeBackend(string? tenant)
    {
        var backend = Substitute.For<IIdempotencyStore>();
        var store = new TenantIdempotencyStore(backend, tenant is null ? null : Tenant(tenant));
        await Assert.ThrowsAsync<GenesisException>(() => store.TryGetRecordAsync("globex", "same"));
        await Assert.ThrowsAsync<GenesisException>(() => store.CreateInFlightRecordAsync("globex", "same", "request"));
        await Assert.ThrowsAsync<GenesisException>(() => store.CompleteRecordAsync("globex", "same", Completed("globex")));
        await Assert.ThrowsAsync<GenesisException>(() => store.DeleteRecordAsync("globex", "same"));
        Assert.Empty(backend.ReceivedCalls());
    }

    [Fact]
    public async Task CompletionRejectsForeignRecordEvenWithCorrectTenantArgument()
    {
        var backend = Substitute.For<IIdempotencyStore>();
        var store = new TenantIdempotencyStore(backend, Tenant("acme"));
        await Assert.ThrowsAsync<GenesisException>(() => store.CompleteRecordAsync("acme", "same", Completed("globex")));
        Assert.Empty(backend.ReceivedCalls());
    }

    [Fact]
    public async Task SharedDynamoTable_UsesTenantKeysForWritesAndDeletes()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>()).Returns(new PutItemResponse());
        client.DeleteItemAsync(Arg.Any<DeleteItemRequest>(), Arg.Any<CancellationToken>()).Returns(new DeleteItemResponse());
        var backend = new DynamoDbIdempotencyStore(client,
            Microsoft.Extensions.Options.Options.Create(new IdempotencyOptions { TableName = "shared" }),
            NullLogger<DynamoDbIdempotencyStore>.Instance);
        var store = new TenantIdempotencyStore(backend, Tenant("acme"));
        await store.CreateInFlightRecordAsync("acme", "same", "request");
        await store.DeleteRecordAsync("acme", "same");
        await client.Received(1).PutItemAsync(Arg.Is<PutItemRequest>(r => r.TableName == "shared" && r.Item["PK"].S == "acme#same"), Arg.Any<CancellationToken>());
        await client.Received(1).DeleteItemAsync(Arg.Is<DeleteItemRequest>(r => r.Key["PK"].S == "acme#same"), Arg.Any<CancellationToken>());
    }
}
