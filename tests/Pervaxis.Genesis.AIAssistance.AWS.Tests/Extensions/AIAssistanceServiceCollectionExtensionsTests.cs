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
using Pervaxis.Genesis.AIAssistance.AWS.Extensions;
using Pervaxis.Genesis.AIAssistance.AWS.Options;
using Pervaxis.Genesis.AIAssistance.AWS.Providers;

namespace Pervaxis.Genesis.AIAssistance.AWS.Tests.Extensions;

public class AIAssistanceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisAIAssistance_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["TextModelId"] = "anthropic.claude-3-5-sonnet-20241022-v2:0",
                ["EmbeddingModelId"] = "amazon.titan-embed-text-v1",
                ["ImageModelId"] = "stability.stable-diffusion-xl-v1",
                ["Temperature"] = "0.8",
                ["MaxTokens"] = "2048"
            })
            .Build();

        // Act
        services.AddGenesisAIAssistance(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var aiAssistant = serviceProvider.GetService<IAIAssistant>();
        aiAssistant.Should().NotBeNull();
        aiAssistant.Should().BeOfType<BedrockAIAssistantProvider>();

        var options = serviceProvider.GetService<IOptions<AIAssistanceOptions>>();
        options.Should().NotBeNull();
        options!.Value.Region.Should().Be("us-east-1");
        options.Value.TextModelId.Should().Be("anthropic.claude-3-5-sonnet-20241022-v2:0");
        options.Value.Temperature.Should().Be(0.8);
        options.Value.MaxTokens.Should().Be(2048);
    }

    [Fact]
    public void AddGenesisAIAssistance_WithAction_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddGenesisAIAssistance(options =>
        {
            options.Region = "eu-west-1";
            options.TextModelId = "anthropic.claude-3-opus-20240229";
            options.Temperature = 0.5;
            options.MaxTokens = 512;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var aiAssistant = serviceProvider.GetService<IAIAssistant>();
        aiAssistant.Should().NotBeNull();

        var options = serviceProvider.GetService<IOptions<AIAssistanceOptions>>();
        options!.Value.Region.Should().Be("eu-west-1");
        options.Value.TextModelId.Should().Be("anthropic.claude-3-opus-20240229");
        options.Value.Temperature.Should().Be(0.5);
        options.Value.MaxTokens.Should().Be(512);
    }

    [Fact]
    public void AddGenesisAIAssistance_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        ServiceCollection? services = null;
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services!.AddGenesisAIAssistance(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddGenesisAIAssistance_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act
        var act = () => services.AddGenesisAIAssistance(configuration!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddGenesisAIAssistance_WithNullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<AIAssistanceOptions>? configureOptions = null;

        // Act
        var act = () => services.AddGenesisAIAssistance(configureOptions!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddGenesisAIAssistance_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1"
            })
            .Build();

        // Act
        services.AddGenesisAIAssistance(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAIAssistant));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddGenesisAIAssistance_WithMethodChaining_ShouldReturnServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1"
            })
            .Build();

        // Act
        var result = services.AddGenesisAIAssistance(configuration);

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddGenesisAIAssistance_WithDefaultOptions_ShouldUseDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "ap-south-1"
            })
            .Build();

        // Act
        services.AddGenesisAIAssistance(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<AIAssistanceOptions>>();
        options!.Value.TextModelId.Should().Be("anthropic.claude-3-5-sonnet-20241022-v2:0");
        options.Value.EmbeddingModelId.Should().Be("amazon.titan-embed-text-v1");
        options.Value.ImageModelId.Should().Be("stability.stable-diffusion-xl-v1");
        options.Value.Temperature.Should().Be(0.7);
        options.Value.MaxTokens.Should().Be(1024);
        options.Value.MaxRetries.Should().Be(3);
        options.Value.RequestTimeoutSeconds.Should().Be(60);
    }
}
