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
using Pervaxis.Genesis.OData.Configuration;
using Pervaxis.Genesis.OData.Filters;
using Pervaxis.Genesis.OData.Options;
using Pervaxis.Genesis.OData.Services;

namespace Pervaxis.Genesis.OData.Extensions;

/// <summary>
/// Extension methods for registering Genesis OData services.
/// </summary>
public static class ODataServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis OData services using configuration binding.
    /// Binds options from the "Genesis:OData" section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisOData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ODataOptions>(
            configuration.GetSection("Genesis:OData"));

        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Adds Genesis OData services with action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The options configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisOData(
        this IServiceCollection services,
        Action<ODataOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        RegisterCoreServices(services);
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton<QueryComplexityCalculator>();
        services.TryAddSingleton<QueryValidator>();
        services.TryAddSingleton<EntityConfigurationRegistry>();
        services.TryAddTransient<ODataQueryFilter>();
    }
}
