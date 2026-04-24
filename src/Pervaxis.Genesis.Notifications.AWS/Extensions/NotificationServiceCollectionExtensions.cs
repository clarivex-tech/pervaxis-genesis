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
using Pervaxis.Genesis.Notifications.AWS.Options;
using Pervaxis.Genesis.Notifications.AWS.Providers;

namespace Pervaxis.Genesis.Notifications.AWS.Extensions;

/// <summary>
/// Extension methods for registering AWS notification services.
/// </summary>
public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Adds AWS notification services to the service collection using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing notification settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<NotificationOptions>(configuration);
        services.AddSingleton<INotification, AwsNotificationProvider>();

        return services;
    }

    /// <summary>
    /// Adds AWS notification services to the service collection using an action delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure notification options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisNotifications(
        this IServiceCollection services,
        Action<NotificationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddSingleton<INotification, AwsNotificationProvider>();

        return services;
    }
}
