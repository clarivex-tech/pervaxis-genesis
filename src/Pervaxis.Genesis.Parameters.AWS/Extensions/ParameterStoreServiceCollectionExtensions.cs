/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Amazon;
using Amazon.SimpleSystemsManagement;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Parameters.AWS.Options;
using Pervaxis.Genesis.Parameters.AWS.Providers;

namespace Pervaxis.Genesis.Parameters.AWS.Extensions;

/// <summary>Registers shared AWS clients with separate tenant and platform read services.</summary>
public static class ParameterStoreServiceCollectionExtensions
{
    private const string ClientKey = "Genesis.Parameters.Client";

    /// <summary>Registers scoped tenant reads using the Genesis:Parameters section.</summary>
    public static IServiceCollection AddGenesisParameterStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddGenesisParameterStore(o => configuration.GetSection("Genesis:Parameters").Bind(o));
    }

    /// <summary>Registers scoped tenant reads. The host must supply a trusted tenant context.</summary>
    public static IServiceCollection AddGenesisParameterStore(this IServiceCollection services, Action<ParameterStoreOptions> configureOptions)
    {
        RegisterShared(services, configureOptions);
        services.TryAddScoped<IParameterProvider>(sp => new ParameterStoreProvider(
            sp.GetRequiredKeyedService<IAmazonSimpleSystemsManagement>(ClientKey),
            sp.GetRequiredService<IOptions<ParameterStoreOptions>>(),
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetService<Pervaxis.Core.Abstractions.MultiTenancy.ITenantContext>()));
        return services;
    }

    /// <summary>Explicitly registers platform startup reads using the Genesis:Parameters section.</summary>
    public static IServiceCollection AddGenesisPlatformParameterStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddGenesisPlatformParameterStore(o => configuration.GetSection("Genesis:Parameters").Bind(o));
    }

    /// <summary>Explicitly registers platform access; never expose this service through tenant request APIs.</summary>
    public static IServiceCollection AddGenesisPlatformParameterStore(this IServiceCollection services, Action<ParameterStoreOptions> configureOptions)
    {
        RegisterShared(services, configureOptions);
        services.TryAddSingleton<IPlatformParameterProvider>(sp => new PlatformParameterProvider(new ParameterReader(
            sp.GetRequiredKeyedService<IAmazonSimpleSystemsManagement>(ClientKey),
            sp.GetRequiredService<IOptions<ParameterStoreOptions>>().Value,
            sp.GetRequiredService<IMemoryCache>())));
        return services;
    }

    private static void RegisterShared(IServiceCollection services, Action<ParameterStoreOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);
        services.Configure(configureOptions);
        services.AddMemoryCache();
        services.TryAddKeyedSingleton<IAmazonSimpleSystemsManagement>(ClientKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<ParameterStoreOptions>>().Value;
            if (!options.Validate())
            {
                throw new ArgumentException("Invalid Parameters options. Tenant isolation must remain enabled.", nameof(configureOptions));
            }
            var config = new AmazonSimpleSystemsManagementConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) };
            if (options.UseLocalEmulator && options.LocalEmulatorUrl is not null)
            {
                config.ServiceURL = options.LocalEmulatorUrl.AbsoluteUri;
                config.AuthenticationRegion = options.Region;
            }
            return new AmazonSimpleSystemsManagementClient(config);
        });
    }
}
