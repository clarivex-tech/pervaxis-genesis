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

internal sealed class WorkflowTransactionInterceptor : IWorkflow
{
    private const string ProviderName = "Workflow";
    private readonly IWorkflow _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public WorkflowTransactionInterceptor(IWorkflow inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<string> StartExecutionAsync(string workflowName, object input, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("start_execution")) return await _inner.StartExecutionAsync(workflowName, input, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.StartExecutionAsync(workflowName, input, cancellationToken); sw.Stop(); Record("start_execution", sw, "success", new { workflowName }); return r; }
        catch (Exception ex) { sw.Stop(); Record("start_execution", sw, "error", new { workflowName, error = ex.GetType().Name }); throw; }
    }

    public async Task<string> GetExecutionStatusAsync(string executionArn, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("get_execution_status")) return await _inner.GetExecutionStatusAsync(executionArn, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetExecutionStatusAsync(executionArn, cancellationToken); sw.Stop(); Record("get_execution_status", sw, "success", new { executionArn }); return r; }
        catch (Exception ex) { sw.Stop(); Record("get_execution_status", sw, "error", new { executionArn, error = ex.GetType().Name }); throw; }
    }

    public async Task<T?> GetExecutionOutputAsync<T>(string executionArn, CancellationToken cancellationToken = default) where T : class
    {
        if (!ShouldCapture("get_execution_output")) return await _inner.GetExecutionOutputAsync<T>(executionArn, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.GetExecutionOutputAsync<T>(executionArn, cancellationToken); sw.Stop(); Record("get_execution_output", sw, "success", new { executionArn }); return r; }
        catch (Exception ex) { sw.Stop(); Record("get_execution_output", sw, "error", new { executionArn, error = ex.GetType().Name }); throw; }
    }

    public async Task<bool> StopExecutionAsync(string executionArn, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("stop_execution")) return await _inner.StopExecutionAsync(executionArn, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.StopExecutionAsync(executionArn, cancellationToken); sw.Stop(); Record("stop_execution", sw, r ? "success" : "error", new { executionArn }); return r; }
        catch (Exception ex) { sw.Stop(); Record("stop_execution", sw, "error", new { executionArn, error = ex.GetType().Name }); throw; }
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
