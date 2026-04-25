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
using Pervaxis.Genesis.AIAssistance.AWS.Options;

namespace Pervaxis.Genesis.AIAssistance.AWS.Tests.Options;

public class AIAssistanceOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_ShouldReturnTrue()
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            TextModelId = "anthropic.claude-3-5-sonnet-20241022-v2:0",
            EmbeddingModelId = "amazon.titan-embed-text-v1",
            ImageModelId = "stability.stable-diffusion-xl-v1",
            Temperature = 0.7,
            MaxTokens = 1024,
            MaxRetries = 3,
            RequestTimeoutSeconds = 60
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
    public void Validate_WithInvalidTextModelId_ShouldReturnFalse(string? modelId)
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            TextModelId = modelId!,
            EmbeddingModelId = "amazon.titan-embed-text-v1",
            ImageModelId = "stability.stable-diffusion-xl-v1"
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
    public void Validate_WithInvalidEmbeddingModelId_ShouldReturnFalse(string? modelId)
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            TextModelId = "anthropic.claude-3-5-sonnet-20241022-v2:0",
            EmbeddingModelId = modelId!,
            ImageModelId = "stability.stable-diffusion-xl-v1"
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
    public void Validate_WithInvalidImageModelId_ShouldReturnFalse(string? modelId)
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            TextModelId = "anthropic.claude-3-5-sonnet-20241022-v2:0",
            EmbeddingModelId = "amazon.titan-embed-text-v1",
            ImageModelId = modelId!
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2.0)]
    public void Validate_WithInvalidTemperature_ShouldReturnFalse(double temperature)
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            Temperature = temperature
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithInvalidMaxTokens_ShouldReturnFalse(int maxTokens)
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            MaxTokens = maxTokens
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
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            MaxRetries = maxRetries
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
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            RequestTimeoutSeconds = timeout
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_WithValidTemperatureRange_ShouldReturnTrue(double temperature)
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            Temperature = temperature
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithZeroMaxRetries_ShouldReturnTrue()
    {
        // Arrange
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            MaxRetries = 0
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
        var options = new AIAssistanceOptions
        {
            Region = "us-east-1"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
        options.TextModelId.Should().Be("anthropic.claude-3-5-sonnet-20241022-v2:0");
        options.EmbeddingModelId.Should().Be("amazon.titan-embed-text-v1");
        options.ImageModelId.Should().Be("stability.stable-diffusion-xl-v1");
        options.Temperature.Should().Be(0.7);
        options.MaxTokens.Should().Be(1024);
        options.MaxRetries.Should().Be(3);
        options.RequestTimeoutSeconds.Should().Be(60);
    }
}
