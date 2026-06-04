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

internal sealed class MessagingTransactionInterceptor : IMessaging
{
    private const string ProviderName = "Messaging";
    private readonly IMessaging _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public MessagingTransactionInterceptor(IMessaging inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<string> PublishAsync<T>(string destination, T message, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("publish")) return await _inner.PublishAsync(destination, message, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.PublishAsync(destination, message, cancellationToken); sw.Stop(); Record("publish", sw, "success", new { destination }); return r; }
        catch (Exception ex) { sw.Stop(); Record("publish", sw, "error", new { destination, error = ex.GetType().Name }); throw; }
    }

    public async Task<IEnumerable<string>> PublishBatchAsync<T>(string destination, IEnumerable<T> messages, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("publish_batch")) return await _inner.PublishBatchAsync(destination, messages, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.PublishBatchAsync(destination, messages, cancellationToken); sw.Stop(); Record("publish_batch", sw, "success", new { destination }); return r; }
        catch (Exception ex) { sw.Stop(); Record("publish_batch", sw, "error", new { destination, error = ex.GetType().Name }); throw; }
    }

    public async Task<IEnumerable<T>> ReceiveAsync<T>(string queue, int maxMessages = 10, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("receive")) return await _inner.ReceiveAsync<T>(queue, maxMessages, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.ReceiveAsync<T>(queue, maxMessages, cancellationToken); sw.Stop(); Record("receive", sw, "success", new { queue, maxMessages }); return r; }
        catch (Exception ex) { sw.Stop(); Record("receive", sw, "error", new { queue, error = ex.GetType().Name }); throw; }
    }

    public async Task<bool> DeleteAsync(string source, string receiptHandle, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("delete")) return await _inner.DeleteAsync(source, receiptHandle, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.DeleteAsync(source, receiptHandle, cancellationToken); sw.Stop(); Record("delete", sw, r ? "success" : "error", new { source }); return r; }
        catch (Exception ex) { sw.Stop(); Record("delete", sw, "error", new { source, error = ex.GetType().Name }); throw; }
    }

    public async Task<string> SubscribeAsync(string topic, string endpoint, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("subscribe")) return await _inner.SubscribeAsync(topic, endpoint, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SubscribeAsync(topic, endpoint, cancellationToken); sw.Stop(); Record("subscribe", sw, "success", new { topic, endpoint }); return r; }
        catch (Exception ex) { sw.Stop(); Record("subscribe", sw, "error", new { topic, error = ex.GetType().Name }); throw; }
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
