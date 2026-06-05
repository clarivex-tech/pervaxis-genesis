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

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Idempotency.Abstractions;
using Pervaxis.Genesis.Idempotency.Options;
using Pervaxis.Genesis.Idempotency.Services;

namespace Pervaxis.Genesis.Idempotency.Filters;

/// <summary>
/// Action filter that implements the idempotency logic:
/// validates key, checks store, creates in-flight record, caches response.
/// </summary>
[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fail-open by design: store failures must not affect request processing.")]
internal sealed class IdempotencyActionFilter : IAsyncActionFilter
{
    private readonly IIdempotencyStore _store;
    private readonly IIdempotencyKeyValidator _keyValidator;
    private readonly IRequestFingerprintComputer _fingerprintComputer;
    private readonly IdempotencyOptions _options;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<IdempotencyActionFilter> _logger;

    public IdempotencyActionFilter(
        IIdempotencyStore store,
        IIdempotencyKeyValidator keyValidator,
        IRequestFingerprintComputer fingerprintComputer,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyActionFilter> logger,
        ITenantContext? tenantContext = null)
    {
        _store = store;
        _keyValidator = keyValidator;
        _fingerprintComputer = fingerprintComputer;
        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var headerValues = httpContext.Request.Headers[_options.HeaderName];

        // If no header present, check if this is attribute-driven (key required) or middleware (optional)
        if (headerValues.Count == 0)
        {
            var hasAttribute = context.ActionDescriptor.EndpointMetadata
                .OfType<IdempotentAttribute>().Any();

            if (hasAttribute)
            {
                context.Result = CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "IDEMPOTENCY_KEY_MISSING",
                    $"The {_options.HeaderName} header is required for this endpoint.");
                return;
            }

            // No attribute + no header = pass through
            await next();
            return;
        }

        // Validate the key
        var hasMultipleValues = headerValues.Count > 1;
        var keyValue = headerValues.FirstOrDefault();
        var validationResult = _keyValidator.Validate(keyValue, hasMultipleValues);

        if (!validationResult.IsValid)
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                validationResult.ErrorCode!,
                validationResult.ErrorMessage!);
            return;
        }

        var idempotencyKey = keyValue!;
        var tenantId = ResolveTenantId();

        // Validate tenant ID doesn't contain the composite key separator
        if (tenantId.Contains('#', StringComparison.Ordinal))
        {
            context.Result = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                "IDEMPOTENCY_TENANT_INVALID",
                "Tenant ID must not contain the '#' character.");
            return;
        }

        // Check for existing record
        IdempotencyRecord? existingRecord;
        try
        {
            existingRecord = await _store.TryGetRecordAsync(tenantId, idempotencyKey, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Fail-open: if store is unreachable, process as new request
            _logger.LogWarning(ex, "Idempotency store read failed for key {IdempotencyKey}. Processing as new request (fail-open).", idempotencyKey);
            await next();
            return;
        }

        if (existingRecord is not null)
        {
            if (!existingRecord.IsCompleted)
            {
                // In-flight — another request is currently processing this key
                context.Result = CreateProblemResult(
                    StatusCodes.Status409Conflict,
                    "IDEMPOTENCY_KEY_IN_FLIGHT",
                    "A request with this idempotency key is already being processed.");
                return;
            }

            // Completed — validate fingerprint if enabled
            if (_options.ValidateRequestFingerprint)
            {
                var currentFingerprint = await _fingerprintComputer.ComputeAsync(httpContext, httpContext.RequestAborted);
                if (!string.Equals(existingRecord.Fingerprint, currentFingerprint, StringComparison.Ordinal))
                {
                    context.Result = CreateProblemResult(
                        StatusCodes.Status422UnprocessableEntity,
                        "IDEMPOTENCY_KEY_REUSE",
                        "This idempotency key was used for a different request.");
                    return;
                }
            }

            // Replay the cached response
            await ReplayCachedResponseAsync(httpContext, existingRecord);
            context.Result = new EmptyResult(); // Response already written
            return;
        }

        // No existing record — compute fingerprint and create in-flight record
        var fingerprint = await _fingerprintComputer.ComputeAsync(httpContext, httpContext.RequestAborted);

        bool created;
        try
        {
            created = await _store.CreateInFlightRecordAsync(tenantId, idempotencyKey, fingerprint, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Fail-open: if store write fails, process normally
            _logger.LogWarning(ex, "Idempotency store write failed for key {IdempotencyKey}. Processing without idempotency protection (fail-open).", idempotencyKey);
            await next();
            return;
        }

        if (!created)
        {
            // Race condition: another request claimed the key between our read and write
            context.Result = CreateProblemResult(
                StatusCodes.Status409Conflict,
                "IDEMPOTENCY_KEY_IN_FLIGHT",
                "A request with this idempotency key is already being processed.");
            return;
        }

        // Execute the action
        var executedContext = await next();

        if (executedContext.Exception is not null)
        {
            // Action threw — delete the in-flight record to allow retry
            try
            {
                await _store.DeleteRecordAsync(tenantId, idempotencyKey, CancellationToken.None);
            }
            catch (Exception deleteEx)
            {
                _logger.LogError(deleteEx, "Failed to delete in-flight idempotency record for key {IdempotencyKey} after endpoint exception.", idempotencyKey);
            }

            return; // Let the exception propagate
        }

        // Store the completed response
        try
        {
            var record = await CaptureResponseAsync(httpContext, idempotencyKey, tenantId, fingerprint);
            await _store.CompleteRecordAsync(tenantId, idempotencyKey, record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store completed idempotency record for key {IdempotencyKey}.", idempotencyKey);
            // Response already sent to client — nothing we can do
        }
    }

    private string ResolveTenantId()
    {
        if (!_options.EnableTenantIsolation)
        {
            return "__global__";
        }

        if (_tenantContext?.IsResolved == true)
        {
            return _tenantContext.TenantId.Value.ToString();
        }

        return "__global__";
    }

    private static async Task ReplayCachedResponseAsync(HttpContext httpContext, IdempotencyRecord record)
    {
        httpContext.Response.StatusCode = record.StatusCode ?? StatusCodes.Status200OK;
        httpContext.Response.Headers["Idempotency-Replayed"] = "true";

        // Restore headers
        if (!string.IsNullOrEmpty(record.ResponseHeaders))
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(record.ResponseHeaders);
            if (headers is not null)
            {
                foreach (var (key, values) in headers)
                {
                    if (!IsHopByHopHeader(key))
                    {
                        httpContext.Response.Headers[key] = values;
                    }
                }
            }
        }

        // Restore body
        if (!string.IsNullOrEmpty(record.ResponseBody))
        {
            var bodyBytes = Convert.FromBase64String(record.ResponseBody);
            await httpContext.Response.Body.WriteAsync(bodyBytes);
        }
    }

    private async Task<IdempotencyRecord> CaptureResponseAsync(
        HttpContext httpContext,
        string idempotencyKey,
        string tenantId,
        string fingerprint)
    {
        var compositeKey = $"{tenantId}#{idempotencyKey}";
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.TtlMinutes).ToUnixTimeSeconds();

        // Capture response headers (excluding hop-by-hop)
        var headers = httpContext.Response.Headers
            .Where(h => !IsHopByHopHeader(h.Key))
            .ToDictionary(h => h.Key, h => h.Value.ToArray());
        var headersJson = JsonSerializer.Serialize(headers);

        // Capture response body (if buffered)
        string? bodyBase64 = null;
        if (httpContext.Response.Body.CanSeek)
        {
            httpContext.Response.Body.Position = 0;
            using var ms = new MemoryStream();
            await httpContext.Response.Body.CopyToAsync(ms);
            bodyBase64 = Convert.ToBase64String(ms.ToArray());
            httpContext.Response.Body.Position = 0;
        }

        return new IdempotencyRecord
        {
            IdempotencyKey = idempotencyKey,
            CompositeKey = compositeKey,
            Fingerprint = fingerprint,
            IsCompleted = true,
            StatusCode = httpContext.Response.StatusCode,
            ResponseHeaders = headersJson,
            ResponseBody = bodyBase64,
            CreatedAt = now,
            ExpiresAtEpoch = expiresAt
        };
    }

    private static ObjectResult CreateProblemResult(int statusCode, string errorCode, string detail)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://pervaxis.io/problems/idempotency/{errorCode.ToLowerInvariant()}",
            Title = "Idempotency Error",
            Status = statusCode,
            Detail = detail
        };
        problemDetails.Extensions["errorCode"] = errorCode;

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    private static bool IsHopByHopHeader(string headerName)
    {
        return headerName.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("TE", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
            || headerName.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
    }
}
