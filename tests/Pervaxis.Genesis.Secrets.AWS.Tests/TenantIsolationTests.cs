/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Secrets.AWS.Extensions;
using Pervaxis.Genesis.Secrets.AWS.Options;
using Pervaxis.Genesis.Secrets.AWS.Providers;

namespace Pervaxis.Genesis.Secrets.AWS.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task CachedValues_ExpireAndStillRequireTenantBeforeCacheAccess()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = "value" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tenant = Tenant("acme");
        var provider = new SecretsManagerProvider(client, Microsoft.Extensions.Options.Options.Create(
            new SecretsOptions { Region = "us-east-1", CacheTtlSeconds = 1 }), cache, tenant);
        Assert.Equal("value", await provider.GetSecretAsync("key"));
        tenant.IsResolved.Returns(false);
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetSecretAsync("key"));
        tenant.IsResolved.Returns(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSecretAsync("key", new CancellationToken(true)));
        await client.Received(1).GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Assert.Equal("value", await provider.GetSecretAsync("key"));
        await client.Received(2).GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    private static ITenantContext Tenant(string id)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(true);
        tenant.TenantId.Returns(new TenantId(id));
        return tenant;
    }

    [Fact]
    public async Task SharedClientAndCache_SeparateSameNameAcrossTenants()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new GetSecretValueResponse { SecretString = c.Arg<GetSecretValueRequest>().SecretId });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1" });
        var acme = new SecretsManagerProvider(client, options, cache, Tenant("acme"));
        var globex = new SecretsManagerProvider(client, options, cache, Tenant("globex"));
        Assert.Equal("tenant-acme/db/password", await acme.GetSecretAsync("db/password"));
        Assert.Equal("tenant-globex/db/password", await globex.GetSecretAsync("db/password"));
        Assert.Equal("tenant-acme/db/password", await acme.GetSecretAsync("db/password"));
        await client.Received(2).GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("INVALID")]
    public async Task MissingOrInvalidTenant_BlocksEveryRead(string? id)
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new SecretsManagerProvider(client, Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1" }), cache, id is null ? null : Tenant(id));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetSecretAsync("key"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetSecretAsync<Dictionary<string, string>>("key"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetBinarySecretAsync("key"));
        Assert.Empty(client.ReceivedCalls());
    }

    [Theory]
    [InlineData("../globex/key")]
    [InlineData("/platform/key")]
    [InlineData("arn:aws:secretsmanager:foreign")]
    [InlineData("a//b")]
    [InlineData("a%2fb")]
    [InlineData("a\\b")]
    public async Task UnsafeNames_AreRejectedBeforeAws(string name)
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new SecretsManagerProvider(client, Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetSecretAsync(name));
        Assert.Empty(client.ReceivedCalls());
    }

    [Fact]
    public async Task JsonAndBinaryReads_WorkAndCachedBinaryIsNotMutable()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Is<GetSecretValueRequest>(r => r.SecretId == "tenant-acme/json"), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = "{\"User\":\"alice\"}" });
        client.GetSecretValueAsync(Arg.Is<GetSecretValueRequest>(r => r.SecretId == "tenant-acme/cert"), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretBinary = new MemoryStream(new byte[] { 1, 2, 3 }) });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new SecretsManagerProvider(client, Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        Assert.Equal("alice", (await provider.GetSecretAsync<Dictionary<string, string>>("json"))!["User"]);
        var binary = await provider.GetBinarySecretAsync("cert");
        binary![0] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, await provider.GetBinarySecretAsync("cert"));
        await client.Received(1).GetSecretValueAsync(Arg.Is<GetSecretValueRequest>(r => r.SecretId == "tenant-acme/cert"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingSecretReturnsNull_ButServiceFailuresPropagate()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetSecretValueResponse>>(_ => throw new ResourceNotFoundException("missing"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new SecretsManagerProvider(client, Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        Assert.Null(await provider.GetSecretAsync("missing"));
        Assert.Null(await provider.GetSecretAsync("missing"));
        await client.Received(2).GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetSecretValueResponse>>(_ => throw new AmazonSecretsManagerException("denied"));
        await Assert.ThrowsAsync<AmazonSecretsManagerException>(() => provider.GetSecretAsync("denied"));
    }

    [Fact]
    public async Task Registration_UsesTenantScopesAndExplicitPlatformAccess()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new GetSecretValueResponse { SecretString = c.Arg<GetSecretValueRequest>().SecretId });
        var tenantId = "acme";
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IAmazonSecretsManager>("Genesis.Secrets.Client", client);
        services.AddScoped<ITenantContext>(_ => Tenant(tenantId));
        services.AddGenesisSecretsManager(o => o.Region = "us-east-1");
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Null(root.GetService<IPlatformSecretProvider>());
        using (var scope = root.CreateScope())
        {
            Assert.Equal("tenant-acme/key", await scope.ServiceProvider.GetRequiredService<ISecretProvider>().GetSecretAsync("key"));
        }
        tenantId = "globex";
        using (var scope = root.CreateScope())
        {
            Assert.Equal("tenant-globex/key", await scope.ServiceProvider.GetRequiredService<ISecretProvider>().GetSecretAsync("key"));
        }
        var platformServices = new ServiceCollection();
        platformServices.AddKeyedSingleton<IAmazonSecretsManager>("Genesis.Secrets.Client", client);
        platformServices.AddGenesisPlatformSecretsManager(o => o.Region = "us-east-1");
        using var platformRoot = platformServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Null(platformRoot.GetService<ISecretProvider>());
        Assert.Equal("platform/db/password", await platformRoot.GetRequiredService<IPlatformSecretProvider>().GetSecretAsync("db/password"));
        client.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task CachingCanBeDisabled_AndCancellationIsHonoredBeforeCache()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        client.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetSecretValueResponse { SecretString = "value" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new SecretsManagerProvider(client, Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1", EnableCaching = false }), cache, Tenant("acme"));
        await provider.GetSecretAsync("key");
        await provider.GetSecretAsync("key");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetSecretAsync("key", new CancellationToken(true)));
        await client.Received(2).GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DisablingIsolationIsRejected()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Throws<ArgumentException>(() => new SecretsManagerProvider(client,
            Microsoft.Extensions.Options.Options.Create(new SecretsOptions { Region = "us-east-1", EnableTenantIsolation = false }), cache, Tenant("acme")));
    }
}
