// -------------------------------------------------------------------------
// Copyright (c) 2026 Clarivex Technologies. All rights reserved.
// Pervaxis Platform - Genesis Edition
// -------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Caching.Abstractions;
using Pervaxis.Genesis.Caching.Options;
using Pervaxis.Genesis.Caching.Providers.ElastiCache;

namespace Pervaxis.Genesis.Caching.Extensions;

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

        // Register cache provider as singleton (connection pooling)
        services.TryAddSingleton<ICache, ElastiCacheProvider>();

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

        // Register cache provider as singleton (connection pooling)
        services.TryAddSingleton<ICache, ElastiCacheProvider>();

        return services;
    }
}
