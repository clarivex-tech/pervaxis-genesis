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

using Microsoft.Extensions.Logging;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Diagnostics;

/// <summary>
/// Source-generated log messages for the transactional logging module.
/// Uses LoggerMessage attributes to minimize allocation overhead.
/// </summary>
internal static partial class TransactionLogLogMessages
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Transaction context created: {TransactionId}, TraceId={TraceId}, TenantId={TenantId}, Path={RequestPath}")]
    public static partial void TransactionContextCreated(
        ILogger logger, string transactionId, string? traceId, string? tenantId, string? requestPath);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Transaction context finalized: {TransactionId}, Entries={EntryCount}, Duration={DurationMs:F1}ms, Status={Status}")]
    public static partial void TransactionContextFinalized(
        ILogger logger, string transactionId, int entryCount, double durationMs, string status);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to persist transaction log {TransactionId}: {ErrorMessage}. Retry={WillRetry}")]
    public static partial void PersistenceFailed(
        ILogger logger, string transactionId, string errorMessage, bool willRetry);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Transaction log {TransactionId} overflowed to S3: {EntryCount} entries, S3Key={S3Key}")]
    public static partial void S3Overflow(
        ILogger logger, string transactionId, int entryCount, string s3Key);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Transaction log circuit breaker opened. Persistence degraded — audit coverage reduced.")]
    public static partial void CircuitBreakerOpened(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Transaction log circuit breaker recovered. Normal persistence resumed.")]
    public static partial void CircuitBreakerRecovered(ILogger logger);
}
