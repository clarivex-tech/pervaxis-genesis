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

namespace Pervaxis.Genesis.Base.Results;

/// <summary>
/// Represents the result of a provider operation with success/failure status and optional data.
/// </summary>
/// <typeparam name="T">The type of data returned on success.</typeparam>
public sealed class ProviderResult<T>
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the result data if the operation succeeded.
    /// </summary>
    public T? Data { get; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets the exception if the operation failed with an exception.
    /// </summary>
    public Exception? Exception { get; }

    private ProviderResult(bool isSuccess, T? data, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Data = data;
        Error = error;
        Exception = exception;
    }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The result data.</param>
    /// <returns>A successful result.</returns>
    public static ProviderResult<T> Success(T data)
        => new(true, data, null, null);

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed result.</returns>
    public static ProviderResult<T> Failure(string error)
        => new(false, default, error, null);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed result.</returns>
    public static ProviderResult<T> Failure(string error, Exception exception)
        => new(false, default, error, exception);
}

/// <summary>
/// Represents the result of a provider operation without return data.
/// </summary>
public sealed class ProviderResult
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets the exception if the operation failed with an exception.
    /// </summary>
    public Exception? Exception { get; }

    private ProviderResult(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static ProviderResult Success()
        => new(true, null, null);

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed result.</returns>
    public static ProviderResult Failure(string error)
        => new(false, error, null);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A failed result.</returns>
    public static ProviderResult Failure(string error, Exception exception)
        => new(false, error, exception);
}
