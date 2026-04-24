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

using Pervaxis.Core.Abstractions.Genesis;
using Pervaxis.Genesis.Base.Exceptions;

namespace Pervaxis.Genesis.FileStorage.AWS.Options;

/// <summary>
/// Configuration options for AWS S3 file storage provider.
/// </summary>
public sealed class FileStorageOptions : GenesisOptionsBase
{
    /// <summary>
    /// S3 bucket name for file storage.
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional key prefix for all files (e.g., "uploads/", "prod/").
    /// Note: For tenant isolation, use <see cref="EnableTenantIsolation"/> instead.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to automatically prefix S3 keys with tenant ID
    /// and tag objects with tenant metadata. Default is true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Default presigned URL expiration time in minutes (default: 60 minutes).
    /// </summary>
    public int DefaultPresignedUrlExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum file size in bytes for single-part uploads (default: 5MB).
    /// Files larger than this will use multipart upload.
    /// </summary>
    public long MultipartUploadThresholdBytes { get; set; } = 5 * 1024 * 1024; // 5MB

    /// <summary>
    /// Part size for multipart uploads in bytes (default: 5MB).
    /// Must be between 5MB and 5GB.
    /// </summary>
    public long MultipartUploadPartSizeBytes { get; set; } = 5 * 1024 * 1024; // 5MB

    /// <summary>
    /// Default storage class for uploaded files (e.g., "STANDARD", "INTELLIGENT_TIERING", "GLACIER").
    /// </summary>
    public string StorageClass { get; set; } = "STANDARD";

    /// <summary>
    /// Enable server-side encryption (AES256 or aws:kms).
    /// </summary>
    public bool EnableServerSideEncryption { get; set; } = true;

    /// <summary>
    /// KMS key ID for server-side encryption (optional, only used if EnableServerSideEncryption is true).
    /// </summary>
    public string? KmsKeyId { get; set; }

    /// <summary>
    /// Validates the file storage configuration.
    /// </summary>
    public override bool Validate()
    {
        var baseValid = base.Validate();

        ArgumentException.ThrowIfNullOrWhiteSpace(Region, nameof(Region));
        ArgumentException.ThrowIfNullOrWhiteSpace(BucketName, nameof(BucketName));

        if (DefaultPresignedUrlExpiryMinutes <= 0)
        {
            throw new GenesisConfigurationException(
                "FileStorageOptions",
                $"{nameof(DefaultPresignedUrlExpiryMinutes)} must be greater than 0.");
        }

        if (MultipartUploadThresholdBytes < 5 * 1024 * 1024)
        {
            throw new GenesisConfigurationException(
                "FileStorageOptions",
                $"{nameof(MultipartUploadThresholdBytes)} must be at least 5MB (5,242,880 bytes).");
        }

        if (MultipartUploadPartSizeBytes < 5 * 1024 * 1024 || MultipartUploadPartSizeBytes > 5L * 1024 * 1024 * 1024)
        {
            throw new GenesisConfigurationException(
                "FileStorageOptions",
                $"{nameof(MultipartUploadPartSizeBytes)} must be between 5MB and 5GB.");
        }

        return baseValid;
    }
}
