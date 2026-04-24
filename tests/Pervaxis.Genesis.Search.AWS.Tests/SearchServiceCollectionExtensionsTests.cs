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
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Search.AWS.Extensions;
using Pervaxis.Genesis.Search.AWS.Options;
using Pervaxis.Genesis.Search.AWS.Providers.OpenSearch;

namespace Pervaxis.Genesis.Search.AWS.Tests;

public class SearchServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisSearch_WithConfiguration_NullServices_ThrowsArgumentNullException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act & Assert
        var act = () => ((IServiceCollection)null!).AddGenesisSearch(configuration);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddGenesisSearch_WithConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddGenesisSearch((IConfiguration)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddGenesisSearch_WithConfiguration_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["DomainEndpoint"] = "https://test-domain.us-east-1.es.amazonaws.com"
            })
            .Build();

        // Act
        services.AddGenesisSearch(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetService<ISearch>().Should().NotBeNull();
        serviceProvider.GetService<ISearch>().Should().BeOfType<OpenSearchProvider>();
    }

    [Fact]
    public void AddGenesisSearch_WithConfiguration_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["DomainEndpoint"] = "https://test-domain.us-east-1.es.amazonaws.com"
            })
            .Build();

        // Act
        services.AddLogging();
        services.AddGenesisSearch(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var instance1 = serviceProvider.GetService<ISearch>();
        var instance2 = serviceProvider.GetService<ISearch>();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void AddGenesisSearch_WithAction_NullServices_ThrowsArgumentNullException()
    {
        // Arrange
        Action<SearchOptions> configureOptions = _ => { };

        // Act & Assert
        var act = () => ((IServiceCollection)null!).AddGenesisSearch(configureOptions);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddGenesisSearch_WithAction_NullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        var act = () => services.AddGenesisSearch((Action<SearchOptions>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configureOptions");
    }

    [Fact]
    public void AddGenesisSearch_WithAction_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddLogging();
        services.AddGenesisSearch(options =>
        {
            options.Region = "us-east-1";
            options.DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com";
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetService<ISearch>().Should().NotBeNull();
        serviceProvider.GetService<ISearch>().Should().BeOfType<OpenSearchProvider>();
    }

    [Fact]
    public void AddGenesisSearch_WithAction_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddLogging();
        services.AddGenesisSearch(options =>
        {
            options.Region = "us-west-2";
            options.DomainEndpoint = "https://custom-domain.us-west-2.es.amazonaws.com";
            options.IndexPrefix = "prod-";
            options.DefaultPageSize = 50;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var search = serviceProvider.GetService<ISearch>();
        search.Should().NotBeNull();
    }

    [Fact]
    public void AddGenesisSearch_WithAction_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddGenesisSearch(options =>
        {
            options.Region = "us-east-1";
            options.DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com";
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var instance1 = serviceProvider.GetService<ISearch>();
        var instance2 = serviceProvider.GetService<ISearch>();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void AddGenesisSearch_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["DomainEndpoint"] = "https://test-domain.us-east-1.es.amazonaws.com"
            })
            .Build();

        // Act
        var result = services.AddGenesisSearch(configuration);

        // Assert
        result.Should().BeSameAs(services);
    }
}
