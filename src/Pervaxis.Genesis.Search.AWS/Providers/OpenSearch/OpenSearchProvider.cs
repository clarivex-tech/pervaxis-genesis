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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Search.AWS.Options;

namespace Pervaxis.Genesis.Search.AWS.Providers.OpenSearch;

/// <summary>
/// AWS OpenSearch implementation of the <see cref="ISearch"/> interface.
/// </summary>
public sealed class OpenSearchProvider : ISearch, IDisposable
{
    private readonly SearchOptions _options;
    private readonly ILogger<OpenSearchProvider> _logger;
    private readonly Lazy<IOpenSearchClient> _client;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSearchProvider"/> class.
    /// </summary>
    public OpenSearchProvider(
        IOptions<SearchOptions> options,
        ILogger<OpenSearchProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        _options.Validate();

        _client = new Lazy<IOpenSearchClient>(() =>
        {
            var uri = new Uri(_options.DomainEndpoint);
            var connectionSettings = new ConnectionSettings(uri)
                .RequestTimeout(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds))
                .MaximumRetries(_options.MaxRetries);

            if (_options.EnableDebugMode)
            {
                connectionSettings.EnableDebugMode();
            }

            if (!string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.Password))
            {
                connectionSettings.BasicAuthentication(_options.Username, _options.Password);
            }

            return new OpenSearchClient(connectionSettings);
        });

        _logger.LogInformation(
            "OpenSearchProvider initialized for domain {DomainEndpoint}",
            _options.DomainEndpoint);
    }

    /// <summary>
    /// Internal constructor for testing with injected client.
    /// </summary>
    internal OpenSearchProvider(
        IOptions<SearchOptions> options,
        ILogger<OpenSearchProvider> logger,
        IOpenSearchClient client)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(client);

        _options = options.Value;
        _logger = logger;
        _client = new Lazy<IOpenSearchClient>(() => client);

        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<bool> IndexAsync<T>(
        string index,
        string id,
        T document,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Indexing document {Id} to index {Index}",
                id,
                fullIndex);

            var response = await _client.Value.IndexAsync(document, idx => idx
                .Index(fullIndex)
                .Id(id), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError(
                    "Failed to index document {Id}: {Error}",
                    id,
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Failed to index document: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Failed to index document: {response.ServerError}");
            }

            _logger.LogInformation(
                "Successfully indexed document {Id} to index {Index}",
                id,
                fullIndex);

            return true;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            _logger.LogError(ex, "Failed to index document {Id} to index {Index}", id, index);
            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to index document: {id}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<T>> SearchAsync<T>(
        string index,
        string query,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        try
        {
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Searching index {Index} with query: {Query}",
                fullIndex,
                query);

            var response = await _client.Value.SearchAsync<T>(s => s
                .Index(fullIndex)
                .Query(q => q
                    .QueryString(qs => qs
                        .Query(query)))
                .Size(_options.DefaultPageSize), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError(
                    "Search failed for index {Index}: {Error}",
                    fullIndex,
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Search failed: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Search failed: {response.ServerError}");
            }

            var results = response.Documents.ToList();

            _logger.LogInformation(
                "Search completed for index {Index}, found {Count} documents",
                fullIndex,
                results.Count);

            return results;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            _logger.LogError(ex, "Failed to search index {Index}", index);
            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to search index: {index}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string index,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        try
        {
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Deleting document {Id} from index {Index}",
                id,
                fullIndex);

            var response = await _client.Value.DeleteAsync<object>(id, d => d
                .Index(fullIndex), cancellationToken);

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                _logger.LogError(
                    "Failed to delete document {Id}: {Error}",
                    id,
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Failed to delete document: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Failed to delete document: {response.ServerError}");
            }

            var wasDeleted = response.Result == Result.Deleted;

            _logger.LogInformation(
                "Delete operation for document {Id} completed: {Result}",
                id,
                response.Result);

            return wasDeleted;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            _logger.LogError(ex, "Failed to delete document {Id} from index {Index}", id, index);
            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to delete document: {id}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> BulkIndexAsync<T>(
        string index,
        IDictionary<string, T> documents,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            _logger.LogDebug("No documents to bulk index, skipping");
            return 0;
        }

        try
        {
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Bulk indexing {Count} documents to index {Index}",
                documents.Count,
                fullIndex);

            var bulkDescriptor = new BulkDescriptor();
            foreach (var kvp in documents)
            {
                bulkDescriptor.Index<T>(i => i
                    .Index(fullIndex)
                    .Id(kvp.Key)
                    .Document(kvp.Value));
            }

            var response = await _client.Value.BulkAsync(bulkDescriptor, cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError(
                    "Bulk index failed: {Error}",
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Bulk index failed: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Bulk index failed: {response.ServerError}");
            }

            var successCount = response.Items.Count(i => i.IsValid);

            if (response.Errors)
            {
                _logger.LogWarning(
                    "Bulk index completed with errors: {SuccessCount}/{TotalCount} documents indexed",
                    successCount,
                    documents.Count);
            }
            else
            {
                _logger.LogInformation(
                    "Successfully bulk indexed {Count} documents to index {Index}",
                    successCount,
                    fullIndex);
            }

            return successCount;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            _logger.LogError(ex, "Failed to bulk index documents to index {Index}", index);
            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to bulk index documents to: {index}", ex);
        }
    }

    /// <summary>
    /// Gets the full index name with configured prefix.
    /// </summary>
    private string GetFullIndexName(string index)
    {
        return string.IsNullOrWhiteSpace(_options.IndexPrefix)
            ? index.ToLowerInvariant()
            : $"{_options.IndexPrefix.ToLowerInvariant()}{index.ToLowerInvariant()}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // OpenSearch.Client doesn't require explicit disposal
        // Connection pool is managed internally
        _logger.LogDebug("OpenSearchProvider disposed");

        _disposed = true;
    }
}
