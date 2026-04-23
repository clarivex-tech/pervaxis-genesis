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
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Search.AWS.Options;

namespace Pervaxis.Genesis.Search.AWS.Tests;

public class SearchOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_ReturnsTrue()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            DefaultPageSize = 10,
            RequestTimeoutSeconds = 30,
            MaxRetries = 3
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyRegion_ThrowsArgumentException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com"
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_WithEmptyDomainEndpoint_ThrowsArgumentException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = ""
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validate_WithInvalidDomainEndpoint_ThrowsGenesisConfigurationException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "not-a-valid-url"
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage("*must be a valid absolute URL*");
    }

    [Fact]
    public void Validate_WithZeroDefaultPageSize_ThrowsGenesisConfigurationException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            DefaultPageSize = 0
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage("*must be greater than 0*");
    }

    [Fact]
    public void Validate_WithNegativeDefaultPageSize_ThrowsGenesisConfigurationException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            DefaultPageSize = -1
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage("*must be greater than 0*");
    }

    [Fact]
    public void Validate_WithZeroRequestTimeout_ThrowsGenesisConfigurationException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            RequestTimeoutSeconds = 0
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage("*must be greater than 0*");
    }

    [Fact]
    public void Validate_WithNegativeRequestTimeout_ThrowsGenesisConfigurationException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            RequestTimeoutSeconds = -1
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage("*must be greater than 0*");
    }

    [Fact]
    public void Validate_WithNegativeMaxRetries_ThrowsGenesisConfigurationException()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            MaxRetries = -1
        };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage("*must be greater than or equal to 0*");
    }

    [Fact]
    public void Validate_WithZeroMaxRetries_ReturnsTrue()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            MaxRetries = 0
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithIndexPrefix_ReturnsTrue()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            IndexPrefix = "prod-"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithBasicAuth_ReturnsTrue()
    {
        // Arrange
        var options = new SearchOptions
        {
            Region = "us-east-1",
            DomainEndpoint = "https://test-domain.us-east-1.es.amazonaws.com",
            Username = "admin",
            Password = "password123"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var options = new SearchOptions();

        // Assert
        options.DomainEndpoint.Should().BeEmpty();
        options.IndexPrefix.Should().BeEmpty();
        options.DefaultPageSize.Should().Be(10);
        options.RequestTimeoutSeconds.Should().Be(30);
        options.MaxRetries.Should().Be(3);
        options.EnableDebugMode.Should().BeFalse();
        options.Username.Should().BeNull();
        options.Password.Should().BeNull();
    }
}
