/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.MultiTenancy;
using Pervaxis.Genesis.Secrets.AWS.Options;

namespace Pervaxis.Genesis.Secrets.AWS.Providers;

/// <summary>Reads tenant secrets from a shared Secrets Manager account using trusted prefixes.</summary>
public sealed class SecretsManagerProvider : ISecretProvider
{
    private readonly ITenantContext? _tenant;
    private readonly SecretReader _reader;

    /// <summary>Creates a scoped provider with container-owned shared client and cache.</summary>
    public SecretsManagerProvider(IAmazonSecretsManager client, IOptions<SecretsOptions> options, IMemoryCache cache, ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _tenant = tenantContext;
        _reader = new SecretReader(client, options.Value, cache);
    }

    private string Resolve(string name) => $"tenant-{TenantIsolation.RequireTenant(_tenant)}/{SharedResourceName.Normalize(name)}";
    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        => _reader.GetStringAsync(Resolve(secretName), cancellationToken);
    /// <inheritdoc />
    public Task<T?> GetSecretAsync<T>(string secretName, CancellationToken cancellationToken = default)
        => _reader.GetJsonAsync<T>(Resolve(secretName), cancellationToken);
    /// <inheritdoc />
    public Task<byte[]?> GetBinarySecretAsync(string secretName, CancellationToken cancellationToken = default)
        => _reader.GetBinaryAsync(Resolve(secretName), cancellationToken);
}

internal sealed class PlatformSecretProvider(SecretReader reader) : IPlatformSecretProvider
{
    private static string Resolve(string name) => "platform/" + SharedResourceName.Normalize(name);
    public Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        => reader.GetStringAsync(Resolve(secretName), cancellationToken);
    public Task<T?> GetSecretAsync<T>(string secretName, CancellationToken cancellationToken = default)
        => reader.GetJsonAsync<T>(Resolve(secretName), cancellationToken);
    public Task<byte[]?> GetBinarySecretAsync(string secretName, CancellationToken cancellationToken = default)
        => reader.GetBinaryAsync(Resolve(secretName), cancellationToken);
}

internal sealed class SecretReader
{
    private readonly IAmazonSecretsManager _client;
    private readonly SecretsOptions _options;
    private readonly IMemoryCache _cache;
    private sealed record CacheKey(IAmazonSecretsManager Client, string Name);
    private sealed record SecretValue(string? Text, byte[]? Binary);

    internal SecretReader(IAmazonSecretsManager client, SecretsOptions options, IMemoryCache cache)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        if (!options.Validate())
        {
            throw new ArgumentException("Invalid Secrets options. Tenant isolation must remain enabled.", nameof(options));
        }
        _client = client;
        _options = options;
        _cache = cache;
    }

    public async Task<string?> GetStringAsync(string name, CancellationToken ct)
        => (await ReadAsync(name, ct))?.Text;

    public async Task<T?> GetJsonAsync<T>(string name, CancellationToken ct)
    {
        var text = await GetStringAsync(name, ct);
        return text is null ? default : JsonSerializer.Deserialize<T>(text);
    }

    public async Task<byte[]?> GetBinaryAsync(string name, CancellationToken ct)
        => (await ReadAsync(name, ct))?.Binary?.ToArray();

    private async Task<SecretValue?> ReadAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = new CacheKey(_client, name);
        if (_options.EnableCaching && _cache.TryGetValue<SecretValue>(key, out var cached))
        {
            return cached;
        }
        try
        {
            var response = await _client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = name }, ct);
            using var binary = response.SecretBinary;
            var value = new SecretValue(response.SecretString, binary?.ToArray());
            if (_options.EnableCaching)
            {
                _cache.Set(key, value, TimeSpan.FromSeconds(_options.CacheTtlSeconds));
            }
            return value;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }
}
