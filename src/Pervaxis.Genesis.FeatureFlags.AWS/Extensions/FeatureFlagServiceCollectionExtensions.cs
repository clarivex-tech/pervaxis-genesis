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
using Microsoft.FeatureManagement;
using Pervaxis.Genesis.FeatureFlags.AWS.Filters;
using Pervaxis.Genesis.FeatureFlags.AWS.Observability;
using Pervaxis.Genesis.FeatureFlags.AWS.Options;

namespace Pervaxis.Genesis.FeatureFlags.AWS.Extensions;

/// <summary>
/// Extension methods for registering Genesis Feature Flags services.
/// </summary>
public static class FeatureFlagServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis Feature Flags services using AWS AppConfig provider.
    /// Binds options from the "Genesis:FeatureFlags" configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FeatureFlagOptions>(
            configuration.GetSection("Genesis:FeatureFlags"));

        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Adds Genesis Feature Flags services with action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The options configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisFeatureFlags(
        this IServiceCollection services,
        Action<FeatureFlagOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        RegisterCoreServices(services);
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // Register Microsoft.FeatureManagement with built-in + custom filters
        services.AddFeatureManagement()
            .AddFeatureFilter<TenantFilter>();

        // Register state tracker as singleton
        services.TryAddSingleton<FeatureFlagStateTracker>();

        // Decorate IFeatureManager with observability interceptor
        services.Decorate<IFeatureManager, FeatureFlagObservabilityInterceptor>();
    }
}
