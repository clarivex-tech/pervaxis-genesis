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

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.Search.AWS.Options;

namespace Pervaxis.Genesis.Search.AWS.Providers.OpenSearch;

/// <summary>
/// AWS OpenSearch implementation of the <see cref="ISearch"/> interface.
/// </summary>
public sealed class OpenSearchProvider : ISearch, IDisposable
{
    private readonly SearchOptions _options;
    private readonly ILogger<OpenSearchProvider> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IOpenSearchClient> _client;
    private readonly ResiliencePipeline _resiliencePipeline;
    private bool _disposed;

    // Metrics
    private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.search.operations",
        "1",
        "Total number of search operations");

    private static readonly Counter<long> _queriesExecuted = PervaxisMeter.CreateCounter<long>(
        "genesis.search.queries.executed",
        "1",
        "Total number of search queries executed");

    private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.search.operation.duration",
        "ms",
        "Duration of search operations in milliseconds");

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenSearchProvider"/> class.
    /// </summary>
    /// <param name="options">Search configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public OpenSearchProvider(
        IOptions<SearchOptions> options,
        ILogger<OpenSearchProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

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

        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "OpenSearch");

        _logger.LogInformation(
            "OpenSearchProvider initialized for domain {DomainEndpoint}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.DomainEndpoint,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Internal constructor for testing with injected client.
    /// </summary>
    internal OpenSearchProvider(
        IOptions<SearchOptions> options,
        ILogger<OpenSearchProvider> logger,
        IOpenSearchClient client,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(client);

        _tenantContext = tenantContext;

        _options = options.Value;
        _logger = logger;
        _client = new Lazy<IOpenSearchClient>(() => client);
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "OpenSearch");

        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<bool> IndexAsync<T>(
        string index,
        string id,
        T document,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("search.index", ActivityKind.Client);
        activity?.SetTag("search.system", "opensearch");
        activity?.SetTag("search.operation", "index");
        activity?.SetTag("search.index", index);
        AddTenantTags(activity);

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

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.IndexAsync(document, idx => idx
                    .Index(fullIndex)
                    .Id(id), ct),
                cancellationToken);

            if (!response.IsValid)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Failed to index document");
                _logger.LogError(
                    "Failed to index document {Id}: {Error}",
                    id,
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Failed to index document: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Failed to index document: {response.ServerError}");
            }

            activity?.SetTag("search.success", true);
            _logger.LogInformation(
                "Successfully indexed document {Id} to index {Index}",
                id,
                fullIndex);

            var tags = GetMetricTags("index", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return true;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to index document {Id} to index {Index}", id, index);

            var tags = GetMetricTags("index", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to index document: {id}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<T>> SearchAsync<T>(
        string index,
        string query,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("search.search", ActivityKind.Client);
        activity?.SetTag("search.system", "opensearch");
        activity?.SetTag("search.operation", "search");
        activity?.SetTag("search.index", index);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        try
        {
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Searching index {Index} with query: {Query}",
                fullIndex,
                query);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.SearchAsync<T>(s => s
                    .Index(fullIndex)
                    .Query(q => q
                        .QueryString(qs => qs
                            .Query(query)))
                    .Size(_options.DefaultPageSize), ct),
                cancellationToken);

            if (!response.IsValid)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Search failed");
                _logger.LogError(
                    "Search failed for index {Index}: {Error}",
                    fullIndex,
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Search failed: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Search failed: {response.ServerError}");
            }

            var results = response.Documents.ToList();

            activity?.SetTag("search.result_count", results.Count);
            _logger.LogInformation(
                "Search completed for index {Index}, found {Count} documents",
                fullIndex,
                results.Count);

            var tags = GetMetricTags("search", "success");
            _operationsCounter.Add(1, tags);
            _queriesExecuted.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return results;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to search index {Index}", index);

            var tags = GetMetricTags("search", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to search index: {index}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string index,
        string id,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("search.delete", ActivityKind.Client);
        activity?.SetTag("search.system", "opensearch");
        activity?.SetTag("search.operation", "delete");
        activity?.SetTag("search.index", index);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        try
        {
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Deleting document {Id} from index {Index}",
                id,
                fullIndex);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.DeleteAsync<object>(id, d => d
                    .Index(fullIndex), ct),
                cancellationToken);

            if (!response.IsValid && response.Result != Result.NotFound)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Failed to delete document");
                _logger.LogError(
                    "Failed to delete document {Id}: {Error}",
                    id,
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Failed to delete document: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Failed to delete document: {response.ServerError}");
            }

            var wasDeleted = response.Result == Result.Deleted;

            activity?.SetTag("search.success", wasDeleted);
            _logger.LogInformation(
                "Delete operation for document {Id} completed: {Result}",
                id,
                response.Result);

            var tags = GetMetricTags("delete", wasDeleted ? "success" : "not_found");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return wasDeleted;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to delete document {Id} from index {Index}", id, index);

            var tags = GetMetricTags("delete", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to delete document: {id}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> BulkIndexAsync<T>(
        string index,
        IDictionary<string, T> documents,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        ArgumentException.ThrowIfNullOrWhiteSpace(index);
        ArgumentNullException.ThrowIfNull(documents);

        using var activity = PervaxisActivitySource.StartActivity("search.bulk_index", ActivityKind.Client);
        activity?.SetTag("search.system", "opensearch");
        activity?.SetTag("search.operation", "bulk_index");
        activity?.SetTag("search.index", index);
        activity?.SetTag("search.document_count", documents.Count);
        AddTenantTags(activity);

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

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.BulkAsync(bulkDescriptor, ct),
                cancellationToken);

            if (!response.IsValid)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Bulk index failed");
                _logger.LogError(
                    "Bulk index failed: {Error}",
                    response.OriginalException?.Message ?? response.ServerError?.ToString());
                throw response.OriginalException != null
                    ? new GenesisException(nameof(OpenSearchProvider), $"Bulk index failed: {response.OriginalException.Message}", response.OriginalException)
                    : new GenesisException(nameof(OpenSearchProvider), $"Bulk index failed: {response.ServerError}");
            }

            var successCount = response.Items.Count(i => i.IsValid);

            activity?.SetTag("search.success_count", successCount);
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

            var tags = GetMetricTags("bulk_index", response.Errors ? "partial_success" : "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return successCount;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to bulk index documents to index {Index}", index);

            var tags = GetMetricTags("bulk_index", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(OpenSearchProvider), $"Failed to bulk index documents to: {index}", ex);
        }
    }

    /// <summary>
    /// Gets the full index name with configured prefix.
    /// </summary>
    private string GetFullIndexName(string index)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(_options.IndexPrefix))
        {
            parts.Add(_options.IndexPrefix.TrimEnd('-').ToLowerInvariant());
        }

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            parts.Add($"tenant-{_tenantContext.TenantId.Value.ToLowerInvariant()}");
        }

        parts.Add(index.ToLowerInvariant());

        return parts.Count > 1 ? string.Join("-", parts) : index.ToLowerInvariant();
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

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }

    private TagList GetMetricTags(string operation, string result)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "result", result }
        };
        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }
        return tags;
    }
}
