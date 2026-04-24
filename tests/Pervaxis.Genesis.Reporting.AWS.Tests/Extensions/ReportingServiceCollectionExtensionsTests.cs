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
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Reporting.AWS.Extensions;
using Pervaxis.Genesis.Reporting.AWS.Options;

namespace Pervaxis.Genesis.Reporting.AWS.Tests.Extensions;

public class ReportingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisReporting_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["BaseUrl"] = "https://metabase.example.com",
                ["ApiKey"] = "mb_test_api_key_12345",
                ["DatabaseId"] = "1",
                ["RequestTimeoutSeconds"] = "45",
                ["MaxRetries"] = "5"
            })
            .Build();

        // Act
        services.AddGenesisReporting(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var reporting = serviceProvider.GetService<IReporting>();
        reporting.Should().NotBeNull();

        var options = serviceProvider.GetService<IOptions<ReportingOptions>>();
        options.Should().NotBeNull();
        options!.Value.Region.Should().Be("us-east-1");
        options.Value.BaseUrl.Should().Be("https://metabase.example.com");
        options.Value.ApiKey.Should().Be("mb_test_api_key_12345");
        options.Value.DatabaseId.Should().Be(1);
        options.Value.RequestTimeoutSeconds.Should().Be(45);
        options.Value.MaxRetries.Should().Be(5);
    }

    [Fact]
    public void AddGenesisReporting_WithAction_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddGenesisReporting(options =>
        {
            options.Region = "eu-west-1";
            options.BaseUrl = "https://analytics.company.com";
            options.ApiKey = "mb_production_key";
            options.DatabaseId = 42;
            options.RequestTimeoutSeconds = 60;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var reporting = serviceProvider.GetService<IReporting>();
        reporting.Should().NotBeNull();

        var options = serviceProvider.GetService<IOptions<ReportingOptions>>();
        options!.Value.Region.Should().Be("eu-west-1");
        options.Value.BaseUrl.Should().Be("https://analytics.company.com");
        options.Value.ApiKey.Should().Be("mb_production_key");
        options.Value.DatabaseId.Should().Be(42);
        options.Value.RequestTimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void AddGenesisReporting_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        ServiceCollection? services = null;
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services!.AddGenesisReporting(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddGenesisReporting_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act
        var act = () => services.AddGenesisReporting(configuration!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddGenesisReporting_WithNullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<ReportingOptions>? configureOptions = null;

        // Act
        var act = () => services.AddGenesisReporting(configureOptions!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddGenesisReporting_WithMethodChaining_ShouldReturnServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["BaseUrl"] = "https://metabase.example.com",
                ["ApiKey"] = "mb_test_api_key_12345"
            })
            .Build();

        // Act
        var result = services.AddGenesisReporting(configuration);

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddGenesisReporting_WithDefaultOptions_ShouldUseDefaults()
    {
        // Arrange
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "ap-south-1",
                ["BaseUrl"] = "https://metabase.local",
                ["ApiKey"] = "mb_local_key"
            })
            .Build();

        // Act
        services.AddGenesisReporting(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<ReportingOptions>>();
        options!.Value.RequestTimeoutSeconds.Should().Be(30);
        options.Value.MaxRetries.Should().Be(3);
        options.Value.DatabaseId.Should().BeNull();
    }

    [Fact]
    public void AddGenesisReporting_ShouldRegisterHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["BaseUrl"] = "https://metabase.example.com",
                ["ApiKey"] = "mb_test_api_key_12345"
            })
            .Build();

        // Act
        services.AddGenesisReporting(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }
}
