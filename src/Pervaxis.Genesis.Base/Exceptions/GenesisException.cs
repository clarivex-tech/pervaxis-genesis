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
