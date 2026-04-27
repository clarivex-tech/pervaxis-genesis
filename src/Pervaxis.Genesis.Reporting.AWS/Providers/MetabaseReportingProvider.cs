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
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.Reporting.AWS.Options;

namespace Pervaxis.Genesis.Reporting.AWS.Providers;

/// <summary>
/// Metabase implementation of the IReporting interface.
/// Supports query execution, dashboard management, and report exports.
/// </summary>
public sealed class MetabaseReportingProvider : IReporting, IDisposable
{
    private readonly ILogger<MetabaseReportingProvider> _logger;
    private readonly ReportingOptions _options;
    private readonly ITenantContext? _tenantContext;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResiliencePipeline _resiliencePipeline;
    private bool _disposed;

    // Metrics
    private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.reporting.operations",
        "1",
        "Total number of reporting operations");

    private static readonly Counter<long> _queriesExecuted = PervaxisMeter.CreateCounter<long>(
        "genesis.reporting.queries.executed",
        "1",
        "Total number of queries executed");

    private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.reporting.operation.duration",
        "ms",
        "Duration of reporting operations in milliseconds");

    /// <summary>
    /// Initializes a new instance of the <see cref="MetabaseReportingProvider"/> class.
    /// </summary>
    /// <param name="options">The reporting options.</param>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="GenesisConfigurationException">Thrown when options validation fails.</exception>
    public MetabaseReportingProvider(
        IOptions<ReportingOptions> options,
        HttpClient httpClient,
        ILogger<MetabaseReportingProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
        _httpClient = httpClient;

        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(nameof(ReportingOptions), "Invalid reporting options configuration");
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _options.ApiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "MetabaseReporting");

        _logger.LogInformation(
            "MetabaseReportingProvider initialized for {BaseUrl}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.BaseUrl,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Internal constructor for testing with pre-configured client.
    /// </summary>
    internal MetabaseReportingProvider(
        ReportingOptions options,
        HttpClient httpClient,
        ILogger<MetabaseReportingProvider> logger,
        bool skipValidation,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _tenantContext = tenantContext;

        _options = options;
        _logger = logger;
        _httpClient = httpClient;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "MetabaseReporting");
    }

    /// <inheritdoc />
    public async Task<IEnumerable<T>> ExecuteQueryAsync<T>(
        string query,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("reporting.execute_query", ActivityKind.Client);
        activity?.SetTag("reporting.system", "metabase");
        activity?.SetTag("reporting.operation", "execute_query");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        try
        {
            var requestBody = new
            {
                database = _options.DatabaseId,
                type = "native",
                native = new
                {
                    query
                }
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _httpClient.PostAsJsonAsync(
                    "/api/dataset",
                    requestBody,
                    _jsonOptions,
                    ct),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MetabaseQueryResponse>(
                _jsonOptions,
                cancellationToken);

            if (result?.Data?.Rows == null)
            {
                _logger.LogWarning("Query returned no data");
                return Enumerable.Empty<T>();
            }

            var mappedResults = MapResultsToType<T>(result.Data.Rows, result.Data.Cols).ToList();

            activity?.SetTag("reporting.result_count", mappedResults.Count);
            _logger.LogInformation(
                "Query executed successfully, returned {Count} rows",
                mappedResults.Count);

            var tags = GetMetricTags("execute_query", "success");
            _operationsCounter.Add(1, tags);
            _queriesExecuted.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return mappedResults;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to execute query");

            var tags = GetMetricTags("execute_query", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(
                nameof(MetabaseReportingProvider),
                $"Failed to execute query: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<object> GetDashboardAsync(
        string dashboardId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("reporting.get_dashboard", ActivityKind.Client);
        activity?.SetTag("reporting.system", "metabase");
        activity?.SetTag("reporting.operation", "get_dashboard");
        activity?.SetTag("reporting.dashboard_id", dashboardId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(dashboardId, nameof(dashboardId));

        try
        {
            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _httpClient.GetAsync(
                    new Uri($"/api/dashboard/{dashboardId}", UriKind.Relative),
                    ct),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var dashboard = await response.Content.ReadFromJsonAsync<object>(
                _jsonOptions,
                cancellationToken);

            _logger.LogInformation(
                "Retrieved dashboard {DashboardId}",
                dashboardId);

            var tags = GetMetricTags("get_dashboard", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return dashboard ?? new object();
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var tags = GetMetricTags("get_dashboard", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            _logger.LogError(ex, "Failed to get dashboard {DashboardId}", dashboardId);
            throw new GenesisException(
                nameof(MetabaseReportingProvider),
                $"Failed to get dashboard: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateDashboardAsync(
        string name,
        object definition,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("reporting.create_dashboard", ActivityKind.Client);
        activity?.SetTag("reporting.system", "metabase");
        activity?.SetTag("reporting.operation", "create_dashboard");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));

        try
        {
            var requestBody = new
            {
                name,
                description = "",
                parameters = Array.Empty<object>()
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _httpClient.PostAsJsonAsync(
                    "/api/dashboard",
                    requestBody,
                    _jsonOptions,
                    ct),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<MetabaseDashboardResponse>(
                _jsonOptions,
                cancellationToken);

            var dashboardId = result?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            activity?.SetTag("reporting.dashboard_id", dashboardId);
            _logger.LogInformation(
                "Created dashboard '{Name}' with ID {DashboardId}",
                name, dashboardId);

            var tags = GetMetricTags("create_dashboard", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return dashboardId;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to create dashboard '{Name}'", name);

            var tags = GetMetricTags("create_dashboard", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(
                nameof(MetabaseReportingProvider),
                $"Failed to create dashboard: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportReportAsync(
        string reportId,
        string format,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("reporting.export_report", ActivityKind.Client);
        activity?.SetTag("reporting.system", "metabase");
        activity?.SetTag("reporting.operation", "export_report");
        activity?.SetTag("reporting.format", format);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(reportId, nameof(reportId));
        ArgumentException.ThrowIfNullOrWhiteSpace(format, nameof(format));

        var normalizedFormat = format.ToLowerInvariant();
        var endpoint = normalizedFormat switch
        {
            "csv" => $"/api/card/{reportId}/query/csv",
            "json" => $"/api/card/{reportId}/query/json",
            "xlsx" => $"/api/card/{reportId}/query/xlsx",
            _ => throw new ArgumentException($"Unsupported format: {format}", nameof(format))
        };

        try
        {

            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _httpClient.PostAsync(
                    new Uri(endpoint, UriKind.Relative),
                    content,
                    ct),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            activity?.SetTag("reporting.export_size", data.Length);
            _logger.LogInformation(
                "Exported report {ReportId} as {Format} ({Size} bytes)",
                reportId, format, data.Length);

            var tags = GetMetricTags("export_report", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return data;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to export report {ReportId} as {Format}", reportId, format);

            var tags = GetMetricTags("export_report", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(
                nameof(MetabaseReportingProvider),
                $"Failed to export report: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Maps Metabase result rows to typed objects.
    /// </summary>
    private static IEnumerable<T> MapResultsToType<T>(
        List<List<object?>> rows,
        List<MetabaseColumn> columns) where T : class
    {
        if (rows.Count == 0 || columns.Count == 0)
        {
            return Enumerable.Empty<T>();
        }

        var results = new List<T>();
        var type = typeof(T);
        var properties = type.GetProperties();

        foreach (var row in rows)
        {
            if (row.Count != columns.Count)
            {
                continue;
            }

            var instance = Activator.CreateInstance<T>();

            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var value = row[i];

                var property = properties.FirstOrDefault(p =>
                    p.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase));

                if (property != null && property.CanWrite && value != null)
                {
                    try
                    {
                        object convertedValue;

                        // Handle JsonElement from System.Text.Json
                        if (value is JsonElement jsonElement)
                        {
                            convertedValue = jsonElement.ValueKind switch
                            {
                                JsonValueKind.Number => jsonElement.TryGetInt32(out var intVal) ? intVal : jsonElement.GetDouble(),
                                JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => value
                            };
                        }
                        else
                        {
                            convertedValue = value;
                        }

                        var finalValue = Convert.ChangeType(convertedValue, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                        property.SetValue(instance, finalValue);
                    }
                    catch (InvalidCastException)
                    {
                        // Skip properties that can't be converted
                    }
                    catch (FormatException)
                    {
                        // Skip properties with invalid format
                    }
                }
            }

            results.Add(instance);
        }

        return results;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
        _logger.LogInformation("MetabaseReportingProvider disposed");
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

    private sealed class MetabaseQueryResponse
    {
        public MetabaseQueryData? Data { get; set; }
    }

    private sealed class MetabaseQueryData
    {
        public List<MetabaseColumn> Cols { get; set; } = new();
        public List<List<object?>> Rows { get; set; } = new();
    }

    private sealed class MetabaseColumn
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string BaseType { get; set; } = string.Empty;
    }

    private sealed class MetabaseDashboardResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
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
