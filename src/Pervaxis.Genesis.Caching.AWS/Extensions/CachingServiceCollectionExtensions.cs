/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Caching.AWS.Security;
using Pervaxis.Genesis.Base.Exceptions;
using StackExchange.Redis;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Caching.AWS.Options;
using Pervaxis.Genesis.Caching.AWS.Providers.ElastiCache;

namespace Pervaxis.Genesis.Caching.AWS.Extensions;

/// <summary>
/// Extension methods for registering Genesis Caching services with dependency injection.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis Caching services using ElastiCache (Redis) provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section containing caching options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure options from configuration
        services.Configure<CachingOptions>(configuration);

        RegisterProvider(services);

        return services;
    }

    /// <summary>
    /// Adds Genesis Caching services using ElastiCache (Redis) provider with action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure caching options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisCaching(
        this IServiceCollection services,
        Action<CachingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // Configure options via action
        services.Configure(configureOptions);

        RegisterProvider(services);

        return services;
    }

    private static void RegisterProvider(IServiceCollection services)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CachingOptions>>().Value;
            if (!options.Validate())
            {
                throw new GenesisConfigurationException(nameof(ElastiCacheProvider), "Invalid caching configuration");
            }
            var redis = ConfigurationOptions.Parse(options.ConnectionString);
            redis.ConnectTimeout = options.ConnectTimeoutMs;
            redis.SyncTimeout = options.SyncTimeoutMs;
            redis.AbortOnConnectFail = options.AbortOnConnectFail;
            redis.Ssl = options.UseSsl && !options.UseLocalEmulator;
            return ConnectionMultiplexer.Connect(redis);
        });
        services.TryAddScoped<TenantCacheKeyResolver>(sp => new TenantCacheKeyResolver(sp.GetService<ITenantContext>()));
        services.TryAddScoped<ICache>(sp => new ElastiCacheProvider(
            sp.GetRequiredService<IOptions<CachingOptions>>(),
            sp.GetRequiredService<ILogger<ElastiCacheProvider>>(),
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetService<ITenantContext>(),
            sp.GetRequiredService<TenantCacheKeyResolver>()));
    }

}
