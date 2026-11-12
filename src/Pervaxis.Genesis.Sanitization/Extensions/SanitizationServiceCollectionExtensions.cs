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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Sanitization.Abstractions;
using Pervaxis.Genesis.Sanitization.Diagnostics;
using Pervaxis.Genesis.Sanitization.Filters;
using Pervaxis.Genesis.Sanitization.Options;
using Pervaxis.Genesis.Sanitization.Services;

namespace Pervaxis.Genesis.Sanitization.Extensions;

/// <summary>
/// Extension methods for registering Genesis Sanitization services.
/// </summary>
public static class SanitizationServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis Sanitization services using configuration binding.
    /// Binds options from the "Genesis:Sanitization" section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisSanitization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SanitizationOptions>(
            configuration.GetSection("Genesis:Sanitization"));

        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Adds Genesis Sanitization services using action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The options configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisSanitization(
        this IServiceCollection services,
        Action<SanitizationOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        RegisterCoreServices(services);
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // ProfileRegistry — singleton, created from options at startup
        services.TryAddSingleton<ProfileRegistry>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SanitizationOptions>>().Value;
            var registry = new ProfileRegistry(options);

            // Log custom profile loading
            if (options.AllowCustomProfiles)
            {
                var logger = sp.GetRequiredService<ILogger<ProfileRegistry>>();
                foreach (var (_, definition) in options.CustomProfiles)
                {
                    SanitizationLogMessages.LogCustomProfileLoaded(
                        logger,
                        definition.Name,
                        definition.AllowedTags.Count,
                        definition.AllowedAttributes.Count);
                }
            }

            return registry;
        });

        // ISanitizer — singleton backed by GenesisSanitizer
        services.TryAddSingleton<GenesisSanitizer>(sp =>
            new GenesisSanitizer(
                sp.GetRequiredService<ProfileRegistry>(),
                sp.GetRequiredService<IOptions<SanitizationOptions>>(),
                sp.GetRequiredService<ILogger<GenesisSanitizer>>()));

        services.TryAddSingleton<ISanitizer>(sp => sp.GetRequiredService<GenesisSanitizer>());

        // Action filter for [Sanitize] attribute processing
        services.TryAddTransient<SanitizeActionFilter>(sp =>
            new SanitizeActionFilter(
                sp.GetRequiredService<GenesisSanitizer>(),
                sp.GetRequiredService<IOptions<SanitizationOptions>>()));

        // Register the action filter globally with an order that runs before validation
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.Filters.AddService<SanitizeActionFilter>(order: -100);
        });
    }
}
