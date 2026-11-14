using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.FeatureFlags.AWS.Extensions;

namespace Pervaxis.Genesis.FeatureFlags.AWS.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task SharedConfiguration_EvaluatesTenantTargetingInSeparateScopesAndSnapshots()
    {
        var tenantId = "acme";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Checkout.Beta:EnabledFor:0:Name"] = "Tenant",
            ["FeatureManagement:Checkout.Beta:EnabledFor:0:Parameters:AllowedTenants:0"] = "acme"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddScoped<ITenantContext>(_ =>
        {
            var context = Substitute.For<ITenantContext>();
            context.IsResolved.Returns(true);
            context.TenantId.Returns(new TenantId(tenantId));
            return context;
        });
        services.AddGenesisFeatureFlags(config);
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using (var acme = root.CreateScope())
        {
            Assert.True(await acme.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync("Checkout.Beta"));
            Assert.True(await acme.ServiceProvider.GetRequiredService<IFeatureManagerSnapshot>().IsEnabledAsync("Checkout.Beta"));
        }
        tenantId = "globex";
        using var globex = root.CreateScope();
        Assert.False(await globex.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync("Checkout.Beta"));
        Assert.False(await globex.ServiceProvider.GetRequiredService<IFeatureManagerSnapshot>().IsEnabledAsync("Checkout.Beta"));
    }

    [Fact]
    public async Task MissingTenantCannotEvaluateGlobalFlag()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddGenesisFeatureFlags(_ => { });
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();
        await Assert.ThrowsAsync<GenesisException>(() => scope.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync("Checkout.Beta"));
        await Assert.ThrowsAsync<GenesisException>(() => scope.ServiceProvider.GetRequiredService<IFeatureManagerSnapshot>().IsEnabledAsync("Checkout.Beta"));
    }
}
