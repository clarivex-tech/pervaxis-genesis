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

using Amazon.Extensions.Configuration.SystemsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Pervaxis.Genesis.FeatureFlags.AWS.Options;

namespace Pervaxis.Genesis.FeatureFlags.AWS.Extensions;

/// <summary>
/// Extension methods for adding AWS AppConfig as a configuration source for feature flags.
/// </summary>
public static class FeatureFlagConfigurationExtensions
{
    /// <summary>
    /// Adds AWS AppConfig as a configuration source for feature flags.
    /// Should be called on the IConfigurationBuilder during host startup.
    /// Skipped in Development environment.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <param name="options">The feature flag options.</param>
    /// <returns>The configuration builder for chaining.</returns>
    public static IConfigurationBuilder AddGenesisFeatureFlagSource(
        this IConfigurationBuilder builder,
        IHostEnvironment environment,
        FeatureFlagOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        if (environment.IsDevelopment())
        {
            return builder; // Local dev uses appsettings only
        }

        var resolvedPath = options.AppConfigPath
            .Replace("{env}", environment.EnvironmentName, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return builder; // Warning logged at options validation
        }

        builder.AddSystemsManager(source =>
        {
            source.Path = resolvedPath;
            source.ReloadAfter = TimeSpan.FromSeconds(options.PollingIntervalSeconds);
            source.Optional = true; // Fallback to appsettings if unavailable
        });

        return builder;
    }
}
