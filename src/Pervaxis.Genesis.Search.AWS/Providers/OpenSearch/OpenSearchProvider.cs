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
using System.Text;
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
using Pervaxis.Genesis.Base.Validation;
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
    private ConnectionSettings? _ownedSettings;
    private readonly object _tenantLock = new();
    private string? _boundTenant;
    internal const string TenantField = "tenantId";

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
    /// <param name="tenantContext">Current tenant context; operations fail when unresolved.</param>
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

        _client = new Lazy<IOpenSearchClient>(() => new OpenSearchClient(_ownedSettings = CreateSettings(_options)));

        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "OpenSearch");

        _logger.LogInformation(
            "OpenSearchProvider initialized for domain {DomainEndpoint}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.DomainEndpoint,
            _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Initializes a scoped provider with a container-owned or test client.
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

    internal static ConnectionSettings CreateSettings(SearchOptions options)
    {
        options.Validate();
        var uri = new Uri(options.DomainEndpoint);
        var connectionSettings = new ConnectionSettings(uri);
        connectionSettings.RequestTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        connectionSettings.MaximumRetries(options.MaxRetries);

        if (options.EnableDebugMode)
        {
            connectionSettings.EnableDebugMode();
        }

        if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
        {
            connectionSettings.BasicAuthentication(options.Username, options.Password);
        }

        return connectionSettings;
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
            var tenant = RequireTenant();
            var fullIndex = GetFullIndexName(index);
            var physicalId = GetDocumentId(tenant, id);
            var stamped = StampDocument(document, tenant);

            _logger.LogDebug(
                "Indexing document {Id} to index {Index}",
                id,
                fullIndex);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.IndexAsync(stamped, idx => idx
                    .Index(fullIndex)
                    .Id(physicalId), ct),
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
        catch (Exception ex)
        {
            if (ex is GenesisException gex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
                activity?.SetTag("exception.id", gex.ExceptionId);
                activity?.SetTag("exception.provider", gex.ProviderName);
                if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
                {
                    activity?.SetTag("exception.error_code", gex.ErrorCode);
                }

                _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
                throw;
            }

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
            var tenant = RequireTenant();
            var fullIndex = GetFullIndexName(index);

            _logger.LogDebug(
                "Searching index {Index} with query: {Query}",
                fullIndex,
                query);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.SearchAsync<T>(s => s
                    .Index(fullIndex)
                    .Query(q => q.Bool(b => b
                        .Filter(f => f.Term(t => t.Field(TenantField + ".keyword").Value(tenant)))
                        .Must(m => m.QueryString(qs => qs.Query(query)))))
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
        catch (Exception ex)
        {
            if (ex is GenesisException gex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
                activity?.SetTag("exception.id", gex.ExceptionId);
                activity?.SetTag("exception.provider", gex.ProviderName);
                if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
                {
                    activity?.SetTag("exception.error_code", gex.ErrorCode);
                }

                _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
                throw;
            }

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
            var tenant = RequireTenant();
            var fullIndex = GetFullIndexName(index);
            var physicalId = GetDocumentId(tenant, id);

            _logger.LogDebug(
                "Deleting document {Id} from index {Index}",
                id,
                fullIndex);

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.DeleteAsync<object>(physicalId, d => d
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
        catch (Exception ex)
        {
            if (ex is GenesisException gex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
                activity?.SetTag("exception.id", gex.ExceptionId);
                activity?.SetTag("exception.provider", gex.ProviderName);
                if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
                {
                    activity?.SetTag("exception.error_code", gex.ErrorCode);
                }

                _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
                throw;
            }

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

        var tenant = RequireTenant();
        GetFullIndexName(index);
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
                bulkDescriptor.Index<Dictionary<string, object>>(i => i
                    .Index(fullIndex)
                    .Id(GetDocumentId(tenant, kvp.Key))
                    .Document(StampDocument(kvp.Value, tenant)));
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
        catch (Exception ex)
        {
            if (ex is GenesisException gex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
                activity?.SetTag("exception.id", gex.ExceptionId);
                activity?.SetTag("exception.provider", gex.ProviderName);
                if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
                {
                    activity?.SetTag("exception.error_code", gex.ErrorCode);
                }

                _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
                throw;
            }

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
        var fullIndex = string.IsNullOrWhiteSpace(_options.IndexPrefix)
            ? index : _options.IndexPrefix.TrimEnd('-') + "-" + index;
        if (fullIndex.Length > 255 || fullIndex.Length == 0
            || !char.IsAsciiLetterOrDigit(fullIndex[0])
            || fullIndex.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            throw Failure("A concrete lowercase index name is required.", "InvalidIndexName");
        }
        return fullIndex;
    }

    private string RequireTenant()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_tenantContext?.IsResolved != true)
        {
            throw Failure("Tenant context not resolved.", "TenantContextNotResolved");
        }
        var tenant = _tenantContext.TenantId.Value;
        if (!TenantIdValidator.IsValid(tenant) || tenant.Any(char.IsControl))
        {
            throw Failure("Invalid tenant ID.", "InvalidTenantId");
        }
        lock (_tenantLock)
        {
            if (_boundTenant is not null && !StringComparer.Ordinal.Equals(_boundTenant, tenant))
            {
                throw Failure("Tenant changed during the provider scope.", "TenantContextChanged");
            }
            _boundTenant = tenant;
        }
        return tenant;
    }

    private static string GetDocumentId(string tenant, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var physicalId = tenant + ":" + id;
        if (id.Any(char.IsControl) || Encoding.UTF8.GetByteCount(physicalId) > 512)
        {
            throw Failure("Invalid document ID or ID exceeds 512 UTF-8 bytes.", "InvalidDocumentId");
        }
        return physicalId;
    }

    private Dictionary<string, object> StampDocument<T>(T document, string tenant) where T : class
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream();
        _client.Value.SourceSerializer.Serialize(document, stream);
        stream.Position = 0;
        var fields = _client.Value.SourceSerializer.Deserialize<Dictionary<string, object>>(stream)
            ?? throw Failure("Document must be a JSON object.", "InvalidDocument");
        if (fields.Keys.Any(k => k.Equals(TenantField, StringComparison.OrdinalIgnoreCase)
            || k.StartsWith(TenantField + ".", StringComparison.OrdinalIgnoreCase)))
        {
            throw Failure("tenantId is reserved for the provider.", "ReservedTenantField");
        }
        fields.Add(TenantField, tenant);
        return fields;
    }

    private static GenesisException Failure(string message, string code)
        => new(nameof(OpenSearchProvider), message, code, null);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // DI owns shared settings. Only directly constructed providers own settings here.
        _logger.LogDebug("OpenSearchProvider disposed");

        (_ownedSettings as IDisposable)?.Dispose();
        _disposed = true;
    }

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || _tenantContext?.IsResolved != true)
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
        if (_tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }
        return tags;
    }
}
