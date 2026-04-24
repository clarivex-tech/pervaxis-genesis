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

using System.Text;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Genesis.AIAssistance.AWS.Options;
using Pervaxis.Genesis.AIAssistance.AWS.Providers;
using Pervaxis.Genesis.Base.Exceptions;

namespace Pervaxis.Genesis.AIAssistance.AWS.Tests.Providers;

public class BedrockAIAssistantProviderTests
{
    private readonly Mock<IAmazonBedrockRuntime> _mockClient;
    private readonly Mock<ILogger<BedrockAIAssistantProvider>> _mockLogger;
    private readonly AIAssistanceOptions _options;

    public BedrockAIAssistantProviderTests()
    {
        _mockClient = new Mock<IAmazonBedrockRuntime>();
        _mockLogger = new Mock<ILogger<BedrockAIAssistantProvider>>();
        _options = new AIAssistanceOptions
        {
            Region = "us-east-1",
            TextModelId = "anthropic.claude-3-5-sonnet-20241022-v2:0",
            EmbeddingModelId = "amazon.titan-embed-text-v1",
            ImageModelId = "stability.stable-diffusion-xl-v1",
            Temperature = 0.7,
            MaxTokens = 1024
        };
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Act
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new BedrockAIAssistantProvider(null!, _mockClient.Object, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_WithNullClient_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new BedrockAIAssistantProvider(_options, null!, _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("client");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new BedrockAIAssistantProvider(_options, _mockClient.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task GenerateTextAsync_WithClaudeModel_ShouldReturnGeneratedText()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var expectedText = "This is a generated response from Claude.";
        var responseJson = $$"""
        {
            "content": [
                {
                    "text": "{{expectedText}}"
                }
            ]
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await provider.GenerateTextAsync("Test prompt", CancellationToken.None);

        // Assert
        result.Should().Be(expectedText);
        _mockClient.Verify(c => c.InvokeModelAsync(
            It.Is<InvokeModelRequest>(r => r.ModelId == _options.TextModelId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateTextAsync_WithTitanModel_ShouldReturnGeneratedText()
    {
        // Arrange
        _options.TextModelId = "amazon.titan-text-express-v1";
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var expectedText = "This is a generated response from Titan.";
        var responseJson = $$"""
        {
            "results": [
                {
                    "outputText": "{{expectedText}}"
                }
            ]
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await provider.GenerateTextAsync("Test prompt", CancellationToken.None);

        // Assert
        result.Should().Be(expectedText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GenerateTextAsync_WithInvalidPrompt_ShouldThrowArgumentException(string? prompt)
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        // Act
        var act = async () => await provider.GenerateTextAsync(prompt!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateTextAsync_WhenBedrockFails_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonBedrockRuntimeException("Bedrock service error"));

        // Act
        var act = async () => await provider.GenerateTextAsync("Test prompt", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to generate text*");
    }

    [Fact]
    public async Task GenerateTextAsync_WithInvalidClaudeResponse_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var responseJson = """
        {
            "invalid": "response"
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var act = async () => await provider.GenerateTextAsync("Test prompt", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Invalid response format from Claude model*");
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_ShouldReturnEmbedding()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var responseJson = $$"""
        {
            "embedding": [{{string.Join(",", expectedEmbedding)}}]
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await provider.GenerateEmbeddingAsync("Test text", CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedEmbedding);
        _mockClient.Verify(c => c.InvokeModelAsync(
            It.Is<InvokeModelRequest>(r => r.ModelId == _options.EmbeddingModelId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GenerateEmbeddingAsync_WithInvalidText_ShouldThrowArgumentException(string? text)
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        // Act
        var act = async () => await provider.GenerateEmbeddingAsync(text!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenBedrockFails_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonBedrockRuntimeException("Bedrock service error"));

        // Act
        var act = async () => await provider.GenerateEmbeddingAsync("Test text", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to generate embedding*");
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithInvalidResponse_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var responseJson = """
        {
            "invalid": "response"
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var act = async () => await provider.GenerateEmbeddingAsync("Test text", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Invalid response format from Titan Embeddings model*");
    }

    [Fact]
    public async Task GenerateImageAsync_WithValidPrompt_ShouldReturnImageData()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var expectedImageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var base64Image = Convert.ToBase64String(expectedImageData);
        var responseJson = $$"""
        {
            "artifacts": [
                {
                    "base64": "{{base64Image}}"
                }
            ]
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await provider.GenerateImageAsync("A beautiful sunset", CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expectedImageData);
        _mockClient.Verify(c => c.InvokeModelAsync(
            It.Is<InvokeModelRequest>(r => r.ModelId == _options.ImageModelId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GenerateImageAsync_WithInvalidPrompt_ShouldThrowArgumentException(string? prompt)
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        // Act
        var act = async () => await provider.GenerateImageAsync(prompt!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenBedrockFails_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonBedrockRuntimeException("Bedrock service error"));

        // Act
        var act = async () => await provider.GenerateImageAsync("Test prompt", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to generate image*");
    }

    [Fact]
    public async Task GenerateImageAsync_WithInvalidResponse_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);
        var responseJson = """
        {
            "invalid": "response"
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var act = async () => await provider.GenerateImageAsync("Test prompt", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Invalid response format from Stable Diffusion model*");
    }

    [Fact]
    public void Dispose_ShouldDisposeClient()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        // Act
        provider.Dispose();

        // Assert
        _mockClient.Verify(c => c.Dispose(), Times.Never); // Lazy not created yet
    }

    [Fact]
    public async Task Dispose_AfterClientCreation_ShouldDisposeClient()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        var responseJson = """
        {
            "content": [
                {
                    "text": "test"
                }
            ]
        }
        """;

        var response = new InvokeModelResponse
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(responseJson))
        };

        _mockClient.Setup(c => c.InvokeModelAsync(
            It.IsAny<InvokeModelRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        await provider.GenerateTextAsync("test", CancellationToken.None);

        // Act
        provider.Dispose();

        // Assert
        _mockClient.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldOnlyDisposeOnce()
    {
        // Arrange
        var provider = new BedrockAIAssistantProvider(_options, _mockClient.Object, _mockLogger.Object);

        // Act
        provider.Dispose();
        provider.Dispose();
        provider.Dispose();

        // Assert - Should not throw
        _mockClient.Verify(c => c.Dispose(), Times.Never); // Lazy not created
    }
}
