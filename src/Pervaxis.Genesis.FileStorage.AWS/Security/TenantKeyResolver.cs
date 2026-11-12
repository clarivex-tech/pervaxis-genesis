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

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Validation;

namespace Pervaxis.Genesis.FileStorage.AWS.Security;

/// <summary>
/// Resolves and validates S3 keys for tenant-scoped file storage operations.
/// Phase 1: Application-enforced tenant isolation with fail-closed behavior.
/// </summary>
/// <remarks>
/// Key format: tenants/{tenantId}/{user-key}
/// 
/// Security features:
/// - Validates tenant context is resolved before building keys
/// - Rejects path traversal (../, ..\, %2e%2e, etc.)
/// - Rejects control characters and dangerous patterns
/// - Validates tenant ID format using shared TenantIdValidator
/// - Enforces AWS S3 key length limits (1024 bytes)
/// - Strips tenant prefix when returning keys to callers
/// - Audit logs all validation failures
/// </remarks>
public sealed class TenantKeyResolver
{
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<TenantKeyResolver> _logger;
    private readonly bool _enableTenantIsolation;

    // Dangerous patterns that could bypass validation
    private static readonly Regex PathTraversalRegex = new(
        @"(\.\./|\.\.\\|%2e%2e%2f|%2e%2e%5c|%252e%252e%252f|%252e%252e%255c)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ControlCharacterRegex = new(
        @"[\x00-\x1f\x7f]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeadingSlashRegex = new(
        @"^\/+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MultipleSlashesRegex = new(
        @"\/\/+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmptySegmentRegex = new(
        @"(^\/|\/\/|\/\.|\/\.\.|\/\.$|\/\.\.\/$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantKeyResolver"/> class.
    /// </summary>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="enableTenantIsolation">Whether tenant isolation is enabled.</param>
    public TenantKeyResolver(
        ITenantContext? tenantContext,
        ILogger<TenantKeyResolver> logger,
        bool enableTenantIsolation = true)
    {
        _tenantContext = tenantContext;
        _logger = logger;
        _enableTenantIsolation = enableTenantIsolation;
    }

    /// <summary>
    /// Resolves a user-provided key to a full S3 key with tenant prefix.
    /// </summary>
    /// <param name="userKey">The user-provided key (without tenant prefix).</param>
    /// <returns>Full S3 key in format: tenants/{tenantId}/{userKey}</returns>
    /// <exception cref="GenesisException">Thrown when tenant context is not resolved or key validation fails.</exception>
    public string ResolveKey(string userKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey, nameof(userKey));

        // Phase 1: Fail-closed - tenant isolation must be enabled and context must be resolved
        if (_enableTenantIsolation)
        {
            if (_tenantContext is null || !_tenantContext.IsResolved)
            {
                _logger.LogError(
                    "Tenant context not resolved for S3 operation. EnableTenantIsolation={Enabled}, TenantContext={Context}",
                    _enableTenantIsolation,
                    _tenantContext is null ? "null" : "not-resolved");

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    "Tenant context not resolved. Cannot perform S3 operations without tenant context.",
                    errorCode: FileStorageConstants.FailureReasons.TenantContextNotResolved);
            }

            // Validate tenant ID format
            var tenantId = _tenantContext.TenantId.Value;
            var validationResult = TenantIdValidator.Validate(tenantId);
            if (!validationResult.IsValid)
            {
                _logger.LogError(
                    "Invalid tenant ID format: {TenantId}. Reason: {Reason}",
                    tenantId,
                    validationResult.ErrorMessage);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    $"Invalid tenant ID format: {validationResult.ErrorMessage}",
                    errorCode: FileStorageConstants.FailureReasons.InvalidTenantId);
            }

            // Normalize and validate the user key
            var normalizedKey = NormalizeKey(userKey);
            ValidateKeyFormat(normalizedKey);

            // Build full key: tenants/{tenantId}/{normalizedKey}
            var fullKey = string.Format(
                FileStorageConstants.TenantKeyPrefixFormat,
                tenantId) + normalizedKey;

            // Final verification: ensure the key starts with the expected prefix
            var expectedPrefix = string.Format(FileStorageConstants.TenantKeyPrefixFormat, tenantId);
            if (!fullKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Key construction failed. Expected prefix: {ExpectedPrefix}, Actual key: {ActualKey}",
                    expectedPrefix,
                    fullKey);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    "Key construction validation failed",
                    errorCode: FileStorageConstants.FailureReasons.InvalidKeyFormat);
            }

            // Validate final key length (AWS S3 limit: 1024 bytes)
            ValidateKeyLength(fullKey);

            _logger.LogDebug(
                "Resolved user key {UserKey} to full S3 key {FullKey} for tenant {TenantId}",
                userKey,
                fullKey,
                tenantId);

            return fullKey;
        }

        // Tenant isolation disabled (only allowed in Development/Test)
        var nonTenantKey = NormalizeKey(userKey);
        ValidateKeyFormat(nonTenantKey);
        ValidateKeyLength(nonTenantKey);

        _logger.LogDebug(
            "Tenant isolation disabled. Using non-tenant key: {Key}",
            nonTenantKey);

        return nonTenantKey;
    }

    /// <summary>
    /// Resolves a user-provided prefix for list operations.
    /// Handles empty, null, and path traversal attempts in prefixes.
    /// </summary>
    /// <param name="userPrefix">Optional user-provided prefix.</param>
    /// <returns>Full S3 prefix scoped to tenant.</returns>
    public string ResolvePrefix(string? userPrefix)
    {
        if (_enableTenantIsolation)
        {
            if (_tenantContext is null || !_tenantContext.IsResolved)
            {
                _logger.LogError("Tenant context not resolved for S3 list operation");

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    "Tenant context not resolved. Cannot perform S3 list operations without tenant context.",
                    errorCode: FileStorageConstants.FailureReasons.TenantContextNotResolved);
            }

            var tenantId = _tenantContext.TenantId.Value;
            
            // Validate tenant ID
            TenantIdValidator.ThrowIfInvalid(tenantId);

            // Base tenant prefix
            var tenantPrefix = string.Format(FileStorageConstants.TenantKeyPrefixFormat, tenantId);

            // If no user prefix, return just the tenant prefix
            if (string.IsNullOrWhiteSpace(userPrefix))
            {
                _logger.LogDebug(
                    "List operation with empty prefix, using tenant prefix: {Prefix}",
                    tenantPrefix);
                return tenantPrefix;
            }

            // Validate and normalize the user prefix
            var normalizedPrefix = NormalizeKey(userPrefix);
            
            // Don't validate as a full key - prefixes can be partial
            // But still check for traversal attempts
            if (PathTraversalRegex.IsMatch(normalizedPrefix))
            {
                _logger.LogWarning(
                    "Path traversal attempt detected in prefix: {Prefix}",
                    userPrefix);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    $"Invalid prefix: path traversal detected in '{userPrefix}'",
                    errorCode: FileStorageConstants.FailureReasons.KeyPathTraversal);
            }

            var fullPrefix = tenantPrefix + normalizedPrefix;

            // Ensure the final prefix is still within the tenant scope
            if (!fullPrefix.StartsWith(tenantPrefix, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Prefix construction failed. Expected to start with: {TenantPrefix}, Got: {FullPrefix}",
                    tenantPrefix,
                    fullPrefix);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    "Prefix construction validation failed - attempted escape from tenant scope",
                    errorCode: FileStorageConstants.FailureReasons.KeyPathTraversal);
            }

            _logger.LogDebug(
                "Resolved prefix {UserPrefix} to {FullPrefix} for tenant {TenantId}",
                userPrefix,
                fullPrefix,
                tenantId);

            return fullPrefix;
        }

        // Tenant isolation disabled
        if (string.IsNullOrWhiteSpace(userPrefix))
        {
            return string.Empty;
        }

        var nonTenantPrefix = NormalizeKey(userPrefix);
        return nonTenantPrefix;
    }

    /// <summary>
    /// Strips the tenant prefix from a full S3 key, returning only the user-visible portion.
    /// </summary>
    /// <param name="fullKey">The full S3 key with tenant prefix.</param>
    /// <returns>User-visible key without tenant prefix.</returns>
    public string StripTenantPrefix(string fullKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullKey, nameof(fullKey));

        if (!_enableTenantIsolation)
        {
            return fullKey;
        }

        if (_tenantContext is null || !_tenantContext.IsResolved)
        {
            // This shouldn't happen if keys were created through ResolveKey,
            // but handle defensively
            _logger.LogWarning(
                "Attempting to strip tenant prefix without resolved tenant context. Returning full key.");
            return fullKey;
        }

        var tenantId = _tenantContext.TenantId.Value;
        var expectedPrefix = string.Format(FileStorageConstants.TenantKeyPrefixFormat, tenantId);

        if (fullKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            var userKey = fullKey[expectedPrefix.Length..];
            
            _logger.LogDebug(
                "Stripped tenant prefix from {FullKey}, returning {UserKey}",
                fullKey,
                userKey);
            
            return userKey;
        }

        // Key doesn't have expected prefix - log warning and return as-is
        _logger.LogWarning(
            "Full key {FullKey} does not start with expected tenant prefix {ExpectedPrefix}. Returning as-is.",
            fullKey,
            expectedPrefix);

        return fullKey;
    }

    /// <summary>
    /// Gets the current tenant ID from context.
    /// </summary>
    /// <returns>Tenant ID string.</returns>
    /// <exception cref="GenesisException">Thrown when tenant context is not resolved.</exception>
    public string GetTenantId()
    {
        if (!_enableTenantIsolation)
        {
            throw new GenesisException(
                nameof(TenantKeyResolver),
                "Cannot get tenant ID when tenant isolation is disabled");
        }

        if (_tenantContext is null || !_tenantContext.IsResolved)
        {
            throw new GenesisException(
                nameof(TenantKeyResolver),
                "Tenant context not resolved",
                errorCode: FileStorageConstants.FailureReasons.TenantContextNotResolved);
        }

        return _tenantContext.TenantId.Value;
    }

    /// <summary>
    /// Normalizes an S3 key by:
    /// - Converting backslashes to forward slashes
    /// - Removing leading slashes
    /// - Collapsing multiple consecutive slashes
    /// </summary>
    private static string NormalizeKey(string key)
    {
        // Convert backslashes to forward slashes
        var normalized = key.Replace("\\", "/", StringComparison.Ordinal);

        // Remove leading slashes
        normalized = LeadingSlashRegex.Replace(normalized, string.Empty);

        // Collapse multiple slashes to single slash
        normalized = MultipleSlashesRegex.Replace(normalized, "/");

        return normalized;
    }

    /// <summary>
    /// Validates key format for security issues.
    /// </summary>
    private void ValidateKeyFormat(string key)
    {
        // Check for path traversal patterns (including URL-encoded variants)
        if (PathTraversalRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Path traversal attempt detected in key: {Key}",
                key);

            throw new GenesisException(
                nameof(TenantKeyResolver),
                $"Invalid key format: path traversal detected in '{key}'",
                errorCode: FileStorageConstants.FailureReasons.KeyPathTraversal);
        }

        // Check for control characters
        if (ControlCharacterRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Control characters detected in key: {Key}",
                key);

            throw new GenesisException(
                nameof(TenantKeyResolver),
                $"Invalid key format: control characters detected in '{key}'",
                errorCode: FileStorageConstants.FailureReasons.InvalidKeyFormat);
        }

        // Check for empty segments (e.g., //, /./, /../)
        if (EmptySegmentRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Empty segments detected in key: {Key}",
                key);

            throw new GenesisException(
                nameof(TenantKeyResolver),
                $"Invalid key format: empty segments detected in '{key}'",
                errorCode: FileStorageConstants.FailureReasons.InvalidKeyFormat);
        }

        // Check for single or double dots as standalone segments
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                _logger.LogWarning(
                    "Dot segments detected in key: {Key}",
                    key);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    $"Invalid key format: relative path segments detected in '{key}'",
                    errorCode: FileStorageConstants.FailureReasons.KeyPathTraversal);
            }
        }
    }

    /// <summary>
    /// Validates that the key length doesn't exceed AWS S3 limits.
    /// </summary>
    private void ValidateKeyLength(string key)
    {
        var keyBytes = Encoding.UTF8.GetByteCount(key);
        if (keyBytes > FileStorageConstants.MaxKeyLengthBytes)
        {
            _logger.LogWarning(
                "Key exceeds maximum length. Key: {Key}, Length: {Length} bytes, Max: {Max} bytes",
                key,
                keyBytes,
                FileStorageConstants.MaxKeyLengthBytes);

            throw new GenesisException(
                nameof(TenantKeyResolver),
                $"Key exceeds maximum length of {FileStorageConstants.MaxKeyLengthBytes} bytes (got {keyBytes} bytes)",
                errorCode: FileStorageConstants.FailureReasons.KeyTooLong);
        }
    }
}
