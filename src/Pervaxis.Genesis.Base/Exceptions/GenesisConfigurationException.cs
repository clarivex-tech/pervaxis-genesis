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
/// Exception thrown when Genesis provider configuration is invalid.
/// </summary>
public sealed class GenesisConfigurationException : GenesisException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public GenesisConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisConfigurationException"/> class with inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public GenesisConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisConfigurationException"/> class with provider context.
    /// </summary>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="message">The error message.</param>
    public GenesisConfigurationException(string providerName, string message)
        : base(providerName, message)
    {
    }
}
