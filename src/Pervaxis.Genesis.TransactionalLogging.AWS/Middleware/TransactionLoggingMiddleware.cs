/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.TransactionalLogging.AWS.Attributes;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Diagnostics;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Middleware;

/// <summary>
/// ASP.NET Core middleware that creates and finalizes a TransactionContext per request.
/// </summary>
public sealed class TransactionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TransactionalLoggingOptions _options;
    private readonly ILogger<TransactionLoggingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionLoggingMiddleware"/> class.
    /// </summary>
    public TransactionLoggingMiddleware(
        RequestDelegate next,
        IOptions<TransactionalLoggingOptions> options,
        ILogger<TransactionLoggingMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request, creating a transaction scope and persisting the log on completion.
    /// </summary>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!_options.Enabled || IsSuppressed(httpContext))
        {
            await _next(httpContext);
            return;
        }

        var contextAccessor = httpContext.RequestServices.GetRequiredService<TransactionContextAccessor>();
        var store = httpContext.RequestServices.GetRequiredService<ITransactionLogStore>();
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();

        var context = new TransactionContext
        {
            TraceId = Activity.Current?.TraceId.ToString(),
            TenantId = tenantContext?.IsResolved == true
                ? tenantContext.TenantId.Value.ToString() : null,
            HttpMethod = httpContext.Request.Method,
            RequestPath = httpContext.Request.Path.Value,
            IdempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(),
            CorrelationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        };

        contextAccessor.Current = context;

        TransactionLogLogMessages.TransactionContextCreated(
            _logger, context.TransactionId, context.TraceId, context.TenantId, context.RequestPath);

        try
        {
            await _next(httpContext);
            context.Finalize(httpContext.Response.StatusCode, TransactionLogStatus.Completed);
        }
        catch (Exception ex)
        {
            context.Finalize(500, TransactionLogStatus.Failed, ex.GetType().Name, ex.Message);
            throw;
        }
        finally
        {
            contextAccessor.Current = null;

            TransactionLogLogMessages.TransactionContextFinalized(
                _logger, context.TransactionId, context.Entries.Count,
                context.DurationMs ?? 0, context.Status.ToString());

            _ = PersistSafelyAsync(store, context);
        }
    }

    private async Task PersistSafelyAsync(ITransactionLogStore store, TransactionContext context)
    {
        try
        {
            await store.PersistAsync(context);
            TransactionLogMetrics.RecordTransaction(context);
        }
        catch (Exception ex)
        {
            TransactionLogMetrics.RecordPersistFailure("other");
            TransactionLogLogMessages.PersistenceFailed(
                _logger, context.TransactionId, ex.Message, false);
        }
    }

    private bool IsSuppressed(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<SuppressTransactionLogAttribute>() is not null)
        {
            return true;
        }

        var path = httpContext.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return _options.SuppressRoutes.Any(pattern =>
            path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
