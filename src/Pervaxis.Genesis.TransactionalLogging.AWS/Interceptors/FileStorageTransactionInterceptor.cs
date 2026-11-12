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

internal sealed class FileStorageTransactionInterceptor : IFileStorage
{
    private const string ProviderName = "FileStorage";
    private readonly IFileStorage _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public FileStorageTransactionInterceptor(IFileStorage inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<string> UploadAsync(string key, Stream content, string? contentType = null, IDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("upload")) return await _inner.UploadAsync(key, content, contentType, metadata, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.UploadAsync(key, content, contentType, metadata, cancellationToken); sw.Stop(); Record("upload", sw, "success", new { key, contentType }); return r; }
        catch (Exception ex) { sw.Stop(); Record("upload", sw, "error", new { key, error = ex.GetType().Name }); throw; }
    }

    public async Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("download")) return await _inner.DownloadAsync(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.DownloadAsync(key, cancellationToken); sw.Stop(); Record("download", sw, r is not null ? "success" : "not_found", new { key }); return r; }
        catch (Exception ex) { sw.Stop(); Record("download", sw, "error", new { key, error = ex.GetType().Name }); throw; }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("delete")) return await _inner.DeleteAsync(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.DeleteAsync(key, cancellationToken); sw.Stop(); Record("delete", sw, r ? "success" : "error", new { key }); return r; }
        catch (Exception ex) { sw.Stop(); Record("delete", sw, "error", new { key, error = ex.GetType().Name }); throw; }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("exists")) return await _inner.ExistsAsync(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.ExistsAsync(key, cancellationToken); sw.Stop(); Record("exists", sw, r ? "exists" : "not_found", new { key }); return r; }
        catch (Exception ex) { sw.Stop(); Record("exists", sw, "error", new { key, error = ex.GetType().Name }); throw; }
    }

    public async Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("get_presigned_url")) return await _inner.GetPresignedUrlAsync(key, expiry, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetPresignedUrlAsync(key, expiry, cancellationToken); sw.Stop(); Record("get_presigned_url", sw, "success", new { key }); return r; }
        catch (Exception ex) { sw.Stop(); Record("get_presigned_url", sw, "error", new { key, error = ex.GetType().Name }); throw; }
    }

    public async Task<IDictionary<string, string>> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("get_metadata")) return await _inner.GetMetadataAsync(key, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetMetadataAsync(key, cancellationToken); sw.Stop(); Record("get_metadata", sw, "success", new { key }); return r; }
        catch (Exception ex) { sw.Stop(); Record("get_metadata", sw, "error", new { key, error = ex.GetType().Name }); throw; }
    }

    public async Task<IEnumerable<string>> ListAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("list")) return await _inner.ListAsync(prefix, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.ListAsync(prefix, cancellationToken); sw.Stop(); Record("list", sw, "success", new { prefix }); return r; }
        catch (Exception ex) { sw.Stop(); Record("list", sw, "error", new { prefix, error = ex.GetType().Name }); throw; }
    }

    private bool ShouldCapture(string op) =>
        _contextAccessor.Current is not null && !_contextAccessor.IsSuppressed && _options.ImplicitCapture
        && !_options.ExcludeProviders.Contains(ProviderName, StringComparer.OrdinalIgnoreCase)
        && !_options.ExcludeOperations.Contains($"{ProviderName}.{op}", StringComparer.OrdinalIgnoreCase)
        && (_options.CaptureProviders.Count == 0 || _options.CaptureProviders.Contains(ProviderName, StringComparer.OrdinalIgnoreCase));

    private void Record(string op, Stopwatch sw, string result, object? p)
    {
        if (_options.MinimumDurationMs > 0 && sw.Elapsed.TotalMilliseconds < _options.MinimumDurationMs) return;
        _contextAccessor.Current?.AddEntry(InterceptorHelper.CreateEntry(ProviderName, op, sw.Elapsed.TotalMilliseconds, result == "success" || result == "exists", result));
    }
}
