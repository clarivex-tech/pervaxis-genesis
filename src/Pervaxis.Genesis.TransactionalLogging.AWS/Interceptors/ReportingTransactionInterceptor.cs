/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Sanitization;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Interceptors;

internal sealed class ReportingTransactionInterceptor : IReporting
{
    private const string ProviderName = "Reporting";
    private readonly IReporting _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public ReportingTransactionInterceptor(IReporting inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<IEnumerable<T>> ExecuteQueryAsync<T>(string query, CancellationToken cancellationToken = default) where T : class
    {
        if (!ShouldCapture("execute_query")) return await _inner.ExecuteQueryAsync<T>(query, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.ExecuteQueryAsync<T>(query, cancellationToken); sw.Stop(); Record("execute_query", sw, "success", new { query }); return r; }
        catch (Exception ex) { sw.Stop(); Record("execute_query", sw, "error", new { query, error = ex.GetType().Name }); throw; }
    }

    public async Task<object> GetDashboardAsync(string dashboardId, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("get_dashboard")) return await _inner.GetDashboardAsync(dashboardId, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetDashboardAsync(dashboardId, cancellationToken); sw.Stop(); Record("get_dashboard", sw, "success", new { dashboardId }); return r; }
        catch (Exception ex) { sw.Stop(); Record("get_dashboard", sw, "error", new { dashboardId, error = ex.GetType().Name }); throw; }
    }

    public async Task<string> CreateDashboardAsync(string name, object configuration, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("create_dashboard")) return await _inner.CreateDashboardAsync(name, configuration, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.CreateDashboardAsync(name, configuration, cancellationToken); sw.Stop(); Record("create_dashboard", sw, "success", new { name }); return r; }
        catch (Exception ex) { sw.Stop(); Record("create_dashboard", sw, "error", new { name, error = ex.GetType().Name }); throw; }
    }

    public async Task<byte[]> ExportReportAsync(string reportId, string format, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("export_report")) return await _inner.ExportReportAsync(reportId, format, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.ExportReportAsync(reportId, format, cancellationToken); sw.Stop(); Record("export_report", sw, "success", new { reportId, format }); return r; }
        catch (Exception ex) { sw.Stop(); Record("export_report", sw, "error", new { reportId, error = ex.GetType().Name }); throw; }
    }

    private bool ShouldCapture(string op) =>
        _contextAccessor.Current is not null && !_contextAccessor.IsSuppressed && _options.ImplicitCapture
        && !_options.ExcludeProviders.Contains(ProviderName, StringComparer.OrdinalIgnoreCase)
        && !_options.ExcludeOperations.Contains($"{ProviderName}.{op}", StringComparer.OrdinalIgnoreCase)
        && (_options.CaptureProviders.Count == 0 || _options.CaptureProviders.Contains(ProviderName, StringComparer.OrdinalIgnoreCase));

    private void Record(string op, Stopwatch sw, string result, object? p)
    {
        if (_options.MinimumDurationMs > 0 && sw.Elapsed.TotalMilliseconds < _options.MinimumDurationMs) return;
        _contextAccessor.Current?.AddEntry(InterceptorHelper.CreateEntry(ProviderName, op, sw.Elapsed.TotalMilliseconds, result == "success", result));
    }
}
