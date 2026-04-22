// -------------------------------------------------------------------------
// Copyright (c) 2026 Clarivex Technologies. All rights reserved.
// Pervaxis Platform - Genesis Edition
// -------------------------------------------------------------------------

namespace Pervaxis.Genesis.Base.Options;

/// <summary>
/// Base configuration options for all Genesis providers.
/// </summary>
public abstract class GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the AWS region for the provider.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Gets or sets a value indicating whether to enable detailed logging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>
    /// Gets or sets the timeout in seconds for provider operations.
    /// Default is 30 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed operations.
    /// Default is 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether to use LocalStack for local development.
    /// </summary>
    public bool UseLocalStack { get; set; }

    /// <summary>
    /// Gets or sets the LocalStack service URL (e.g., http://localhost:4566).
    /// Only used when UseLocalStack is true.
    /// </summary>
    public string? LocalStackUrl { get; set; }

    /// <summary>
    /// Validates the options configuration.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public virtual bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Region))
        {
            return false;
        }

        if (TimeoutSeconds <= 0)
        {
            return false;
        }

        if (MaxRetryAttempts < 0)
        {
            return false;
        }

        if (UseLocalStack && string.IsNullOrWhiteSpace(LocalStackUrl))
        {
            return false;
        }

        return true;
    }
}
