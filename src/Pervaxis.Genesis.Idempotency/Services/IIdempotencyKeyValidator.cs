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
/// Validates idempotency key format and constraints.
/// Allowed: 1-256 characters, alphanumeric + hyphens + underscores + periods.
/// </summary>
public interface IIdempotencyKeyValidator
{
    /// <summary>
    /// Validates the key value and returns a validation result.
    /// </summary>
    /// <param name="keyValue">The key value to validate (may be null).</param>
    /// <param name="hasMultipleValues">Whether the header contained multiple values.</param>
    /// <returns>Validation result indicating success or failure with error details.</returns>
    IdempotencyKeyValidationResult Validate(string? keyValue, bool hasMultipleValues);
}
