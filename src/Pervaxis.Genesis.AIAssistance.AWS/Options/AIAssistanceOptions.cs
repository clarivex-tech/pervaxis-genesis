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

using Pervaxis.Core.Abstractions.Genesis;

namespace Pervaxis.Genesis.AIAssistance.AWS.Options;

/// <summary>
/// Configuration options for AWS Bedrock AI assistance provider.
/// </summary>
public sealed class AIAssistanceOptions : GenesisOptionsBase
{
    /// <summary>
    /// Model ID for text generation (e.g., "anthropic.claude-3-5-sonnet-20241022-v2:0").
    /// Default: Claude 3.5 Sonnet.
    /// </summary>
    public string TextModelId { get; set; } = "anthropic.claude-3-5-sonnet-20241022-v2:0";

    /// <summary>
    /// Model ID for embeddings generation (e.g., "amazon.titan-embed-text-v1").
    /// Default: Titan Embeddings G1.
    /// </summary>
    public string EmbeddingModelId { get; set; } = "amazon.titan-embed-text-v1";

    /// <summary>
    /// Model ID for image generation (e.g., "stability.stable-diffusion-xl-v1").
    /// Default: Stable Diffusion XL.
    /// </summary>
    public string ImageModelId { get; set; } = "stability.stable-diffusion-xl-v1";

    /// <summary>
    /// Temperature for text generation (0.0 to 1.0).
    /// Higher values make output more random, lower values more deterministic.
    /// Default: 0.7.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Maximum tokens to generate for text completion.
    /// Default: 1024.
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Maximum number of retries for transient failures.
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Request timeout in seconds.
    /// Default: 60 (Bedrock can take longer for image generation).
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets a value indicating whether to enable tenant isolation.
    /// When enabled, AI requests include tenant metadata for tracking.
    /// Default is true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Validates the AI assistance options.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(TextModelId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(EmbeddingModelId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ImageModelId))
        {
            return false;
        }

        if (Temperature is < 0 or > 1)
        {
            return false;
        }

        if (MaxTokens <= 0)
        {
            return false;
        }

        if (MaxRetries < 0)
        {
            return false;
        }

        if (RequestTimeoutSeconds <= 0)
        {
            return false;
        }

        return true;
    }
}
