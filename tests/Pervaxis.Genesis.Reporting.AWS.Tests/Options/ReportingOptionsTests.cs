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
using FluentAssertions;
using Pervaxis.Genesis.Reporting.AWS.Options;

namespace Pervaxis.Genesis.Reporting.AWS.Tests.Options;

public class ReportingOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_ShouldReturnTrue()
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345",
            DatabaseId = 1,
            RequestTimeoutSeconds = 30,
            MaxRetries = 3
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Test method validates string input")]
    public void Validate_WithInvalidBaseUrl_ShouldReturnFalse(string? baseUrl)
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = baseUrl!,
            ApiKey = "mb_test_api_key_12345"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://invalid")]
    [InlineData("://missing-scheme")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Test method validates string input")]
    public void Validate_WithMalformedBaseUrl_ShouldReturnFalse(string baseUrl)
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = baseUrl,
            ApiKey = "mb_test_api_key_12345"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_WithInvalidApiKey_ShouldReturnFalse(string? apiKey)
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = apiKey!
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void Validate_WithInvalidRequestTimeout_ShouldReturnFalse(int timeout)
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345",
            RequestTimeoutSeconds = timeout
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Validate_WithInvalidMaxRetries_ShouldReturnFalse(int maxRetries)
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345",
            MaxRetries = maxRetries
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithZeroMaxRetries_ShouldReturnTrue()
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345",
            MaxRetries = 0
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithNullDatabaseId_ShouldReturnTrue()
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345",
            DatabaseId = null
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_ShouldBeValid()
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = "https://metabase.example.com",
            ApiKey = "mb_test_api_key_12345"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
        options.RequestTimeoutSeconds.Should().Be(30);
        options.MaxRetries.Should().Be(3);
        options.DatabaseId.Should().BeNull();
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://metabase.company.com")]
    [InlineData("https://analytics.example.org:8080")]
    [SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "Test method validates string input")]
    public void Validate_WithVariousValidUrls_ShouldReturnTrue(string baseUrl)
    {
        // Arrange
        var options = new ReportingOptions
        {
            Region = "us-east-1",
            BaseUrl = baseUrl,
            ApiKey = "mb_test_api_key_12345"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }
}
