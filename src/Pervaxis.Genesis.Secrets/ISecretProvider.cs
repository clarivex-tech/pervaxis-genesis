/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

namespace Pervaxis.Genesis.Secrets;

/// <summary>Reads secrets belonging to the current trusted tenant.</summary>
public interface ISecretProvider
{
    /// <summary>Reads a string secret, or null when it does not exist.</summary>
    Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
    /// <summary>Reads and deserializes a JSON secret.</summary>
    Task<T?> GetSecretAsync<T>(string secretName, CancellationToken cancellationToken = default);
    /// <summary>Reads a binary secret, or null when it does not exist.</summary>
    Task<byte[]?> GetBinarySecretAsync(string secretName, CancellationToken cancellationToken = default);
}

/// <summary>Explicit platform-only access for startup credentials. Register only in trusted hosts.</summary>
public interface IPlatformSecretProvider : ISecretProvider;
