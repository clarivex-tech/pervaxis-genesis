// -------------------------------------------------------------------------
// Copyright (c) 2026 Clarivex Technologies. All rights reserved.
// Pervaxis Platform - Genesis Edition
// -------------------------------------------------------------------------

namespace Pervaxis.Genesis.Base.Exceptions;

/// <summary>
/// Base exception for all Genesis provider errors.
/// </summary>
public class GenesisException : Exception
{
    /// <summary>
    /// Gets the provider name that threw the exception.
    /// </summary>
    public string? ProviderName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisException"/> class.
    /// </summary>
    public GenesisException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public GenesisException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public GenesisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisException"/> class with provider context.
    /// </summary>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="message">The error message.</param>
    public GenesisException(string providerName, string message)
        : base(message)
    {
        ProviderName = providerName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisException"/> class with provider context and inner exception.
    /// </summary>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public GenesisException(string providerName, string message, Exception innerException)
        : base(message, innerException)
    {
        ProviderName = providerName;
    }
}
