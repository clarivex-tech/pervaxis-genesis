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

using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sns;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;

namespace Pervaxis.Genesis.Messaging.AWS.Extensions;

/// <summary>
/// Extension methods for registering Genesis Messaging services with dependency injection.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    internal const string SqsClientKey = "Genesis.Messaging.Sqs";
    internal const string SnsClientKey = "Genesis.Messaging.Sns";

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
        RegisterSqs(services);
        services.TryAddScoped<IMessaging>(sp => sp.GetRequiredService<SqsMessagingProvider>());

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
        RegisterSqs(services);
        services.TryAddScoped<IMessaging>(sp => sp.GetRequiredService<SqsMessagingProvider>());

        return services;
    }

    /// <summary>
    /// Registers the SNS messaging provider as <see cref="IMessaging"/>.
    /// Suitable for publishing tenant-stamped messages to shared topics.
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
        RegisterSns(services);
        services.TryAddScoped<IMessaging>(sp => sp.GetRequiredService<SnsMessagingProvider>());

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
        RegisterSns(services);
        services.TryAddScoped<IMessaging>(sp => sp.GetRequiredService<SnsMessagingProvider>());

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
        RegisterSqs(services);
        RegisterSns(services);
        services.TryAddKeyedScoped<IMessaging>("sqs", (sp, _) => sp.GetRequiredService<SqsMessagingProvider>());
        services.TryAddKeyedScoped<IMessaging>("sns", (sp, _) => sp.GetRequiredService<SnsMessagingProvider>());

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
        RegisterSqs(services);
        RegisterSns(services);
        services.TryAddKeyedScoped<IMessaging>("sqs", (sp, _) => sp.GetRequiredService<SqsMessagingProvider>());
        services.TryAddKeyedScoped<IMessaging>("sns", (sp, _) => sp.GetRequiredService<SnsMessagingProvider>());

        return services;
    }
    private static void RegisterSqs(IServiceCollection services)
    {
        services.TryAddKeyedSingleton<IAmazonSQS>(SqsClientKey, (sp, _) =>
            SqsMessagingProvider.CreateClient(sp.GetRequiredService<IOptions<MessagingOptions>>().Value));
        services.TryAddScoped<SqsMessagingProvider>(sp => new SqsMessagingProvider(
            sp.GetRequiredService<IOptions<MessagingOptions>>(),
            sp.GetRequiredService<ILogger<SqsMessagingProvider>>(),
            sp.GetRequiredKeyedService<IAmazonSQS>(SqsClientKey),
            sp.GetService<ITenantContext>(), sp.GetService<TimeProvider>()));
    }

    private static void RegisterSns(IServiceCollection services)
    {
        services.TryAddKeyedSingleton<IAmazonSimpleNotificationService>(SnsClientKey, (sp, _) =>
            SnsMessagingProvider.CreateClient(sp.GetRequiredService<IOptions<MessagingOptions>>().Value));
        services.TryAddScoped<SnsMessagingProvider>(sp => new SnsMessagingProvider(
            sp.GetRequiredService<IOptions<MessagingOptions>>(),
            sp.GetRequiredService<ILogger<SnsMessagingProvider>>(),
            sp.GetRequiredKeyedService<IAmazonSimpleNotificationService>(SnsClientKey),
            sp.GetService<ITenantContext>(), sp.GetService<TimeProvider>()));
    }

    /// <summary>
    /// Explicitly registers a trusted cross-tenant queue worker. Do not expose this
    /// interface to tenant request handlers. Requires ITenantScopeFactory and an
    /// ambient ITenantContext that follows the scope factory.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Shared messaging configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGenesisSharedQueueConsumer(
        this IServiceCollection services, Action<MessagingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);
        services.Configure(configureOptions);
        return RegisterWorker(services);
    }

    /// <summary>Explicitly registers a trusted shared-queue worker from configuration.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Messaging configuration section.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGenesisSharedQueueConsumer(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<MessagingOptions>(configuration);
        return RegisterWorker(services);
    }

    private static IServiceCollection RegisterWorker(IServiceCollection services)
    {
        RegisterSqs(services);
        services.TryAddScoped<ISharedQueueConsumer>(sp => new SqsMessagingProvider(
            sp.GetRequiredService<IOptions<MessagingOptions>>(),
            sp.GetRequiredService<ILogger<SqsMessagingProvider>>(),
            sp.GetRequiredKeyedService<IAmazonSQS>(SqsClientKey),
            sp.GetRequiredService<ITenantContext>(), sp.GetService<TimeProvider>(),
            sp.GetRequiredService<ITenantScopeFactory>(), sharedConsumer: true,
            serviceScopeFactory: sp.GetRequiredService<IServiceScopeFactory>()));
        return services;
    }
}
