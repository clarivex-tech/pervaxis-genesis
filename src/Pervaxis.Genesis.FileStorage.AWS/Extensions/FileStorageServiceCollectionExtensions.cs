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
using Pervaxis.Genesis.FileStorage.AWS.Options;
using Pervaxis.Genesis.FileStorage.AWS.Providers.S3;

namespace Pervaxis.Genesis.FileStorage.AWS.Extensions;

/// <summary>
/// Extension methods for registering AWS S3 file storage services.
/// </summary>
public static class FileStorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds AWS S3 file storage services to the service collection using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing FileStorage settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FileStorageOptions>(configuration);
        services.AddSingleton<IFileStorage, S3FileStorageProvider>();

        return services;
    }

    /// <summary>
    /// Adds AWS S3 file storage services to the service collection using an action delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure file storage options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisFileStorage(
        this IServiceCollection services,
        Action<FileStorageOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddSingleton<IFileStorage, S3FileStorageProvider>();

        return services;
    }
}
