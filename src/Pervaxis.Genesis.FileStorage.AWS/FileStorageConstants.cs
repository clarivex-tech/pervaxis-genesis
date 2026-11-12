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
    /// Maximum presigned URL expiry in hours (7 days).
    /// Prevents callers from creating overly-long-lived URLs.
    /// </summary>
    public const int MaxPresignedUrlExpiryHours = 168; // 7 days

    /// <summary>
    /// Maximum S3 key length in bytes (1,024 bytes per AWS spec).
    /// </summary>
    public const int MaxKeyLengthBytes = 1024;

    /// <summary>
    /// Failure reasons for dead-letter handling and diagnostics.
    /// </summary>
    public static class FailureReasons
    {
        public const string TenantContextNotResolved = "TENANT_CONTEXT_NOT_RESOLVED";
        public const string InvalidTenantId = "INVALID_TENANT_ID";
        public const string KeyPathTraversal = "KEY_PATH_TRAVERSAL";
        public const string KeyTooLong = "KEY_TOO_LONG";
        public const string InvalidKeyFormat = "INVALID_KEY_FORMAT";
        public const string TenantMismatchOnDownload = "TENANT_MISMATCH_ON_DOWNLOAD";
        public const string PresignedUrlExpiryExceeded = "PRESIGNED_URL_EXPIRY_EXCEEDED";
    }
}
