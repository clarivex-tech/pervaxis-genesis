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
using Pervaxis.Genesis.AIAssistance.AWS.Options;
using Pervaxis.Genesis.AIAssistance.AWS.Providers;

namespace Pervaxis.Genesis.AIAssistance.AWS.Extensions;

/// <summary>
/// Extension methods for registering AWS Bedrock AI assistance services.
/// </summary>
public static class AIAssistanceServiceCollectionExtensions
{
    /// <summary>
    /// Adds AWS Bedrock AI assistance services to the service collection using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing AI assistance options.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configuration is null.</exception>
    public static IServiceCollection AddGenesisAIAssistance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<AIAssistanceOptions>(configuration);
        services.AddSingleton<IAIAssistant, BedrockAIAssistantProvider>();

        return services;
    }

    /// <summary>
    /// Adds AWS Bedrock AI assistance services to the service collection using an action delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The action to configure AI assistance options.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when services or configureOptions is null.</exception>
    public static IServiceCollection AddGenesisAIAssistance(
        this IServiceCollection services,
        Action<AIAssistanceOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddSingleton<IAIAssistant, BedrockAIAssistantProvider>();

        return services;
    }
}
