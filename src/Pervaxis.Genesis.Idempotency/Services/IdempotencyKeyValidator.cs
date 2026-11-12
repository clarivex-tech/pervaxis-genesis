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

using System.Text.RegularExpressions;

namespace Pervaxis.Genesis.Idempotency.Services;

/// <summary>
/// Validates idempotency key format and constraints.
/// Allowed: 1-256 characters, alphanumeric + hyphens + underscores + periods.
/// </summary>
internal sealed partial class IdempotencyKeyValidator : IIdempotencyKeyValidator
{
    private const int MaxKeyLength = 256;

    [GeneratedRegex(@"^[a-zA-Z0-9\-_.]+$", RegexOptions.Compiled)]
    private static partial Regex AllowedCharactersRegex();

    /// <inheritdoc/>
    public IdempotencyKeyValidationResult Validate(string? keyValue, bool hasMultipleValues)
    {
        if (hasMultipleValues)
        {
            return new IdempotencyKeyValidationResult(
                false,
                "IDEMPOTENCY_KEY_INVALID",
                "The Idempotency-Key header must contain exactly one value.");
        }

        if (string.IsNullOrWhiteSpace(keyValue))
        {
            return new IdempotencyKeyValidationResult(
                false,
                "IDEMPOTENCY_KEY_INVALID",
                "The Idempotency-Key header value must not be empty or whitespace.");
        }

        if (keyValue.Length > MaxKeyLength)
        {
            return new IdempotencyKeyValidationResult(
                false,
                "IDEMPOTENCY_KEY_INVALID",
                $"The Idempotency-Key value must not exceed {MaxKeyLength} characters.");
        }

        if (!AllowedCharactersRegex().IsMatch(keyValue))
        {
            return new IdempotencyKeyValidationResult(
                false,
                "IDEMPOTENCY_KEY_INVALID",
                "The Idempotency-Key value must contain only alphanumeric characters, hyphens, underscores, and periods.");
        }

        return new IdempotencyKeyValidationResult(true, null, null);
    }
}
