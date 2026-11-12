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
/// - URL decodes keys until stable (max 10 iterations) to prevent encoding attacks
/// - Applies Unicode normalization (FormC) to prevent lookalike character attacks
/// - Rejects path traversal (../, ..\, %2e%2e, etc.)
/// - Rejects control characters and dangerous patterns
/// - Validates tenant ID format using shared TenantIdValidator
/// - Enforces AWS S3 key length limits (1024 bytes including tenant prefix)
/// - Strips tenant prefix when returning keys to callers
/// - Audit logs all validation failures
/// 
/// IMPORTANT NORMALIZATION BEHAVIOR:
/// All keys are normalized to Unicode FormC (canonical composition) before validation.
/// This means:
/// 1. Files stored with different Unicode normalizations (FormD, NFKC, NFKD) may not match
///    their original name when accessed through this resolver.
/// 2. If files are uploaded by systems that don't use this resolver (e.g., AWS Console,
///    direct S3 API calls, third-party tools), their keys may use different normalization.
/// 3. To ensure consistent access, ALL file operations must go through this resolver.
/// 4. Migration: If migrating existing files, normalize all keys to FormC first.
/// 
/// LITERAL % CHARACTER RESTRICTION:
/// Keys containing % followed by hex digits (e.g., "report%20final.pdf") are REJECTED.
/// This prevents data-integrity issues:
/// - Silent key rewriting: "report%20final.pdf" would become "report final.pdf" in S3
/// - Key collisions: "a%20b" and "a b" would collide
/// - Subfolder injection: "%2F" would create unexpected subdirectories
/// 
/// The resolver decodes keys to detect encoding attacks, but rejects any key where
/// decoding changes it. This ensures the key sent to S3 is exactly what the caller provided.
/// 
/// Use alternative naming:
/// - "report_20_final.pdf" or "report-20-final.pdf" instead of "report%20final.pdf"
/// - Avoid % followed by two hex digits ([0-9A-Fa-f]) in filenames
/// 
/// Phase 1 = Application-enforced isolation only. IAM policies come in Phase 2.
/// </remarks>
public sealed class TenantKeyResolver
{
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<TenantKeyResolver> _logger;
    private readonly bool _enableTenantIsolation;

    // Path traversal detection (after URL decoding and Unicode normalization)
    private static readonly Regex PathTraversalRegex = new(
        @"(\.\./|\.\.\\|\.\.(?:/|\\|$))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Control characters
    private static readonly Regex ControlCharacterRegex = new(
        @"[\x00-\x1f\x7f]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Unicode lookalike characters that could represent dots or slashes
    // Fullwidth: ／ (U+FF0F), ． (U+FF0E)
    // Other: ⁄ (U+2044 fraction slash), ∕ (U+2215 division slash)
    private static readonly Regex UnicodeLookalikeRegex = new(
        @"[\uFF0F\uFF0E\u2044\u2215\u2216\u29F8\uFE68\uFF3C]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Empty segments after normalization
    private static readonly Regex EmptySegmentRegex = new(
        @"(^/|//|/\s|/\.$|/\.\.$)",
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

                throw new GenesisException(nameof(TenantKeyResolver), "Tenant context not resolved. Cannot perform S3 operations without tenant context.", FileStorageConstants.FailureReasons.TenantContextNotResolved, null);
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

                throw new GenesisException(nameof(TenantKeyResolver), $"Invalid tenant ID format: {validationResult.ErrorMessage}", FileStorageConstants.FailureReasons.InvalidTenantId, null);
            }

            // URL decode to detect encoding attacks, but reject if it changes the key
            // This prevents silent key rewriting (report%20final.pdf → report final.pdf)
            // and collisions (a%20b vs a b, or %2F creating subfolders)
            var decodedKey = UrlDecodeUntilStable(userKey);
            
            // SECURITY: Reject keys where decoding changes them
            // If caller sends "report%20final.pdf", they expect that literal key in S3
            // Decoding it would silently rewrite to "report final.pdf" (data integrity issue)
            if (decodedKey != userKey)
            {
                _logger.LogWarning(
                    "Key contains URL-encoded sequences. Original: {OriginalKey}, Decoded: {DecodedKey}",
                    userKey,
                    decodedKey);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    $"Key '{userKey}' contains URL-encoded sequences (decoded to '{decodedKey}'). " +
                    "S3 keys must not contain % followed by hex digits. Use alternative naming like 'report_20_final.pdf' instead of 'report%20final.pdf'.",
                    FileStorageConstants.FailureReasons.InvalidKeyFormat,
                    null);
            }
            
            // Now normalize and validate the ORIGINAL key (not the decoded one)
            var normalizedKey = NormalizeKey(userKey);
            ValidateKeyFormat(normalizedKey);

            // Build full key: tenants/{tenantId}/{normalizedKey}
            var fullKey = FileStorageConstants.GetTenantKeyPrefix(tenantId) + normalizedKey;

            // Final verification: ensure the key starts with the expected prefix
            var expectedPrefix = FileStorageConstants.GetTenantKeyPrefix(tenantId);
            if (!fullKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Key construction failed. Expected prefix: {ExpectedPrefix}, Actual key: {ActualKey}",
                    expectedPrefix,
                    fullKey);

                throw new GenesisException(nameof(TenantKeyResolver), "Key construction validation failed", FileStorageConstants.FailureReasons.InvalidKeyFormat, null);
            }

            // Validate final key length in UTF-8 bytes (AWS S3 limit: 1024 bytes)
            ValidateKeyLength(fullKey);

            // Final security check: verify no .. segments exist after all normalization
            ValidateNoTraversalSegments(fullKey);

            _logger.LogDebug(
                "Resolved user key {UserKey} to full S3 key {FullKey} for tenant {TenantId}",
                userKey,
                fullKey,
                tenantId);

            return fullKey;
        }

        // Tenant isolation disabled (only allowed in Development/Test)
        // Still apply the same validation to prevent encoding attacks
        var decodedKeyNonTenant = UrlDecodeUntilStable(userKey);
        
        if (decodedKeyNonTenant != userKey)
        {
            _logger.LogWarning(
                "Key contains URL-encoded sequences. Original: {OriginalKey}, Decoded: {DecodedKey}",
                userKey,
                decodedKeyNonTenant);

            throw new GenesisException(
                nameof(TenantKeyResolver),
                $"Key '{userKey}' contains URL-encoded sequences (decoded to '{decodedKeyNonTenant}'). " +
                "S3 keys must not contain % followed by hex digits. Use alternative naming like 'report_20_final.pdf' instead of 'report%20final.pdf'.",
                FileStorageConstants.FailureReasons.InvalidKeyFormat,
                null);
        }
        
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

                throw new GenesisException(nameof(TenantKeyResolver), "Tenant context not resolved. Cannot perform S3 list operations without tenant context.", FileStorageConstants.FailureReasons.TenantContextNotResolved, null);
            }

            var tenantId = _tenantContext.TenantId.Value;
            
            // Validate tenant ID
            TenantIdValidator.ThrowIfInvalid(tenantId);

            // Base tenant prefix
            var tenantPrefix = FileStorageConstants.GetTenantKeyPrefix(tenantId);

            // If no user prefix, return just the tenant prefix
            if (string.IsNullOrWhiteSpace(userPrefix))
            {
                _logger.LogDebug(
                    "List operation with empty prefix, using tenant prefix: {Prefix}",
                    tenantPrefix);
                return tenantPrefix;
            }

            // URL decode to detect encoding attacks
            var decodedPrefix = UrlDecodeUntilStable(userPrefix);
            
            // Reject if decoding changes the prefix (same data-integrity rule as ResolveKey)
            if (decodedPrefix != userPrefix)
            {
                _logger.LogWarning(
                    "Prefix contains URL-encoded sequences. Original: {OriginalPrefix}, Decoded: {DecodedPrefix}",
                    userPrefix,
                    decodedPrefix);

                throw new GenesisException(
                    nameof(TenantKeyResolver),
                    $"Prefix '{userPrefix}' contains URL-encoded sequences. Use literal characters instead.",
                    FileStorageConstants.FailureReasons.InvalidKeyFormat,
                    null);
            }
            
            // Normalize the ORIGINAL prefix (not the decoded one)
            var normalizedPrefix = NormalizeKey(userPrefix);
            
            // Restore trailing slash if the original user prefix had one
            // (NormalizeKey removes it, but prefixes need it for S3 list operations)
            // However, don't add a slash if normalizedPrefix is empty (e.g., input was "///")
            var hadTrailingSlash = userPrefix.EndsWith('/');
            if (hadTrailingSlash && !string.IsNullOrEmpty(normalizedPrefix) && !normalizedPrefix.EndsWith('/'))
            {
                normalizedPrefix += "/";
            }
            
            // Validate for traversal attempts
            if (PathTraversalRegex.IsMatch(normalizedPrefix))
            {
                _logger.LogWarning(
                    "Path traversal attempt detected in prefix: {Prefix}",
                    userPrefix);

                throw new GenesisException(nameof(TenantKeyResolver), $"Invalid prefix: path traversal detected in '{userPrefix}'", FileStorageConstants.FailureReasons.KeyPathTraversal, null);
            }

            // Check for Unicode lookalikes
            if (UnicodeLookalikeRegex.IsMatch(normalizedPrefix))
            {
                _logger.LogWarning(
                    "Unicode lookalike characters detected in prefix: {Prefix}",
                    userPrefix);

                throw new GenesisException(nameof(TenantKeyResolver), $"Invalid prefix: Unicode lookalike characters detected in '{userPrefix}'", FileStorageConstants.FailureReasons.InvalidKeyFormat, null);
            }

            var fullPrefix = tenantPrefix + normalizedPrefix;

            // Ensure the final prefix is still within the tenant scope
            if (!fullPrefix.StartsWith(tenantPrefix, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Prefix construction failed. Expected to start with: {TenantPrefix}, Got: {FullPrefix}",
                    tenantPrefix,
                    fullPrefix);

                throw new GenesisException(nameof(TenantKeyResolver), "Prefix construction validation failed - attempted escape from tenant scope", FileStorageConstants.FailureReasons.KeyPathTraversal, null);
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

        // Still validate encoding for data integrity
        var decodedPrefixNonTenant = UrlDecodeUntilStable(userPrefix);
        
        if (decodedPrefixNonTenant != userPrefix)
        {
            _logger.LogWarning(
                "Prefix contains URL-encoded sequences. Original: {OriginalPrefix}, Decoded: {DecodedPrefix}",
                userPrefix,
                decodedPrefixNonTenant);

            throw new GenesisException(
                nameof(TenantKeyResolver),
                $"Prefix '{userPrefix}' contains URL-encoded sequences. Use literal characters instead.",
                FileStorageConstants.FailureReasons.InvalidKeyFormat,
                null);
        }

        var nonTenantPrefix = NormalizeKey(userPrefix);
        
        // Restore trailing slash if the original user prefix had one
        // Don't add if normalized prefix is empty
        if (userPrefix.EndsWith('/') && !string.IsNullOrEmpty(nonTenantPrefix) && !nonTenantPrefix.EndsWith('/'))
        {
            nonTenantPrefix += "/";
        }
        
        return nonTenantPrefix;
    }

    /// <summary>
    /// Strips the tenant prefix from a full S3 key, returning only the user-visible portion.
    /// SECURITY: Throws if the key doesn't start with the current tenant's prefix.
    /// </summary>
    /// <param name="fullKey">The full S3 key with tenant prefix.</param>
    /// <returns>User-visible key without tenant prefix.</returns>
    /// <exception cref="GenesisException">Thrown when key doesn't have expected tenant prefix.</exception>
    public string StripTenantPrefix(string fullKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullKey, nameof(fullKey));

        if (!_enableTenantIsolation)
        {
            return fullKey;
        }

        if (_tenantContext is null || !_tenantContext.IsResolved)
        {
            _logger.LogError(
                "Cannot strip tenant prefix without resolved tenant context for key: {FullKey}",
                fullKey);

            throw new GenesisException(nameof(TenantKeyResolver), "Tenant context not resolved. Cannot strip tenant prefix.", FileStorageConstants.FailureReasons.TenantContextNotResolved, null);
        }

        var tenantId = _tenantContext.TenantId.Value;
        var expectedPrefix = FileStorageConstants.GetTenantKeyPrefix(tenantId);

        if (!fullKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            // SECURITY: Never return a foreign key silently
            _logger.LogError(
                "Attempted to strip tenant prefix from key that doesn't belong to current tenant. " +
                "Key: {FullKey}, Expected prefix: {ExpectedPrefix}, Current tenant: {TenantId}",
                fullKey,
                expectedPrefix,
                tenantId);

            throw new GenesisException(nameof(TenantKeyResolver), $"Key '{fullKey}' does not belong to tenant '{tenantId}'. Cross-tenant access denied.", FileStorageConstants.FailureReasons.TenantMismatchOnDownload, null);
        }

        var userKey = fullKey[expectedPrefix.Length..];
        
        _logger.LogDebug(
            "Stripped tenant prefix from {FullKey}, returning {UserKey}",
            fullKey,
            userKey);
        
        return userKey;
    }

    /// <summary>
    /// Gets the current tenant ID from context.
    /// </summary>
    /// <returns>Tenant ID string.</returns>
    /// <exception cref="GenesisException">Thrown when tenant context is not resolved.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Throws when the tenant is unresolved; a property must not throw.")]
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
            throw new GenesisException(nameof(TenantKeyResolver), "Tenant context not resolved", FileStorageConstants.FailureReasons.TenantContextNotResolved, null);
        }

        return _tenantContext.TenantId.Value;
    }

    /// <summary>
    /// URL-decodes a string until it reaches a stable state (no more %XX sequences).
    /// Used to detect encoding-based attacks (e.g., %252e%252e → %2e%2e → ..).
    /// Uses Uri.UnescapeDataString to preserve + characters (only decodes %XX).
    /// </summary>
    /// <remarks>
    /// SECURITY: This method is used for DETECTION only, not rewriting.
    /// The decoded result is compared to the original. If they differ, the key is REJECTED.
    /// 
    /// This prevents:
    /// - Silent key rewriting ("report%20final.pdf" → "report final.pdf")
    /// - Key collisions ("a%20b" vs "a b")
    /// - Subfolder injection ("%2F" creating unexpected directories)
    /// - Path traversal via encoding ("..%2Fsecrets" → "../secrets")
    /// 
    /// If a key doesn't stabilize within MaxIterations, it's rejected to prevent
    /// resource exhaustion attacks.
    /// </remarks>
    private string UrlDecodeUntilStable(string input)
    {
        const int MaxIterations = 10; // Prevent infinite loops
        var current = input;
        var previousValue = input; // Track the previous iteration
        var iterations = 0;

        while (iterations < MaxIterations)
        {
            // Use Uri.UnescapeDataString instead of HttpUtility.UrlDecode
            // This only decodes %XX sequences and does NOT convert + to space
            // which is correct for S3 keys where + is a valid character
            var decoded = Uri.UnescapeDataString(current);
            
            // If no change, we've reached stability
            if (decoded == current)
            {
                return current;
            }

            previousValue = current;
            current = decoded;
            iterations++;
        }

        // If we hit max iterations, the key is still changing after 10 decode passes
        // This is suspicious - reject it to prevent encoding attacks
        _logger.LogWarning(
            "URL decode did not stabilize after {MaxIterations} iterations. " +
            "Input: {Input}, Last value: {LastValue}",
            MaxIterations,
            input,
            current);

        throw new GenesisException(
            nameof(TenantKeyResolver), 
            $"Excessive URL encoding detected in key (>{MaxIterations} levels). Key did not stabilize.", 
            FileStorageConstants.FailureReasons.InvalidKeyFormat, 
            null);
    }

    /// <summary>
    /// Normalizes an S3 key by:
    /// - Applying Unicode normalization (FormC)
    /// - Converting backslashes to forward slashes
    /// - Removing leading slashes
    /// - Collapsing multiple consecutive slashes
    /// - Removing trailing slashes
    /// </summary>
    private static string NormalizeKey(string key)
    {
        // Apply Unicode normalization (Form C - canonical composition)
        // This ensures lookalike characters are normalized to standard forms
        var normalized = key.Normalize(NormalizationForm.FormC);

        // Convert backslashes to forward slashes
        normalized = normalized.Replace("\\", "/", StringComparison.Ordinal);

        // Remove leading slashes
        normalized = normalized.TrimStart('/');

        // Collapse multiple consecutive slashes
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        // Remove trailing slashes for keys (but not for prefixes - caller handles that)
        normalized = normalized.TrimEnd('/');

        return normalized;
    }

    /// <summary>
    /// Validates key format for security issues after URL decode and Unicode normalization.
    /// </summary>
    private void ValidateKeyFormat(string key)
    {
        // Check for Unicode lookalike characters (fullwidth slashes, dots, etc.)
        if (UnicodeLookalikeRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Unicode lookalike characters detected in key: {Key}",
                key);

            throw new GenesisException(nameof(TenantKeyResolver), $"Invalid key format: Unicode lookalike characters detected in '{key}'", FileStorageConstants.FailureReasons.InvalidKeyFormat, null);
        }

        // Check for path traversal patterns (after decoding and normalization)
        if (PathTraversalRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Path traversal attempt detected in key: {Key}",
                key);

            throw new GenesisException(nameof(TenantKeyResolver), $"Invalid key format: path traversal detected in '{key}'", FileStorageConstants.FailureReasons.KeyPathTraversal, null);
        }

        // Check for control characters
        if (ControlCharacterRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Control characters detected in key: {Key}",
                key);

            throw new GenesisException(nameof(TenantKeyResolver), $"Invalid key format: control characters detected in '{key}'", FileStorageConstants.FailureReasons.InvalidKeyFormat, null);
        }

        // Check for empty segments
        if (EmptySegmentRegex.IsMatch(key))
        {
            _logger.LogWarning(
                "Empty segments detected in key: {Key}",
                key);

            throw new GenesisException(nameof(TenantKeyResolver), $"Invalid key format: empty segments detected in '{key}'", FileStorageConstants.FailureReasons.InvalidKeyFormat, null);
        }

        // Check for . or .. as standalone segments
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                _logger.LogWarning(
                    "Dot segments detected in key: {Key}",
                    key);

                throw new GenesisException(nameof(TenantKeyResolver), $"Invalid key format: relative path segments detected in '{key}'", FileStorageConstants.FailureReasons.KeyPathTraversal, null);
            }
        }

        // Reject keys that are just slashes or dots
        if (string.IsNullOrWhiteSpace(key) || key == "/" || key == "." || key == "..")
        {
            _logger.LogWarning(
                "Invalid key format: {Key}",
                key);

            throw new GenesisException(nameof(TenantKeyResolver), $"Invalid key format: '{key}' is not a valid S3 key", FileStorageConstants.FailureReasons.InvalidKeyFormat, null);
        }
    }

    /// <summary>
    /// Final validation: ensures no .. segments exist anywhere in the final key.
    /// </summary>
    private void ValidateNoTraversalSegments(string finalKey)
    {
        var segments = finalKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                _logger.LogError(
                    "SECURITY: .. segment found in final key after all normalization: {Key}",
                    finalKey);

                throw new GenesisException(nameof(TenantKeyResolver), "Security violation: traversal segment in final key", FileStorageConstants.FailureReasons.KeyPathTraversal, null);
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

            throw new GenesisException(nameof(TenantKeyResolver), $"Key exceeds maximum length of {FileStorageConstants.MaxKeyLengthBytes} bytes (got {keyBytes} bytes)", FileStorageConstants.FailureReasons.KeyTooLong, null);
        }
    }
}

