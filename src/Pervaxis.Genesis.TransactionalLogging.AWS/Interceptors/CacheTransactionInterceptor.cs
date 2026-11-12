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

internal sealed class CacheTransactionInterceptor : ICache
{
    private const string ProviderName = "Caching";
    private readonly ICache _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public CacheTransactionInterceptor(ICache inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("get")) return await _inner.GetAsync<T>(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetAsync<T>(key, cancellationToken); sw.Stop(); Record("get", sw, true, $"key={key},result={( r is not null ? "hit" : "miss")}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("get", sw, false, $"key={key},error={ex.GetType().Name}"); throw; }
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("set")) return await _inner.SetAsync(key, value, expiry, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SetAsync(key, value, expiry, cancellationToken); sw.Stop(); Record("set", sw, r, $"key={key}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("set", sw, false, $"key={key},error={ex.GetType().Name}"); throw; }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("remove")) return await _inner.RemoveAsync(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.RemoveAsync(key, cancellationToken); sw.Stop(); Record("remove", sw, r, $"key={key}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("remove", sw, false, $"key={key},error={ex.GetType().Name}"); throw; }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("exists")) return await _inner.ExistsAsync(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.ExistsAsync(key, cancellationToken); sw.Stop(); Record("exists", sw, true, $"key={key},exists={r}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("exists", sw, false, $"key={key},error={ex.GetType().Name}"); throw; }
    }

    public async Task<IDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("get_many")) return await _inner.GetManyAsync<T>(keys, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetManyAsync<T>(keys, cancellationToken); sw.Stop(); Record("get_many", sw, true, $"count={r.Count}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("get_many", sw, false, $"error={ex.GetType().Name}"); throw; }
    }

    public async Task<bool> SetManyAsync<T>(IDictionary<string, T> items, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("set_many")) return await _inner.SetManyAsync(items, expiry, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SetManyAsync(items, expiry, cancellationToken); sw.Stop(); Record("set_many", sw, r, $"count={items.Count}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("set_many", sw, false, $"count={items.Count},error={ex.GetType().Name}"); throw; }
    }

    public async Task<bool> RefreshAsync(string key, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("refresh")) return await _inner.RefreshAsync(key, expiry, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.RefreshAsync(key, expiry, cancellationToken); sw.Stop(); Record("refresh", sw, r, $"key={key}"); return r; }
        catch (Exception ex) { sw.Stop(); Record("refresh", sw, false, $"key={key},error={ex.GetType().Name}"); throw; }
    }

    private bool ShouldCapture(string op) =>
        _contextAccessor.Current is not null && !_contextAccessor.IsSuppressed && _options.ImplicitCapture
        && !_options.ExcludeProviders.Contains(ProviderName, StringComparer.OrdinalIgnoreCase)
        && !_options.ExcludeOperations.Contains($"{ProviderName}.{op}", StringComparer.OrdinalIgnoreCase)
        && (_options.CaptureProviders.Count == 0 || _options.CaptureProviders.Contains(ProviderName, StringComparer.OrdinalIgnoreCase));

    private void Record(string op, Stopwatch sw, bool success, string? details)
    {
        if (_options.MinimumDurationMs > 0 && sw.Elapsed.TotalMilliseconds < _options.MinimumDurationMs) return;
        _contextAccessor.Current?.AddEntry(InterceptorHelper.CreateEntry(ProviderName, op, sw.Elapsed.TotalMilliseconds, success, details));
    }
}
