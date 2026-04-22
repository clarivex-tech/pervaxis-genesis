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
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sns;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;

namespace Pervaxis.Genesis.Messaging.AWS.Extensions;

/// <summary>
/// Extension methods for registering Genesis Messaging services with dependency injection.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQS messaging provider as <see cref="IMessaging"/>.
    /// Suitable for queue-based publish, receive, and delete workflows.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section containing messaging options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisSqsMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MessagingOptions>(configuration);
        services.TryAddSingleton<IMessaging, SqsMessagingProvider>();

        return services;
    }

    /// <summary>
    /// Registers the SQS messaging provider as <see cref="IMessaging"/> with action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure messaging options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisSqsMessaging(
        this IServiceCollection services,
        Action<MessagingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.TryAddSingleton<IMessaging, SqsMessagingProvider>();

        return services;
    }

    /// <summary>
    /// Registers the SNS messaging provider as <see cref="IMessaging"/>.
    /// Suitable for topic-based publish and subscribe workflows.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section containing messaging options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisSnsMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MessagingOptions>(configuration);
        services.TryAddSingleton<IMessaging, SnsMessagingProvider>();

        return services;
    }

    /// <summary>
    /// Registers the SNS messaging provider as <see cref="IMessaging"/> with action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure messaging options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisSnsMessaging(
        this IServiceCollection services,
        Action<MessagingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.TryAddSingleton<IMessaging, SnsMessagingProvider>();

        return services;
    }

    /// <summary>
    /// Registers both SQS and SNS providers as keyed <see cref="IMessaging"/> services.
    /// Inject with <c>[FromKeyedServices("sqs")]</c> or <c>[FromKeyedServices("sns")]</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration section containing messaging options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MessagingOptions>(configuration);
        services.TryAddKeyedSingleton<IMessaging, SqsMessagingProvider>("sqs");
        services.TryAddKeyedSingleton<IMessaging, SnsMessagingProvider>("sns");

        return services;
    }

    /// <summary>
    /// Registers both SQS and SNS providers as keyed <see cref="IMessaging"/> services
    /// with action-based configuration.
    /// Inject with <c>[FromKeyedServices("sqs")]</c> or <c>[FromKeyedServices("sns")]</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure messaging options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisMessaging(
        this IServiceCollection services,
        Action<MessagingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.TryAddKeyedSingleton<IMessaging, SqsMessagingProvider>("sqs");
        services.TryAddKeyedSingleton<IMessaging, SnsMessagingProvider>("sns");

        return services;
    }
}
