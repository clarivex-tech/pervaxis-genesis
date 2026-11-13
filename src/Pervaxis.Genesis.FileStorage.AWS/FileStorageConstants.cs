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

namespace Pervaxis.Genesis.FileStorage.AWS;

/// <summary>
/// Constants for S3 file storage multi-tenancy and security.
/// </summary>
public static class FileStorageConstants
{
    /// <summary>
    /// Metadata key for tenant ID stamping.
    /// Used for defense-in-depth verification on download operations.
    /// </summary>
    public const string TenantIdMetadataKey = "pervaxis-tenant-id";

    /// <summary>
    /// Prefix format for tenant-scoped keys: tenants/{tenantId}/
    /// Phase 1: Application-enforced isolation (not IAM-enforced).
    /// </summary>
    public const string TenantKeyPrefixFormat = "tenants/{0}/";

    /// <summary>
    /// Absolute ceiling for presigned URL lifetime in minutes (24 hours).
    /// Configuration cannot raise <c>MaxPresignedUrlExpiryMinutes</c> above this value.
    /// A presigned URL downloads directly from S3 and bypasses the tenant metadata check,
    /// so the tenant-prefixed key and a short lifetime are the only protection.
    /// </summary>
    public const int AbsoluteMaxPresignedUrlExpiryMinutes = 1440;

    /// <summary>
    /// DI service key under which the Singleton <c>IAmazonS3</c> client used by this module is registered.
    /// Keyed so it cannot collide with another module's unkeyed <c>IAmazonS3</c> registration.
    /// </summary>
    public const string S3ClientServiceKey = "Pervaxis.Genesis.FileStorage.S3Client";

    /// <summary>
    /// Maximum S3 key length in bytes (1,024 bytes per AWS spec).
    /// </summary>
    public const int MaxKeyLengthBytes = 1024;

    /// <summary>
    /// Builds the tenant key prefix (<c>tenants/{tenantId}/</c>) without culture-sensitive formatting.
    /// </summary>
    /// <param name="tenantId">The validated tenant identifier.</param>
    /// <returns>The tenant-scoped key prefix.</returns>
    public static string GetTenantKeyPrefix(string tenantId) => string.Concat("tenants/", tenantId, "/");

    /// <summary>
    /// Failure reasons for dead-letter handling and diagnostics.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1034:Nested types should not be visible",
        Justification = "Public error-code API (FileStorageConstants.FailureReasons.X); un-nesting would be a breaking change.")]
    public static class FailureReasons
    {
        /// <summary>No resolved tenant context was available for the operation.</summary>
        public const string TenantContextNotResolved = "TENANT_CONTEXT_NOT_RESOLVED";

        /// <summary>The tenant identifier failed format validation.</summary>
        public const string InvalidTenantId = "INVALID_TENANT_ID";

        /// <summary>The key or prefix contained a path traversal sequence.</summary>
        public const string KeyPathTraversal = "KEY_PATH_TRAVERSAL";

        /// <summary>The resolved key exceeded the S3 key length limit.</summary>
        public const string KeyTooLong = "KEY_TOO_LONG";

        /// <summary>The key or prefix had an invalid format (encoding, control characters, lookalikes).</summary>
        public const string InvalidKeyFormat = "INVALID_KEY_FORMAT";

        /// <summary>The object or key belongs to a different tenant.</summary>
        public const string TenantMismatchOnDownload = "TENANT_MISMATCH_ON_DOWNLOAD";

        /// <summary>The requested presigned URL lifetime exceeded the configured maximum.</summary>
        public const string PresignedUrlExpiryExceeded = "PRESIGNED_URL_EXPIRY_EXCEEDED";

        /// <summary>The object has no <c>pervaxis-tenant-id</c> stamp and is rejected (fail closed).</summary>
        public const string TenantStampMissing = "TENANT_STAMP_MISSING";

        /// <summary>The caller supplied a metadata key that is reserved for tenant isolation.</summary>
        public const string ReservedMetadataKey = "RESERVED_METADATA_KEY";
    }
}
