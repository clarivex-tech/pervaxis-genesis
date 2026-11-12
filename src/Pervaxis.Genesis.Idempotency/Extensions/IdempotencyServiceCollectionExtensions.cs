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
using Pervaxis.Genesis.Idempotency.Filters;
using Pervaxis.Genesis.Idempotency.Options;
using Pervaxis.Genesis.Idempotency.Services;

namespace Pervaxis.Genesis.Idempotency.Extensions;

/// <summary>
/// Extension methods for registering Genesis Idempotency services.
/// </summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis Idempotency core services using configuration binding.
    /// Binds options from the "Genesis:Idempotency" section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddGenesisIdempotencyCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<IdempotencyOptions>(
            configuration.GetSection("Genesis:Idempotency"));

        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Adds Genesis Idempotency core services using action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The options configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddGenesisIdempotencyCore(
        this IServiceCollection services,
        Action<IdempotencyOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        RegisterCoreServices(services);
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton<IIdempotencyKeyValidator, IdempotencyKeyValidator>();
        services.TryAddSingleton<IRequestFingerprintComputer, RequestFingerprintComputer>();
        services.TryAddTransient<IdempotencyActionFilter>();
    }
}
