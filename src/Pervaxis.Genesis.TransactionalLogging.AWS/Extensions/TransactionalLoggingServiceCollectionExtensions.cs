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

using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Interceptors;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Sanitization;
using Pervaxis.Genesis.TransactionalLogging.AWS.Services;
using Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Extensions;

/// <summary>
/// Extension methods for registering Genesis Transactional Logging services.
/// </summary>
public static class TransactionalLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Genesis Transactional Logging services using configuration binding.
    /// Binds options from the "Genesis:TransactionalLogging" configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisTransactionalLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TransactionalLoggingOptions>(
            configuration.GetSection("Genesis:TransactionalLogging"));

        RegisterCoreServices(services);
        return services;
    }

    /// <summary>
    /// Adds Genesis Transactional Logging services with action-based configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">The options configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisTransactionalLogging(
        this IServiceCollection services,
        Action<TransactionalLoggingOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        RegisterCoreServices(services);
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        // Transaction context accessor (AsyncLocal-based, scoped)
        services.TryAddScoped<TransactionContextAccessor>();

        // ITransactionLog implementation (scoped — one per request)
        services.TryAddScoped<ITransactionLog, TransactionLogService>();

        // AWS SDK clients
        services.TryAddSingleton<IAmazonDynamoDB, AmazonDynamoDBClient>();
        services.TryAddSingleton<IAmazonS3, AmazonS3Client>();

        // Storage
        services.TryAddSingleton<S3OverflowStore>();
        services.TryAddSingleton<ITransactionLogStore, DynamoDbTransactionLogStore>();

        // Sanitizer
        services.TryAddSingleton<ParameterSanitizer>();

        // Provider interceptors via Scrutor TryDecorate — safe no-op if provider not registered
        services.TryDecorate<ICache, CacheTransactionInterceptor>();
        services.TryDecorate<IMessaging, MessagingTransactionInterceptor>();
        services.TryDecorate<IFileStorage, FileStorageTransactionInterceptor>();
        services.TryDecorate<ISearch, SearchTransactionInterceptor>();
        services.TryDecorate<INotification, NotificationsTransactionInterceptor>();
        services.TryDecorate<IWorkflow, WorkflowTransactionInterceptor>();
        services.TryDecorate<IAIAssistant, AIAssistantTransactionInterceptor>();
        services.TryDecorate<IReporting, ReportingTransactionInterceptor>();
    }
}
