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

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.AIAssistance.AWS.Options;

namespace Pervaxis.Genesis.AIAssistance.AWS.Providers;

/// <summary>
/// AWS Bedrock implementation of the IAIAssistant interface.
/// Supports Claude models for text generation, Titan for embeddings, and Stable Diffusion for images.
/// </summary>
public sealed class BedrockAIAssistantProvider : IAIAssistant, IDisposable
{
    private readonly ILogger<BedrockAIAssistantProvider> _logger;
    private readonly AIAssistanceOptions _options;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IAmazonBedrockRuntime> _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BedrockAIAssistantProvider"/> class.
    /// </summary>
    /// <param name="options">The AI assistance options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    /// <exception cref="ArgumentNullException">Thrown when options or logger is null.</exception>
    /// <exception cref="GenesisConfigurationException">Thrown when options validation fails.</exception>
    public BedrockAIAssistantProvider(
        IOptions<AIAssistanceOptions> options,
        ILogger<BedrockAIAssistantProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(nameof(AIAssistanceOptions), "Invalid AI assistance options configuration");
        }

        _client = new Lazy<IAmazonBedrockRuntime>(CreateClient);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _logger.LogInformation(
            "BedrockAIAssistantProvider initialized for region {Region} with text model {TextModel}, tenant isolation: {TenantIsolation}",
            _options.Region, _options.TextModelId,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true);
    }

    /// <summary>
    /// Internal constructor for testing with injected client.
    /// </summary>
    internal BedrockAIAssistantProvider(
        AIAssistanceOptions options,
        IAmazonBedrockRuntime client,
        ILogger<BedrockAIAssistantProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _tenantContext = tenantContext;
        _client = new Lazy<IAmazonBedrockRuntime>(() => client);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public async Task<string> GenerateTextAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        using var activity = PervaxisActivitySource.StartActivity("ai.generate_text", ActivityKind.Client);
        activity?.SetTag("ai.system", "bedrock");
        activity?.SetTag("ai.operation", "generate_text");
        activity?.SetTag("ai.model", _options.TextModelId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, nameof(prompt));

        try
        {
            // Determine if using Claude or Titan model
            var isClaude = _options.TextModelId.Contains("claude", StringComparison.OrdinalIgnoreCase);

            var requestBody = isClaude
                ? BuildClaudeRequest(prompt)
                : BuildTitanTextRequest(prompt);

            var request = new InvokeModelRequest
            {
                ModelId = _options.TextModelId,
                Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
                ContentType = "application/json",
                Accept = "application/json"
            };

            var response = await _client.Value.InvokeModelAsync(request, cancellationToken);

            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync(cancellationToken);

            var generatedText = isClaude
                ? ParseClaudeResponse(responseBody)
                : ParseTitanTextResponse(responseBody);

            activity?.SetTag("ai.response_length", generatedText.Length);
            _logger.LogInformation(
                "Generated text using {ModelId} (length: {Length} chars)",
                _options.TextModelId, generatedText.Length);

            return generatedText;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to generate text using {ModelId}", _options.TextModelId);
            throw new GenesisException(
                nameof(BedrockAIAssistantProvider),
                $"Failed to generate text: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        using var activity = PervaxisActivitySource.StartActivity("ai.generate_embedding", ActivityKind.Client);
        activity?.SetTag("ai.system", "bedrock");
        activity?.SetTag("ai.operation", "generate_embedding");
        activity?.SetTag("ai.model", _options.EmbeddingModelId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                inputText = text
            }, _jsonOptions);

            var request = new InvokeModelRequest
            {
                ModelId = _options.EmbeddingModelId,
                Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
                ContentType = "application/json",
                Accept = "application/json"
            };

            var response = await _client.Value.InvokeModelAsync(request, cancellationToken);

            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync(cancellationToken);

            var embedding = ParseTitanEmbeddingResponse(responseBody);

            activity?.SetTag("ai.embedding_dimensions", embedding.Length);
            _logger.LogInformation(
                "Generated embedding using {ModelId} (dimensions: {Dimensions})",
                _options.EmbeddingModelId, embedding.Length);

            return embedding;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to generate embedding using {ModelId}", _options.EmbeddingModelId);
            throw new GenesisException(
                nameof(BedrockAIAssistantProvider),
                $"Failed to generate embedding: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateImageAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        using var activity = PervaxisActivitySource.StartActivity("ai.generate_image", ActivityKind.Client);
        activity?.SetTag("ai.system", "bedrock");
        activity?.SetTag("ai.operation", "generate_image");
        activity?.SetTag("ai.model", _options.ImageModelId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(prompt, nameof(prompt));

        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                text_prompts = new[]
                {
                    new { text = prompt }
                },
                cfg_scale = 7,
                steps = 30,
                seed = 0
            }, _jsonOptions);

            var request = new InvokeModelRequest
            {
                ModelId = _options.ImageModelId,
                Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody)),
                ContentType = "application/json",
                Accept = "application/json"
            };

            var response = await _client.Value.InvokeModelAsync(request, cancellationToken);

            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync(cancellationToken);

            var imageData = ParseStableDiffusionResponse(responseBody);

            activity?.SetTag("ai.image_size", imageData.Length);
            _logger.LogInformation(
                "Generated image using {ModelId} (size: {Size} bytes)",
                _options.ImageModelId, imageData.Length);

            return imageData;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to generate image using {ModelId}", _options.ImageModelId);
            throw new GenesisException(
                nameof(BedrockAIAssistantProvider),
                $"Failed to generate image: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Builds the request body for Claude models.
    /// </summary>
    private string BuildClaudeRequest(string prompt)
    {
        var request = new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        return JsonSerializer.Serialize(request, _jsonOptions);
    }

    /// <summary>
    /// Builds the request body for Titan text models.
    /// </summary>
    private string BuildTitanTextRequest(string prompt)
    {
        var request = new
        {
            inputText = prompt,
            textGenerationConfig = new
            {
                maxTokenCount = _options.MaxTokens,
                temperature = _options.Temperature,
                topP = 0.9
            }
        };

        return JsonSerializer.Serialize(request, _jsonOptions);
    }

    /// <summary>
    /// Parses the response from Claude models.
    /// </summary>
    private static string ParseClaudeResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
        {
            var firstContent = content[0];
            if (firstContent.TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }
        }

        throw new GenesisException(nameof(BedrockAIAssistantProvider), "Invalid response format from Claude model");
    }

    /// <summary>
    /// Parses the response from Titan text models.
    /// </summary>
    private static string ParseTitanTextResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
        {
            var firstResult = results[0];
            if (firstResult.TryGetProperty("outputText", out var outputText))
            {
                return outputText.GetString() ?? string.Empty;
            }
        }

        throw new GenesisException(nameof(BedrockAIAssistantProvider), "Invalid response format from Titan model");
    }

    /// <summary>
    /// Parses the response from Titan embedding models.
    /// </summary>
    private static float[] ParseTitanEmbeddingResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("embedding", out var embedding))
        {
            var embeddingArray = new List<float>();
            foreach (var element in embedding.EnumerateArray())
            {
                embeddingArray.Add((float)element.GetDouble());
            }
            return embeddingArray.ToArray();
        }

        throw new GenesisException(nameof(BedrockAIAssistantProvider), "Invalid response format from Titan Embeddings model");
    }

    /// <summary>
    /// Parses the response from Stable Diffusion models.
    /// </summary>
    private static byte[] ParseStableDiffusionResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.GetArrayLength() > 0)
        {
            var firstArtifact = artifacts[0];
            if (firstArtifact.TryGetProperty("base64", out var base64))
            {
                var base64String = base64.GetString();
                if (!string.IsNullOrWhiteSpace(base64String))
                {
                    return Convert.FromBase64String(base64String);
                }
            }
        }

        throw new GenesisException(nameof(BedrockAIAssistantProvider), "Invalid response format from Stable Diffusion model");
    }

    /// <summary>
    /// Creates the Bedrock Runtime client with appropriate configuration.
    /// </summary>
    private IAmazonBedrockRuntime CreateClient()
    {
        var config = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region),
            MaxErrorRetry = _options.MaxRetries,
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)
        };

        if (_options.UseLocalEmulator && _options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = _options.LocalEmulatorUrl.AbsoluteUri;
            config.AuthenticationRegion = _options.Region;
        }

        return new AmazonBedrockRuntimeClient(config);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }

        _disposed = true;
        _logger.LogInformation("BedrockAIAssistantProvider disposed");
    }

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }
}
