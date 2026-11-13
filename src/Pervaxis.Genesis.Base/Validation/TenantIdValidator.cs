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

namespace Pervaxis.Genesis.Base.Validation;

/// <summary>
/// Validates tenant identifier format for use in AWS resource names, S3 keys, cache keys, and message attributes.
/// Ensures tenant IDs are safe for downstream systems and prevent injection attacks.
/// </summary>
/// <remarks>
/// Rules:
/// - Lowercase letters, digits, and hyphens only
/// - Must start with a letter OR digit (supports GUIDs from Cognito)
/// - Must end with a letter or digit (not hyphen)
/// - Length: 3-63 characters (AWS naming constraints)
/// - No consecutive hyphens (..)
/// - Reserved words rejected: platform, tenant, admin, system, root, default
/// - Reserved substrings rejected: -tenant-, -admin-, -system-, -platform-, -root-
///   (hyphens on both sides, so a suffix such as "acme-platform" is allowed)
/// 
/// Note: This validator supports both GUID-based tenant IDs from Cognito (e.g., 3f2504e0-4f89-41d3-9a0c-0305e82c3301)
/// and human-readable IDs (e.g., acme-corp).
/// </remarks>
public static class TenantIdValidator
{
    private const int MinLength = 3;
    private const int MaxLength = 63;
    
    // Lowercase alphanumeric + hyphens, start with letter OR digit, end with letter/digit
    private static readonly Regex ValidFormatRegex = new(
        @"^[a-z0-9][a-z0-9-]*[a-z0-9]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ConsecutiveHyphensRegex = new(
        @"--",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "platform",
        "tenant",
        "admin",
        "system",
        "root",
        "default",
        "null",
        "undefined"
    };

    private static readonly string[] ReservedSubstrings =
    {
        "-tenant-",
        "-admin-",
        "-system-",
        "-platform-",
        "-root-"
    };

    /// <summary>
    /// Validates a tenant identifier according to platform rules.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to validate.</param>
    /// <returns>Validation result with success flag and error message.</returns>
    public static ValidationResult Validate(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ValidationResult.Failure("Tenant ID cannot be null or empty");
        }

        // Length check
        if (tenantId.Length < MinLength)
        {
            return ValidationResult.Failure($"Tenant ID must be at least {MinLength} characters (got {tenantId.Length})");
        }

        if (tenantId.Length > MaxLength)
        {
            return ValidationResult.Failure($"Tenant ID must not exceed {MaxLength} characters (got {tenantId.Length})");
        }

        // Format check: lowercase alphanumeric + hyphens, start with letter/digit, end with letter/digit
        if (!ValidFormatRegex.IsMatch(tenantId))
        {
            return ValidationResult.Failure(
                "Tenant ID must contain only lowercase letters, digits, and hyphens; " +
                "must start and end with a letter or digit");
        }

        // No consecutive hyphens
        if (ConsecutiveHyphensRegex.IsMatch(tenantId))
        {
            return ValidationResult.Failure("Tenant ID cannot contain consecutive hyphens (--)");
        }

        // Reserved words check
        if (ReservedWords.Contains(tenantId))
        {
            return ValidationResult.Failure($"Tenant ID '{tenantId}' is a reserved word");
        }

        // Reserved substrings check
        foreach (var reserved in ReservedSubstrings)
        {
            if (tenantId.Contains(reserved, StringComparison.Ordinal))
            {
                return ValidationResult.Failure($"Tenant ID cannot contain reserved substring '{reserved}'");
            }
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Checks if a tenant identifier is valid.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValid(string? tenantId) => Validate(tenantId).IsValid;

    /// <summary>
    /// Validates a tenant identifier and throws if invalid.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to validate.</param>
    /// <param name="paramName">Parameter name for exception message.</param>
    /// <exception cref="ArgumentException">Thrown when tenant ID is invalid.</exception>
    public static void ThrowIfInvalid(string? tenantId, string paramName = "tenantId")
    {
        var result = Validate(tenantId);
        if (!result.IsValid)
        {
            throw new ArgumentException(result.ErrorMessage, paramName);
        }
    }
}

/// <summary>
/// Validation result with success flag and optional error message.
/// </summary>
public readonly struct ValidationResult : IEquatable<ValidationResult>
{
    /// <summary>
    /// Gets a value indicating whether the validation succeeded.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    private ValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new(true, null);

    /// <summary>
    /// Creates a failed validation result with an error message.
    /// </summary>
    public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);

    /// <inheritdoc />
    public bool Equals(ValidationResult other) =>
        IsValid == other.IsValid && string.Equals(ErrorMessage, other.ErrorMessage, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValidationResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsValid, ErrorMessage);

    /// <summary>Determines whether two results are equal.</summary>
    public static bool operator ==(ValidationResult left, ValidationResult right) => left.Equals(right);

    /// <summary>Determines whether two results are not equal.</summary>
    public static bool operator !=(ValidationResult left, ValidationResult right) => !left.Equals(right);
}
