/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Parameters.AWS.Extensions;
using Pervaxis.Genesis.Parameters.AWS.Options;
using Pervaxis.Genesis.Parameters.AWS.Providers;

namespace Pervaxis.Genesis.Parameters.AWS.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task CachedValues_ExpireAndStillRequireTenantBeforeCacheAccess()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParameterResponse { Parameter = new Parameter { Value = "value" } });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tenant = Tenant("acme");
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(
            new ParameterStoreOptions { Region = "us-east-1", CacheTtlSeconds = 1 }), cache, tenant);
        Assert.Equal("value", await provider.GetParameterAsync("key"));
        tenant.IsResolved.Returns(false);
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetParameterAsync("key"));
        tenant.IsResolved.Returns(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParameterAsync("key", new CancellationToken(true)));
        await client.Received(1).GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>());
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        Assert.Equal("value", await provider.GetParameterAsync("key"));
        await client.Received(2).GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>());
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
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new GetParameterResponse { Parameter = new Parameter { Value = c.Arg<GetParameterRequest>().Name } });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" });
        var acme = new ParameterStoreProvider(client, options, cache, Tenant("acme"));
        var globex = new ParameterStoreProvider(client, options, cache, Tenant("globex"));
        Assert.Equal("/tenant/acme/api/url", await acme.GetParameterAsync("api/url"));
        Assert.Equal("/tenant/globex/api/url", await globex.GetParameterAsync("api/url"));
        Assert.Equal("/tenant/acme/api/url", await acme.GetParameterAsync("api/url"));
        await client.Received(2).GetParameterAsync(Arg.Is<GetParameterRequest>(r => r.WithDecryption == true), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("INVALID")]
    public async Task MissingOrInvalidTenant_BlocksEveryRead(string? id)
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" }), cache, id is null ? null : Tenant(id));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetParameterAsync("key"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetParameterAsync<Dictionary<string, string>>("key"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetParametersByPathAsync("/"));
        Assert.Empty(client.ReceivedCalls());
    }

    [Theory]
    [InlineData("../globex/key")]
    [InlineData("/platform/key")]
    [InlineData("arn:aws:ssm:foreign")]
    [InlineData("a//b")]
    [InlineData("a%2fb")]
    [InlineData("a\\b")]
    public async Task UnsafeNames_AreRejectedBeforeAws(string name)
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetParameterAsync(name));
        Assert.Empty(client.ReceivedCalls());
    }

    [Fact]
    public async Task PathReads_KeepTenantPrefixAcrossPagesAndReturnRelativeNames()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<GetParametersByPathRequest>().NextToken is null
                ? new GetParametersByPathResponse { Parameters = [new Parameter { Name = "/tenant/acme/email/host", Value = "smtp" }], NextToken = "page2" }
                : new GetParametersByPathResponse { Parameters = [new Parameter { Name = "/tenant/acme/email/port", Value = "587" }] });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        var values = await provider.GetParametersByPathAsync("/email", recursive: false);
        Assert.Equal(2, values.Count);
        Assert.Equal("smtp", values["email/host"]);
        Assert.Equal("587", values["email/port"]);
        await client.Received(2).GetParametersByPathAsync(Arg.Is<GetParametersByPathRequest>(r =>
            r.Path == "/tenant/acme/email/" && r.Recursive == false && r.WithDecryption == true && r.MaxResults == 10), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/tenant/globex/key")]
    [InlineData("/tenant/acme-other/key")]
    [InlineData("/tenant/acme/email-other/key")]
    public async Task PathReads_RejectOutOfScopeResponses(string returnedName)
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse { Parameters = [new Parameter { Name = returnedName, Value = "foreign" }] });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        await Assert.ThrowsAsync<GenesisException>(() => provider.GetParametersByPathAsync("email"));
    }

    [Fact]
    public async Task EmptyPathPage_WithNextTokenStillContinues()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse { NextToken = "next" }, new GetParametersByPathResponse());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        Assert.Empty(await provider.GetParametersByPathAsync("/"));
        await client.Received(2).GetParametersByPathAsync(Arg.Is<GetParametersByPathRequest>(r => r.Path == "/tenant/acme/"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JsonReadsAndMissingParameters_KeepFailureSemantics()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParameterResponse { Parameter = new Parameter { Value = "{\"Port\":587}" } });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1" }), cache, Tenant("acme"));
        Assert.Equal(587, (await provider.GetParameterAsync<Dictionary<string, int>>("json"))!["Port"]);
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetParameterResponse>>(_ => throw new ParameterNotFoundException("missing"));
        Assert.Null(await provider.GetParameterAsync("missing"));
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetParameterResponse>>(_ => throw new AmazonSimpleSystemsManagementException("denied"));
        await Assert.ThrowsAsync<AmazonSimpleSystemsManagementException>(() => provider.GetParameterAsync("denied"));
    }

    [Fact]
    public async Task Registration_UsesTenantScopesAndExplicitPlatformAccess()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => new GetParameterResponse { Parameter = new Parameter { Value = c.Arg<GetParameterRequest>().Name } });
        var tenantId = "acme";
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IAmazonSimpleSystemsManagement>("Genesis.Parameters.Client", client);
        services.AddScoped<ITenantContext>(_ => Tenant(tenantId));
        services.AddGenesisParameterStore(o => o.Region = "us-east-1");
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Null(root.GetService<IPlatformParameterProvider>());
        using (var scope = root.CreateScope())
        {
            Assert.Equal("/tenant/acme/key", await scope.ServiceProvider.GetRequiredService<IParameterProvider>().GetParameterAsync("key"));
        }
        tenantId = "globex";
        using (var scope = root.CreateScope())
        {
            Assert.Equal("/tenant/globex/key", await scope.ServiceProvider.GetRequiredService<IParameterProvider>().GetParameterAsync("key"));
        }
        var platformServices = new ServiceCollection();
        platformServices.AddKeyedSingleton<IAmazonSimpleSystemsManagement>("Genesis.Parameters.Client", client);
        platformServices.AddGenesisPlatformParameterStore(o => o.Region = "us-east-1");
        using var platformRoot = platformServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Null(platformRoot.GetService<IParameterProvider>());
        Assert.Equal("/platform/api/url", await platformRoot.GetRequiredService<IPlatformParameterProvider>().GetParameterAsync("api/url"));
        client.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task CachingCanBeDisabled_AndCancellationIsHonored()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        client.GetParameterAsync(Arg.Any<GetParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParameterResponse { Parameter = new Parameter { Value = "value" } });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ParameterStoreProvider(client, Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions
            { Region = "us-east-1", EnableCaching = false, WithDecryption = false }), cache, Tenant("acme"));
        await provider.GetParameterAsync("key");
        await provider.GetParameterAsync("key");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParameterAsync("key", new CancellationToken(true)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParametersByPathAsync("/", cancellationToken: new CancellationToken(true)));
        await client.Received(2).GetParameterAsync(Arg.Is<GetParameterRequest>(r => r.WithDecryption == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DisablingIsolationIsRejected()
    {
        var client = Substitute.For<IAmazonSimpleSystemsManagement>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Throws<ArgumentException>(() => new ParameterStoreProvider(client,
            Microsoft.Extensions.Options.Options.Create(new ParameterStoreOptions { Region = "us-east-1", EnableTenantIsolation = false }), cache, Tenant("acme")));
    }
}
