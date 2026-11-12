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

/// <summary>
/// Decorator that intercepts INotification operations and records them in the current TransactionContext.
/// </summary>
internal sealed class NotificationsTransactionInterceptor : INotification
{
    private const string ProviderName = "Notifications";
    private readonly INotification _inner;
    private readonly TransactionContextAccessor _contextAccessor;
    private readonly TransactionalLoggingOptions _options;
    private readonly ParameterSanitizer _sanitizer;

    public NotificationsTransactionInterceptor(INotification inner, TransactionContextAccessor contextAccessor,
        IOptions<TransactionalLoggingOptions> options, ParameterSanitizer sanitizer)
    { _inner = inner; _contextAccessor = contextAccessor; _options = options.Value; _sanitizer = sanitizer; }

    public async Task<string> SendEmailAsync(string to, string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("send_email")) return await _inner.SendEmailAsync(to, subject, body, isHtml, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SendEmailAsync(to, subject, body, isHtml, cancellationToken); sw.Stop(); Record("send_email", sw, "success", new { to, subject }); return r; }
        catch (Exception ex) { sw.Stop(); Record("send_email", sw, "error", new { to, error = ex.GetType().Name }); throw; }
    }

    public async Task<string> SendTemplatedEmailAsync(string to, string templateId, IDictionary<string, string> templateData, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("send_templated_email")) return await _inner.SendTemplatedEmailAsync(to, templateId, templateData, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SendTemplatedEmailAsync(to, templateId, templateData, cancellationToken); sw.Stop(); Record("send_templated_email", sw, "success", new { to, templateId }); return r; }
        catch (Exception ex) { sw.Stop(); Record("send_templated_email", sw, "error", new { to, error = ex.GetType().Name }); throw; }
    }

    public async Task<string> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("send_sms")) return await _inner.SendSmsAsync(phoneNumber, message, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SendSmsAsync(phoneNumber, message, cancellationToken); sw.Stop(); Record("send_sms", sw, "success", new { phoneNumber = "[REDACTED]" }); return r; }
        catch (Exception ex) { sw.Stop(); Record("send_sms", sw, "error", new { error = ex.GetType().Name }); throw; }
    }

    public async Task<string> SendPushAsync(string deviceToken, string title, string body, IDictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        if (!ShouldCapture("send_push")) return await _inner.SendPushAsync(deviceToken, title, body, data, cancellationToken);
        var sw = Stopwatch.StartNew();
        try { var r = await _inner.SendPushAsync(deviceToken, title, body, data, cancellationToken); sw.Stop(); Record("send_push", sw, "success", new { title }); return r; }
        catch (Exception ex) { sw.Stop(); Record("send_push", sw, "error", new { title, error = ex.GetType().Name }); throw; }
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
