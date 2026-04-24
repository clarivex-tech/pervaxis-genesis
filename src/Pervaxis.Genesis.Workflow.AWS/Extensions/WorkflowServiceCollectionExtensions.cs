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
using Pervaxis.Genesis.Workflow.AWS.Options;
using Pervaxis.Genesis.Workflow.AWS.Providers;

namespace Pervaxis.Genesis.Workflow.AWS.Extensions;

/// <summary>
/// Extension methods for registering AWS Step Functions workflow services.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Adds AWS Step Functions workflow services to the service collection using configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing workflow settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisWorkflow(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WorkflowOptions>(configuration);
        services.AddSingleton<IWorkflow, StepFunctionsWorkflowProvider>();

        return services;
    }

    /// <summary>
    /// Adds AWS Step Functions workflow services to the service collection using an action delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure workflow options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGenesisWorkflow(
        this IServiceCollection services,
        Action<WorkflowOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);
        services.AddSingleton<IWorkflow, StepFunctionsWorkflowProvider>();

        return services;
    }
}
