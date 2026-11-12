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

namespace Pervaxis.Genesis.Idempotency.Diagnostics;

/// <summary>
/// Source-generated log messages for the Genesis Idempotency module.
/// Uses LoggerMessage source generation for zero-allocation logging.
/// </summary>
internal static partial class IdempotencyLogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Replaying cached response for idempotency key {IdempotencyKey} (status={StatusCode}).")]
    internal static partial void LogResponseReplayed(
        ILogger logger, string idempotencyKey, int statusCode);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Created in-flight idempotency record for key {IdempotencyKey}.")]
    internal static partial void LogInFlightCreated(
        ILogger logger, string idempotencyKey);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Completed idempotency record for key {IdempotencyKey} (status={StatusCode}).")]
    internal static partial void LogRecordCompleted(
        ILogger logger, string idempotencyKey, int statusCode);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Idempotency store read failed for key {IdempotencyKey}. Processing as new request (fail-open).")]
    internal static partial void LogStoreReadFailed(
        ILogger logger, Exception exception, string idempotencyKey);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Idempotency store write failed for key {IdempotencyKey}. Processing without idempotency protection (fail-open).")]
    internal static partial void LogStoreWriteFailed(
        ILogger logger, Exception exception, string idempotencyKey);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "In-flight conflict for idempotency key {IdempotencyKey}. Returning 409.")]
    internal static partial void LogConflictDetected(
        ILogger logger, string idempotencyKey);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Fingerprint mismatch for idempotency key {IdempotencyKey}. Key reused for different request. Returning 422.")]
    internal static partial void LogFingerprintMismatch(
        ILogger logger, string idempotencyKey);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "Failed to delete in-flight idempotency record for key {IdempotencyKey} after endpoint exception.")]
    internal static partial void LogDeleteAfterExceptionFailed(
        ILogger logger, Exception exception, string idempotencyKey);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Error,
        Message = "Failed to store completed idempotency record for key {IdempotencyKey}.")]
    internal static partial void LogStoreCompleteFailed(
        ILogger logger, Exception exception, string idempotencyKey);
}
