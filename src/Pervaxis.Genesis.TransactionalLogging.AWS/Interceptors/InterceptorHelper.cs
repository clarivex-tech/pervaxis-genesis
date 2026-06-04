/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Core.Abstractions.Genesis.Modules;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Interceptors;

/// <summary>
/// Shared helper for creating TransactionLogEntry instances from interceptor data.
/// Adapts internal interceptor semantics to the NuGet TransactionLogEntry constructor.
/// </summary>
internal static class InterceptorHelper
{
    /// <summary>
    /// Creates a TransactionLogEntry using the NuGet record constructor:
    /// TransactionLogEntry(string ProviderName, string OperationName, DateTimeOffset Timestamp, TimeSpan Duration, bool Success, string? Details, string? CorrelationId)
    /// </summary>
    public static TransactionLogEntry CreateEntry(
        string providerName,
        string operationName,
        double durationMs,
        bool success,
        string? details = null,
        string? correlationId = null)
    {
        return new TransactionLogEntry(
            providerName,
            operationName,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(durationMs),
            success,
            details,
            correlationId);
    }
}
