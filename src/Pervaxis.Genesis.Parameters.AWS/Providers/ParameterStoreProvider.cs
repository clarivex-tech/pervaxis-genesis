/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Text.Json;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.MultiTenancy;
using Pervaxis.Genesis.Parameters.AWS.Options;

namespace Pervaxis.Genesis.Parameters.AWS.Providers;

/// <summary>Reads tenant parameters from shared SSM using trusted tenant paths.</summary>
public sealed class ParameterStoreProvider : IParameterProvider
{
    private readonly ITenantContext? _tenant;
    private readonly ParameterReader _reader;

    /// <summary>Creates a scoped provider with container-owned shared client and cache.</summary>
    public ParameterStoreProvider(IAmazonSimpleSystemsManagement client, IOptions<ParameterStoreOptions> options, IMemoryCache cache, ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _tenant = tenantContext;
        _reader = new ParameterReader(client, options.Value, cache);
    }

    private string Root => $"/tenant/{TenantIsolation.RequireTenant(_tenant)}/";
    /// <inheritdoc />
    public Task<string?> GetParameterAsync(string parameterName, CancellationToken cancellationToken = default)
        => _reader.GetStringAsync(Root + SharedResourceName.Normalize(parameterName), cancellationToken);
    /// <inheritdoc />
    public Task<T?> GetParameterAsync<T>(string parameterName, CancellationToken cancellationToken = default)
        => _reader.GetJsonAsync<T>(Root + SharedResourceName.Normalize(parameterName), cancellationToken);
    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetParametersByPathAsync(string path, bool recursive = true, CancellationToken cancellationToken = default)
        => _reader.GetByPathAsync(Root, SharedResourceName.Normalize(path, isPath: true), recursive, cancellationToken);
}

internal sealed class PlatformParameterProvider(ParameterReader reader) : IPlatformParameterProvider
{
    public Task<string?> GetParameterAsync(string parameterName, CancellationToken cancellationToken = default)
        => reader.GetStringAsync("/platform/" + SharedResourceName.Normalize(parameterName), cancellationToken);
    public Task<T?> GetParameterAsync<T>(string parameterName, CancellationToken cancellationToken = default)
        => reader.GetJsonAsync<T>("/platform/" + SharedResourceName.Normalize(parameterName), cancellationToken);
    public Task<IReadOnlyDictionary<string, string>> GetParametersByPathAsync(string path, bool recursive = true, CancellationToken cancellationToken = default)
        => reader.GetByPathAsync("/platform/", SharedResourceName.Normalize(path, isPath: true), recursive, cancellationToken);
}

internal sealed class ParameterReader
{
    private readonly IAmazonSimpleSystemsManagement _client;
    private readonly ParameterStoreOptions _options;
    private readonly IMemoryCache _cache;
    private sealed record CacheKey(IAmazonSimpleSystemsManagement Client, string Name, bool Decrypt);

    internal ParameterReader(IAmazonSimpleSystemsManagement client, ParameterStoreOptions options, IMemoryCache cache)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cache);
        if (!options.Validate())
        {
            throw new ArgumentException("Invalid Parameter Store options. Tenant isolation must remain enabled.", nameof(options));
        }
        _client = client;
        _options = options;
        _cache = cache;
    }

    public async Task<string?> GetStringAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = new CacheKey(_client, name, _options.WithDecryption);
        if (_options.EnableCaching && _cache.TryGetValue<string>(key, out var cached))
        {
            return cached;
        }
        try
        {
            var response = await _client.GetParameterAsync(new GetParameterRequest { Name = name, WithDecryption = _options.WithDecryption }, ct);
            var value = response.Parameter?.Value;
            if (value is not null && _options.EnableCaching)
            {
                _cache.Set(key, value, TimeSpan.FromSeconds(_options.CacheTtlSeconds));
            }
            return value;
        }
        catch (ParameterNotFoundException)
        {
            return null;
        }
    }

    public async Task<T?> GetJsonAsync<T>(string name, CancellationToken ct)
    {
        var text = await GetStringAsync(name, ct);
        return text is null ? default : JsonSerializer.Deserialize<T>(text);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetByPathAsync(string root, string relative, bool recursive, CancellationToken ct)
    {
        var prefix = root + (relative.Length == 0 ? "" : relative + "/");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? nextToken = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var response = await _client.GetParametersByPathAsync(new GetParametersByPathRequest
            {
                Path = prefix, Recursive = recursive, WithDecryption = _options.WithDecryption,
                MaxResults = _options.MaxResults, NextToken = nextToken
            }, ct);
            foreach (var parameter in response.Parameters ?? [])
            {
                if (parameter.Name is null || !parameter.Name.StartsWith(prefix, StringComparison.Ordinal)
                    || parameter.Name.Length == prefix.Length)
                {
                    throw new GenesisException(nameof(ParameterStoreProvider), "Parameter response is outside the requested path.", "TENANT_MISMATCH", null);
                }
                if (parameter.Value is not null)
                {
                    values[parameter.Name[root.Length..]] = parameter.Value;
                }
            }
            nextToken = response.NextToken;
            if (!string.IsNullOrEmpty(nextToken) && !seenTokens.Add(nextToken))
            {
                throw new GenesisException(nameof(ParameterStoreProvider), "Repeated pagination token.", "INVALID_PAGINATION", null);
            }
        } while (!string.IsNullOrEmpty(nextToken));
        return values;
    }
}
