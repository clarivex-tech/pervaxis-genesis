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

namespace Pervaxis.Genesis.Base.Options;

/// <summary>
/// Resilience policy configuration for Genesis AWS providers.
/// Configures retry, circuit breaker, and timeout strategies for handling transient AWS failures.
/// </summary>
public sealed class ResilienceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether resilience policies are enabled.
    /// When false, all operations execute directly without retry/circuit breaker/timeout.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures.
    /// Default is 3 retries (4 total attempts including the initial call).
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay in milliseconds for exponential backoff retry.
    /// Actual delay is calculated as: baseDelay * (2 ^ attemptNumber) with jitter.
    /// Default is 1000ms (1 second).
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum retry delay in milliseconds.
    /// Caps the exponential backoff delay to prevent excessively long waits.
    /// Default is 30000ms (30 seconds).
    /// </summary>
    public int MaxRetryDelayMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets the failure ratio threshold (0.0 to 1.0) that opens the circuit breaker.
    /// Default is 0.5 (50% failure rate triggers circuit breaker).
    /// </summary>
    public double CircuitBreakerFailureThreshold { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the minimum number of calls required before the circuit breaker can trip.
    /// Prevents opening on a single failure during low-traffic periods.
    /// Default is 10 calls.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Gets or sets the duration in seconds to keep the circuit breaker open before attempting recovery.
    /// Default is 60 seconds.
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the sampling window in seconds for measuring failure ratio.
    /// Default is 30 seconds.
    /// </summary>
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the operation timeout in seconds.
    /// Applied per operation attempt (not cumulative across retries).
    /// Default is 30 seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Validates the resilience options configuration.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public bool Validate()
    {
        if (RetryCount < 0)
        {
            return false;
        }

        if (RetryDelayMs <= 0)
        {
            return false;
        }

        if (MaxRetryDelayMs < RetryDelayMs)
        {
            return false;
        }

        if (CircuitBreakerFailureThreshold is < 0.0 or > 1.0)
        {
            return false;
        }

        if (CircuitBreakerMinimumThroughput < 1)
        {
            return false;
        }

        if (CircuitBreakerDurationSeconds <= 0)
        {
            return false;
        }

        if (CircuitBreakerSamplingDurationSeconds <= 0)
        {
            return false;
        }

        if (TimeoutSeconds <= 0)
        {
            return false;
        }

        return true;
    }
}
