// Copyright © Clarivex Technologies. All rights reserved.

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
