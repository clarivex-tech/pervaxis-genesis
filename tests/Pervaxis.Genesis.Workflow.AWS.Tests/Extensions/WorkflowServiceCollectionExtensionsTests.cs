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

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Workflow.AWS.Extensions;
using Pervaxis.Genesis.Workflow.AWS.Options;
using Pervaxis.Genesis.Workflow.AWS.Providers;

namespace Pervaxis.Genesis.Workflow.AWS.Tests.Extensions;

public class WorkflowServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisWorkflow_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["StateMachineArns:OrderProcessing"] = "arn:aws:states:us-east-1:123456789012:stateMachine:OrderWorkflow",
                ["MaxRetries"] = "5",
                ["RequestTimeoutSeconds"] = "60"
            })
            .Build();

        // Act
        services.AddGenesisWorkflow(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var workflow = serviceProvider.GetService<IWorkflow>();
        workflow.Should().NotBeNull();
        workflow.Should().BeOfType<StepFunctionsWorkflowProvider>();

        var options = serviceProvider.GetService<IOptions<WorkflowOptions>>();
        options.Should().NotBeNull();
        options!.Value.Region.Should().Be("us-east-1");
        options.Value.MaxRetries.Should().Be(5);
        options.Value.StateMachineArns.Should().ContainKey("OrderProcessing");
    }

    [Fact]
    public void AddGenesisWorkflow_WithAction_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddGenesisWorkflow(options =>
        {
            options.Region = "eu-west-1";
            options.StateMachineArns.Add("TestWorkflow", "arn:aws:states:eu-west-1:123456789012:stateMachine:TestWorkflow");
            options.MaxRetries = 2;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var workflow = serviceProvider.GetService<IWorkflow>();
        workflow.Should().NotBeNull();

        var options = serviceProvider.GetService<IOptions<WorkflowOptions>>();
        options!.Value.Region.Should().Be("eu-west-1");
        options.Value.StateMachineArns.Should().ContainKey("TestWorkflow");
    }

    [Fact]
    public void AddGenesisWorkflow_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        ServiceCollection? services = null;
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services!.AddGenesisWorkflow(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddGenesisWorkflow_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act
        var act = () => services.AddGenesisWorkflow(configuration!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddGenesisWorkflow_WithNullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<WorkflowOptions>? configureOptions = null;

        // Act
        var act = () => services.AddGenesisWorkflow(configureOptions!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddGenesisWorkflow_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["StateMachineArns:Test"] = "arn:aws:states:us-east-1:123456789012:stateMachine:Test"
            })
            .Build();

        // Act
        services.AddGenesisWorkflow(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IWorkflow));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddGenesisWorkflow_WithMethodChaining_ShouldReturnServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["StateMachineArns:Test"] = "arn:aws:states:us-east-1:123456789012:stateMachine:Test"
            })
            .Build();

        // Act
        var result = services.AddGenesisWorkflow(configuration);

        // Assert
        result.Should().BeSameAs(services);
    }
}
