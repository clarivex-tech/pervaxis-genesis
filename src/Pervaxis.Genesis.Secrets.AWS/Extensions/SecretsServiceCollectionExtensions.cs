/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Amazon;
using Amazon.SecretsManager;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Secrets.AWS.Options;
using Pervaxis.Genesis.Secrets.AWS.Providers;

namespace Pervaxis.Genesis.Secrets.AWS.Extensions;

/// <summary>Registers shared AWS clients with separate tenant and platform read services.</summary>
public static class SecretsServiceCollectionExtensions
{
    private const string ClientKey = "Genesis.Secrets.Client";

    /// <summary>Registers scoped tenant reads using the Genesis:Secrets section.</summary>
    public static IServiceCollection AddGenesisSecretsManager(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddGenesisSecretsManager(o => configuration.GetSection("Genesis:Secrets").Bind(o));
    }

    /// <summary>Registers scoped tenant reads. The host must supply a trusted tenant context.</summary>
    public static IServiceCollection AddGenesisSecretsManager(this IServiceCollection services, Action<SecretsOptions> configureOptions)
    {
        RegisterShared(services, configureOptions);
        services.TryAddScoped<ISecretProvider>(sp => new SecretsManagerProvider(
            sp.GetRequiredKeyedService<IAmazonSecretsManager>(ClientKey),
            sp.GetRequiredService<IOptions<SecretsOptions>>(),
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetService<Pervaxis.Core.Abstractions.MultiTenancy.ITenantContext>()));
        return services;
    }

    /// <summary>Explicitly registers platform startup reads using the Genesis:Secrets section.</summary>
    public static IServiceCollection AddGenesisPlatformSecretsManager(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddGenesisPlatformSecretsManager(o => configuration.GetSection("Genesis:Secrets").Bind(o));
    }

    /// <summary>Explicitly registers platform access; never expose this service through tenant request APIs.</summary>
    public static IServiceCollection AddGenesisPlatformSecretsManager(this IServiceCollection services, Action<SecretsOptions> configureOptions)
    {
        RegisterShared(services, configureOptions);
        services.TryAddSingleton<IPlatformSecretProvider>(sp => new PlatformSecretProvider(new SecretReader(
            sp.GetRequiredKeyedService<IAmazonSecretsManager>(ClientKey),
            sp.GetRequiredService<IOptions<SecretsOptions>>().Value,
            sp.GetRequiredService<IMemoryCache>())));
        return services;
    }

    private static void RegisterShared(IServiceCollection services, Action<SecretsOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);
        services.Configure(configureOptions);
        services.AddMemoryCache();
        services.TryAddKeyedSingleton<IAmazonSecretsManager>(ClientKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<SecretsOptions>>().Value;
            if (!options.Validate())
            {
                throw new ArgumentException("Invalid Secrets options. Tenant isolation must remain enabled.", nameof(configureOptions));
            }
            var config = new AmazonSecretsManagerConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) };
            if (options.UseLocalEmulator && options.LocalEmulatorUrl is not null)
            {
                config.ServiceURL = options.LocalEmulatorUrl.AbsoluteUri;
                config.AuthenticationRegion = options.Region;
            }
            return new AmazonSecretsManagerClient(config);
        });
    }
}
