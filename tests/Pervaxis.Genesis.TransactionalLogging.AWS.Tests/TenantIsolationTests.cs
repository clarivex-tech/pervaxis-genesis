using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Models;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Tests;

public sealed class TenantIsolationTests
{
    private static ITenantContext Tenant(string id)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.TenantId.Returns(new TenantId(id));
        return tenant;
    }

    [Fact]
    public async Task SharedDynamoAndS3_UseTrustedTenantKeysAndRejectOtherTenant()
    {
        var tenant = Tenant("acme");
        var dynamo = Substitute.For<IAmazonDynamoDB>();
        var s3 = Substitute.For<IAmazonS3>();
        dynamo.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>()).Returns(new PutItemResponse());
        s3.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(new PutObjectResponse());
        dynamo.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>()).Returns(new QueryResponse());
        var options = Microsoft.Extensions.Options.Options.Create(new TransactionalLoggingOptions
        {
            Region = "us-east-1", TableName = "shared", BucketName = "shared",
            EnableTenantIsolation = false, Resilience = new() { Enabled = false }
        });
        using var overflow = new S3OverflowStore(s3, options, NullLogger<S3OverflowStore>.Instance, tenant);
        using var store = new DynamoDbTransactionLogStore(dynamo, overflow, options, NullLogger<DynamoDbTransactionLogStore>.Instance, tenant);
        var own = new TransactionContext { TenantId = "acme" };
        await store.PersistAsync(own);
        var key = await overflow.WriteOverflowAsync(own);
        Assert.StartsWith("acme/", key, StringComparison.Ordinal);
        await dynamo.Received(1).PutItemAsync(Arg.Is<PutItemRequest>(r => r.Item["PK"].S.StartsWith("acme#", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await store.QueryAsync(new TransactionLogQuery { TransactionId = "known-id" });
        await dynamo.Received(1).QueryAsync(Arg.Is<QueryRequest>(r => r.FilterExpression == "TenantId = :tenantId" && r.ExpressionAttributeValues[":tenantId"].S == "acme"), Arg.Any<CancellationToken>());
        var foreign = new TransactionContext { TenantId = "globex" };
        await Assert.ThrowsAsync<GenesisException>(() => store.PersistAsync(foreign));
        await Assert.ThrowsAsync<GenesisException>(() => overflow.WriteOverflowAsync(foreign));
        await Assert.ThrowsAsync<GenesisException>(() => store.QueryAsync(new TransactionLogQuery { TenantId = "globex" }));
        await dynamo.Received(1).PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>());
        await s3.Received(1).PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SharedInMemoryLogs_CannotQueryOtherTenantsTransaction()
    {
        var tenant = Tenant("acme");
        var store = new InMemoryTransactionLogStore(tenant);
        var acmeLog = new TransactionContext { TenantId = "acme" };
        await store.PersistAsync(acmeLog);
        tenant.TenantId.Returns(new TenantId("globex"));
        await store.PersistAsync(new TransactionContext { TenantId = "globex" });
        Assert.Empty((await store.QueryAsync(new TransactionLogQuery { TransactionId = acmeLog.TransactionId })).Items);
        Assert.Equal("globex", Assert.Single((await store.QueryAsync(new TransactionLogQuery())).Items).TenantId);
    }

    [Fact]
    public async Task MissingTenantCannotPersistOrQuery()
    {
        var store = new InMemoryTransactionLogStore();
        await Assert.ThrowsAsync<GenesisException>(() => store.PersistAsync(new TransactionContext { TenantId = "acme" }));
        await Assert.ThrowsAsync<GenesisException>(() => store.QueryAsync(new TransactionLogQuery()));
        Assert.Empty(store.Store);
    }
}
