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
