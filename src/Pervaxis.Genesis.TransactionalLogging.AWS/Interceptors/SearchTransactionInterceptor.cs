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

internal sealed class SearchTransactionInterceptor : ISearch
{
    private const string ProviderName = "Search";
    private readonly ISearch _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public SearchTransactionInterceptor(ISearch inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<bool> IndexAsync<T>(string index, string id, T document, CancellationToken cancellationToken = default) where T : class
    {
        if (!ShouldCapture("index")) return await _inner.IndexAsync(index, id, document, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.IndexAsync(index, id, document, cancellationToken); sw.Stop(); Record("index", sw, r ? "success" : "error", new { index, id }); return r; }
        catch (Exception ex) { sw.Stop(); Record("index", sw, "error", new { index, id, error = ex.GetType().Name }); throw; }
    }

    public async Task<IEnumerable<T>> SearchAsync<T>(string index, string query, CancellationToken cancellationToken = default) where T : class
    {
        if (!ShouldCapture("search")) return await _inner.SearchAsync<T>(index, query, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SearchAsync<T>(index, query, cancellationToken); sw.Stop(); Record("search", sw, "success", new { index, query }); return r; }
        catch (Exception ex) { sw.Stop(); Record("search", sw, "error", new { index, query, error = ex.GetType().Name }); throw; }
    }

    public async Task<bool> DeleteAsync(string index, string id, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("delete")) return await _inner.DeleteAsync(index, id, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.DeleteAsync(index, id, cancellationToken); sw.Stop(); Record("delete", sw, r ? "success" : "error", new { index, id }); return r; }
        catch (Exception ex) { sw.Stop(); Record("delete", sw, "error", new { index, id, error = ex.GetType().Name }); throw; }
    }

    public async Task<int> BulkIndexAsync<T>(string index, IDictionary<string, T> documents, CancellationToken cancellationToken = default) where T : class
    {
        if (!ShouldCapture("bulk_index")) return await _inner.BulkIndexAsync(index, documents, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.BulkIndexAsync(index, documents, cancellationToken); sw.Stop(); Record("bulk_index", sw, "success", new { index, count = documents.Count }); return r; }
        catch (Exception ex) { sw.Stop(); Record("bulk_index", sw, "error", new { index, error = ex.GetType().Name }); throw; }
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
