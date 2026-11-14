/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

namespace Pervaxis.Genesis.Parameters;

/// <summary>Reads parameters belonging to the current trusted tenant.</summary>
public interface IParameterProvider
{
    /// <summary>Reads a string parameter, or null when it does not exist.</summary>
    Task<string?> GetParameterAsync(string parameterName, CancellationToken cancellationToken = default);
    /// <summary>Reads and deserializes a JSON parameter.</summary>
    Task<T?> GetParameterAsync<T>(string parameterName, CancellationToken cancellationToken = default);
    /// <summary>Reads parameters under a relative path, with relative names as dictionary keys.</summary>
    Task<IReadOnlyDictionary<string, string>> GetParametersByPathAsync(string path, bool recursive = true, CancellationToken cancellationToken = default);
}

/// <summary>Explicit platform-only access for startup configuration. Register only in trusted hosts.</summary>
public interface IPlatformParameterProvider : IParameterProvider;
