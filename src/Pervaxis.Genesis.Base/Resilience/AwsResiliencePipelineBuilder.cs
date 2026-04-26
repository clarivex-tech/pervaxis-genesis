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

using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Pervaxis.Genesis.Base.Options;

namespace Pervaxis.Genesis.Base.Resilience;

/// <summary>
/// Builds Polly v8 resilience pipelines for Genesis AWS providers.
/// Handles network errors, timeouts, and I/O exceptions common to cloud services.
/// AWS-specific exception handling (throttling, service errors) is done via Polly's
/// built-in exception predicates without requiring AWSSDK references.
/// </summary>
public static class GenesisResiliencePipelineBuilder
{
    /// <summary>
    /// Builds a resilience pipeline with retry, circuit breaker, and timeout strategies
    /// configured for cloud provider transient error handling.
    /// </summary>
    /// <param name="options">Resilience configuration options.</param>
    /// <param name="logger">Logger for resilience events.</param>
    /// <param name="pipelineName">Name of the pipeline for logging context.</param>
    /// <returns>A configured resilience pipeline.</returns>
    public static ResiliencePipeline BuildPipeline(
        ResilienceOptions options,
        ILogger logger,
        string pipelineName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);

        if (!options.Enabled)
        {
            // Return empty pipeline if resilience is disabled
            return ResiliencePipeline.Empty;
        }

        var builder = new ResiliencePipelineBuilder();

        // Add retry strategy for transient errors
        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = options.RetryCount,
            Delay = TimeSpan.FromMilliseconds(options.RetryDelayMs),
            MaxDelay = TimeSpan.FromMilliseconds(options.MaxRetryDelayMs),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransientError),
            OnRetry = args =>
            {
                logger.LogWarning(
                    "Resilience: Retry attempt {Attempt} of {Max} for {Pipeline} after {Delay:g}. " +
                    "Exception: {ExceptionType} - {Message}",
                    args.AttemptNumber + 1,
                    options.RetryCount,
                    pipelineName,
                    args.RetryDelay,
                    args.Outcome.Exception?.GetType().Name ?? "Unknown",
                    args.Outcome.Exception?.Message ?? "No message");

                return ValueTask.CompletedTask;
            }
        });

        // Add circuit breaker to prevent cascading failures
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = options.CircuitBreakerFailureThreshold,
            SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingDurationSeconds),
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds),
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            OnOpened = args =>
            {
                logger.LogError(
                    args.Outcome.Exception,
                    "Resilience: Circuit breaker opened for {Pipeline}. " +
                    "BreakDuration={BreakDuration:g}. Too many failures detected.",
                    pipelineName,
                    args.BreakDuration);

                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                logger.LogInformation(
                    "Resilience: Circuit breaker closed for {Pipeline}. Calls flowing normally.",
                    pipelineName);

                return ValueTask.CompletedTask;
            },
            OnHalfOpened = _ =>
            {
                logger.LogInformation(
                    "Resilience: Circuit breaker half-open for {Pipeline}. Probing with single call.",
                    pipelineName);

                return ValueTask.CompletedTask;
            }
        });

        // Add timeout strategy
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            OnTimeout = args =>
            {
                logger.LogWarning(
                    "Resilience: Operation timeout for {Pipeline} after {Timeout:g}",
                    pipelineName,
                    args.Timeout);

                return ValueTask.CompletedTask;
            }
        });

        return builder.Build();
    }

    /// <summary>
    /// Determines if an exception represents a transient error that should be retried.
    /// Checks for network errors, I/O exceptions, timeouts, and AWS SDK exceptions
    /// without requiring compile-time dependency on AWSSDK assemblies.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>True if the error is transient and should be retried.</returns>
    private static bool IsTransientError(Exception exception)
    {
        // Network and I/O exceptions (always transient)
        if (exception is HttpRequestException
            or SocketException
            or IOException
            or TimeoutException)
        {
            return true;
        }

        // Task cancellation due to timeout
        if (exception is TaskCanceledException { InnerException: TimeoutException })
        {
            return true;
        }

        // Check for AWS SDK exceptions by type name (duck typing to avoid AWSSDK dependency)
        // This allows Genesis.Base to remain AWS-agnostic while still handling AWS errors
        var exceptionTypeName = exception.GetType().Name;

        if (exceptionTypeName.Contains("AmazonServiceException", StringComparison.Ordinal))
        {
            // Check HTTP status code via reflection for throttling/service errors
            var statusCodeProperty = exception.GetType().GetProperty("StatusCode");
            if (statusCodeProperty?.GetValue(exception) is int statusCode)
            {
                // Retry on 429 (throttling), 500, 503 (service unavailable), 502, 504
                if (statusCode is 429 or >= 500 and < 600)
                {
                    return true;
                }
            }

            // Check error code for AWS-specific transient errors
            var errorCodeProperty = exception.GetType().GetProperty("ErrorCode");
            if (errorCodeProperty?.GetValue(exception) is string errorCode)
            {
                var errorCodeUpper = errorCode.ToUpperInvariant();
                return errorCodeUpper is "THROTTLINGEXCEPTION"
                    or "THROTTLING"
                    or "TOOMANYREQUESTSEXCEPTION"
                    or "REQUESTLIMITEXCEEDED"
                    or "PROVISIONEDTHROUGHPUTEXCEEDEDEXCEPTION"
                    or "SLOWDOWN"
                    or "REQUESTTIMEOUT"
                    or "SERVICEUNAVAILABLE"
                    or "INTERNALSERVERERROR"
                    or "TEMPORARYREDIRECT";
            }
        }

        return false;
    }
}

