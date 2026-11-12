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

namespace Pervaxis.Genesis.Idempotency.Services;

/// <summary>
/// Result of idempotency key validation.
/// </summary>
/// <param name="IsValid">Whether the key is valid.</param>
/// <param name="ErrorCode">Error code when invalid (null when valid).</param>
/// <param name="ErrorMessage">Human-readable error message when invalid (null when valid).</param>
public readonly record struct IdempotencyKeyValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage);
