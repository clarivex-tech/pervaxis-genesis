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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pervaxis.Genesis.Base.Abstractions;
using Pervaxis.Genesis.Base.Configuration;

namespace Pervaxis.Genesis.Base.Extensions;

/// <summary>
/// Extension methods for registering Genesis base services with dependency injection.
/// </summary>
public static class GenesisServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis base services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisBase(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register template configuration loader as singleton
        services.TryAddSingleton<ITemplateConfigurationLoader, TemplateConfigurationLoader>();

        return services;
    }
}
