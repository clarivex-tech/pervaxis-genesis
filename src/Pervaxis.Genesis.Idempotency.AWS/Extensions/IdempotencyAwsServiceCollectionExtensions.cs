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

using System.Diagnostics.CodeAnalysis;
using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Idempotency.Abstractions;
using Pervaxis.Genesis.Idempotency.AWS.Fallback;
using Pervaxis.Genesis.Idempotency.AWS.Providers.DynamoDb;
using Pervaxis.Genesis.Idempotency.Extensions;
using Pervaxis.Genesis.Idempotency.Options;

namespace Pervaxis.Genesis.Idempotency.AWS.Extensions;

/// <summary>
/// Extension methods for registering Genesis Idempotency services with AWS DynamoDB backing.
/// </summary>
public static class IdempotencyAwsServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis Idempotency services with DynamoDB store using configuration binding.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisIdempotencyAws(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGenesisIdempotency(configuration);
        RegisterAwsServices(services);
        return services;
    }

    /// <summary>
    /// Adds Genesis Idempotency services with DynamoDB store using action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The options configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisIdempotencyAws(
        this IServiceCollection services,
        Action<IdempotencyOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddGenesisIdempotency(configureOptions);
        RegisterAwsServices(services);
        return services;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fallback to in-memory store when DynamoDB is unavailable in local emulator mode.")]
    private static void RegisterAwsServices(IServiceCollection services)
    {
        // Register DynamoDB client if not already registered
        services.TryAddSingleton<IAmazonDynamoDB>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value;

            if (options.UseLocalEmulator)
            {
                var config = new AmazonDynamoDBConfig
                {
                    ServiceURL = options.LocalEmulatorUrl?.AbsoluteUri ?? "http://localhost:4566"
                };
                return new AmazonDynamoDBClient(config);
            }

            return new AmazonDynamoDBClient();
        });

        // Register the store implementation
        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value;

            if (options.UseLocalEmulator)
            {
                // Try DynamoDB first; if LocalStack is available, use DynamoDB store
                // For pure unit testing without LocalStack, use InMemory
                try
                {
                    var dynamoDb = sp.GetRequiredService<IAmazonDynamoDB>();
                    return new DynamoDbIdempotencyStore(
                        dynamoDb,
                        sp.GetRequiredService<IOptions<IdempotencyOptions>>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DynamoDbIdempotencyStore>>());
                }
                catch
                {
                    return new InMemoryIdempotencyStore();
                }
            }

            return new DynamoDbIdempotencyStore(
                sp.GetRequiredService<IAmazonDynamoDB>(),
                sp.GetRequiredService<IOptions<IdempotencyOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DynamoDbIdempotencyStore>>());
        });
    }
}
