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

namespace Pervaxis.Genesis.OData.Services;

/// <summary>
/// Result of OData query validation.
/// </summary>
/// <param name="IsValid">Whether the query passed validation.</param>
/// <param name="ErrorCode">Error code when invalid (null when valid).</param>
/// <param name="ErrorMessage">Human-readable error message when invalid.</param>
public readonly record struct QueryValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage)
{
    /// <summary>Creates a successful validation result.</summary>
    public static QueryValidationResult Passed() => new(true, null, null);

    /// <summary>Creates a failed validation result.</summary>
    public static QueryValidationResult Failed(string errorCode, string errorMessage)
        => new(false, errorCode, errorMessage);
}
