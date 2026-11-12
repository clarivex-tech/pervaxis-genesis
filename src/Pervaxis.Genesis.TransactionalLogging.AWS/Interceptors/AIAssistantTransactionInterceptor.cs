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

internal sealed class AIAssistantTransactionInterceptor : IAIAssistant
{
    private const string ProviderName = "AIAssistance";
    private readonly IAIAssistant _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public AIAssistantTransactionInterceptor(IAIAssistant inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("generate_text")) return await _inner.GenerateTextAsync(prompt, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GenerateTextAsync(prompt, cancellationToken); sw.Stop(); Record("generate_text", sw, "success", new { prompt_length = prompt.Length }); return r; }
        catch (Exception ex) { sw.Stop(); Record("generate_text", sw, "error", new { error = ex.GetType().Name }); throw; }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("generate_embedding")) return await _inner.GenerateEmbeddingAsync(text, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GenerateEmbeddingAsync(text, cancellationToken); sw.Stop(); Record("generate_embedding", sw, "success", new { text_length = text.Length }); return r; }
        catch (Exception ex) { sw.Stop(); Record("generate_embedding", sw, "error", new { error = ex.GetType().Name }); throw; }
    }

    public async Task<byte[]> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("generate_image")) return await _inner.GenerateImageAsync(prompt, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GenerateImageAsync(prompt, cancellationToken); sw.Stop(); Record("generate_image", sw, "success", new { prompt_length = prompt.Length }); return r; }
        catch (Exception ex) { sw.Stop(); Record("generate_image", sw, "error", new { error = ex.GetType().Name }); throw; }
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
