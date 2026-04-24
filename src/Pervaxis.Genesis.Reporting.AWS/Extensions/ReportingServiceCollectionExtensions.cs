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
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Reporting.AWS.Options;
using Pervaxis.Genesis.Reporting.AWS.Providers;

namespace Pervaxis.Genesis.Reporting.AWS.Extensions;

/// <summary>
/// Extension methods for registering Metabase reporting services.
/// </summary>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Metabase reporting services to the service collection using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing reporting options.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configuration is null.</exception>
    public static IServiceCollection AddGenesisReporting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ReportingOptions>(configuration);
        services.AddHttpClient<IReporting, MetabaseReportingProvider>();

        return services;
    }

    /// <summary>
    /// Adds Metabase reporting services to the service collection using an action delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The action to configure reporting options.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configureOptions is null.</exception>
    public static IServiceCollection AddGenesisReporting(
        this IServiceCollection services,
        Action<ReportingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddHttpClient<IReporting, MetabaseReportingProvider>();

        return services;
    }
}
