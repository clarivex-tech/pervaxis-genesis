# Pervaxis.Genesis.FileStorage.AWS

AWS S3 file storage provider for the Pervaxis Genesis platform — scalable object storage with presigned URLs, multipart uploads, and metadata management.

## Overview

`Pervaxis.Genesis.FileStorage.AWS` implements the `IFileStorage` abstraction from `Pervaxis.Core.Abstractions` using the AWS SDK for S3. It provides:

- **Single-part and multipart uploads** (automatic threshold-based switching)
- **Presigned URL generation** for secure temporary access
- **Metadata management** with custom key-value pairs
- **Server-side encryption** (AES256 or KMS)
- **Storage class support** (STANDARD, INTELLIGENT_TIERING, GLACIER, etc.)
- **Key prefix support** for tenant isolation
- **LocalStack support** for local development

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.FileStorage.AWS" Version="1.0.0" />
```

## Configuration

### appsettings.json

```json
{
  "FileStorage": {
    "Region": "ap-south-1",
    "BucketName": "my-app-files",
    "KeyPrefix": "uploads/",
    "DefaultPresignedUrlExpiryMinutes": 60,
    "MultipartUploadThresholdBytes": 5242880,
    "MultipartUploadPartSizeBytes": 5242880,
    "StorageClass": "INTELLIGENT_TIERING",
    "EnableServerSideEncryption": true,
    "KmsKeyId": "arn:aws:kms:ap-south-1:123456789012:key/12345678-1234-1234-1234-123456789012"
  }
}
```

### Option properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Region` | `string` | **(required)** | AWS region (e.g., "ap-south-1", "us-east-1") |
| `BucketName` | `string` | **(required)** | S3 bucket name |
| `KeyPrefix` | `string` | `""` | Optional prefix for all keys (e.g., "uploads/", "tenant-123/") |
| `DefaultPresignedUrlExpiryMinutes` | `int` | `60` | Default presigned URL expiration in minutes |
| `MultipartUploadThresholdBytes` | `long` | `5242880` (5MB) | File size threshold for multipart upload |
| `MultipartUploadPartSizeBytes` | `long` | `5242880` (5MB) | Part size for multipart uploads (5MB-5GB) |
| `StorageClass` | `string` | `"STANDARD"` | S3 storage class (STANDARD, INTELLIGENT_TIERING, GLACIER, etc.) |
| `EnableServerSideEncryption` | `bool` | `true` | Enable server-side encryption |
| `KmsKeyId` | `string?` | `null` | KMS key ID for encryption (optional, uses AES256 if not set) |
| `UseLocalEmulator` | `bool` | `false` | Enable LocalStack support |
| `LocalEmulatorUrl` | `Uri?` | `null` | LocalStack URL (defaults to http://localhost:4566) |

### LocalStack Configuration

For local development with LocalStack:

```json
{
  "FileStorage": {
    "Region": "us-east-1",
    "BucketName": "test-bucket",
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566"
  }
}
```

## Registration

### Using IConfiguration

```csharp
using Pervaxis.Genesis.FileStorage.AWS.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register S3 file storage
builder.Services.AddGenesisFileStorage(
    builder.Configuration.GetSection("FileStorage"));
```

### Using Action Delegate

```csharp
using Pervaxis.Genesis.FileStorage.AWS.Extensions;
using Pervaxis.Genesis.FileStorage.AWS.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGenesisFileStorage(options =>
{
    options.Region = "ap-south-1";
    options.BucketName = "my-app-files";
    options.KeyPrefix = "uploads/";
    options.EnableServerSideEncryption = true;
});
```

## Usage

### Basic File Operations

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class DocumentService
{
    private readonly IFileStorage _fileStorage;

    public DocumentService(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    public async Task<string> UploadDocumentAsync(Stream content, string fileName)
    {
        // Upload file (returns full S3 key)
        var key = await _fileStorage.UploadAsync(
            key: $"documents/{fileName}",
            content: content,
            contentType: "application/pdf");

        return key;
    }

    public async Task<Stream?> DownloadDocumentAsync(string key)
    {
        // Download file (returns null if not found)
        return await _fileStorage.DownloadAsync(key);
    }

    public async Task<bool> DeleteDocumentAsync(string key)
    {
        // Delete file
        return await _fileStorage.DeleteAsync(key);
    }

    public async Task<bool> CheckDocumentExistsAsync(string key)
    {
        // Check if file exists
        return await _fileStorage.ExistsAsync(key);
    }
}
```

### Presigned URLs

```csharp
public class FileShareService
{
    private readonly IFileStorage _fileStorage;

    public FileShareService(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    public async Task<string> GenerateDownloadLinkAsync(string key)
    {
        // Generate presigned URL valid for 1 hour
        var url = await _fileStorage.GetPresignedUrlAsync(
            key: key,
            expiry: TimeSpan.FromHours(1));

        return url;
    }

    public async Task<string> GenerateTemporaryShareLinkAsync(string key)
    {
        // Generate presigned URL valid for 15 minutes
        var url = await _fileStorage.GetPresignedUrlAsync(
            key: key,
            expiry: TimeSpan.FromMinutes(15));

        return url;
    }
}
```

### Upload with Metadata

```csharp
public async Task<string> UploadWithMetadataAsync(
    Stream content,
    string fileName,
    string uploadedBy)
{
    var metadata = new Dictionary<string, string>
    {
        ["uploaded-by"] = uploadedBy,
        ["uploaded-at"] = DateTime.UtcNow.ToString("O"),
        ["file-type"] = "document"
    };

    var key = await _fileStorage.UploadAsync(
        key: $"documents/{fileName}",
        content: content,
        contentType: "application/pdf",
        metadata: metadata);

    return key;
}

public async Task<IDictionary<string, string>> GetFileMetadataAsync(string key)
{
    // Retrieve metadata
    return await _fileStorage.GetMetadataAsync(key);
}
```

### List Files

```csharp
public async Task<IEnumerable<string>> ListUserDocumentsAsync(string userId)
{
    // List all files with prefix
    var files = await _fileStorage.ListAsync(
        prefix: $"users/{userId}/documents/");

    return files;
}

public async Task<IEnumerable<string>> ListAllFilesAsync()
{
    // List all files in bucket (respecting KeyPrefix from config)
    return await _fileStorage.ListAsync();
}
```

### Large File Upload (Multipart)

The provider automatically uses multipart upload for files larger than `MultipartUploadThresholdBytes`:

```csharp
public async Task<string> UploadLargeFileAsync(Stream largeFile, string fileName)
{
    // Automatically uses multipart upload if file > threshold (default 5MB)
    var key = await _fileStorage.UploadAsync(
        key: $"large-files/{fileName}",
        content: largeFile,
        contentType: "video/mp4");

    return key;
}
```

## IAM Permissions

### Minimum Required Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket",
        "s3:GetObjectMetadata"
      ],
      "Resource": [
        "arn:aws:s3:::my-app-files/*",
        "arn:aws:s3:::my-app-files"
      ]
    }
  ]
}
```

### With KMS Encryption

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket",
        "s3:GetObjectMetadata"
      ],
      "Resource": [
        "arn:aws:s3:::my-app-files/*",
        "arn:aws:s3:::my-app-files"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "kms:Decrypt",
        "kms:Encrypt",
        "kms:GenerateDataKey"
      ],
      "Resource": "arn:aws:kms:ap-south-1:123456789012:key/12345678-1234-1234-1234-123456789012"
    }
  ]
}
```

## Error Handling

All operations throw `GenesisException` on failure:

```csharp
using Pervaxis.Genesis.Base.Exceptions;

try
{
    await _fileStorage.UploadAsync(key, content);
}
catch (GenesisException ex)
{
    _logger.LogError(ex, "File upload failed: {Message}", ex.Message);
    // Handle error (e.g., return error response)
}
```

## LocalStack Setup

### 1. Start LocalStack

```bash
docker run -d -p 4566:4566 localstack/localstack
```

### 2. Create Test Bucket

```bash
aws --endpoint-url=http://localhost:4566 s3 mb s3://test-bucket
```

### 3. Configure Application

```json
{
  "FileStorage": {
    "Region": "us-east-1",
    "BucketName": "test-bucket",
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566"
  }
}
```

## Troubleshooting

### Issue: "Access Denied" errors

**Cause:** Insufficient IAM permissions or bucket policy restrictions.

**Solution:**
- Verify IAM policy includes required S3 actions
- Check bucket policy allows access from your AWS account/role
- Ensure KMS key policy allows encrypt/decrypt operations

### Issue: "Bucket does not exist"

**Cause:** S3 bucket name is incorrect or doesn't exist.

**Solution:**
- Verify `BucketName` in configuration matches actual bucket name
- Ensure bucket exists in the specified region
- Check AWS account has access to the bucket

### Issue: Multipart upload fails

**Cause:** Part size too small or network interruption.

**Solution:**
- Ensure `MultipartUploadPartSizeBytes` is at least 5MB
- Increase part size for better reliability: `"MultipartUploadPartSizeBytes": 10485760` (10MB)
- Check network stability for large file uploads

### Issue: Presigned URLs don't work

**Cause:** URL expired or bucket/object permissions incorrect.

**Solution:**
- Verify URL hasn't expired (`DefaultPresignedUrlExpiryMinutes`)
- Ensure object exists in S3
- Check bucket CORS configuration for browser access

### Issue: Files uploaded to wrong location

**Cause:** `KeyPrefix` configuration not applied correctly.

**Solution:**
- Verify `KeyPrefix` in options (e.g., `"uploads/"`)
- Ensure prefix ends with `/` for proper path construction
- Check actual S3 keys using AWS Console or CLI

## Best Practices

1. **Use INTELLIGENT_TIERING storage class** for cost optimization on variable access patterns
2. **Enable server-side encryption** (`EnableServerSideEncryption: true`)
3. **Set appropriate multipart threshold** (5-10MB) based on typical file sizes
4. **Use key prefixes for tenant isolation** (e.g., `tenant-{id}/`)
5. **Generate presigned URLs with minimal expiry** for security
6. **Implement retry logic** for transient S3 failures
7. **Monitor S3 metrics** (request rate, error rate, data transfer)
8. **Use lifecycle policies** for automatic archival/deletion

## Performance Considerations

- **Multipart uploads** improve performance for files > 5MB
- **Connection pooling** is handled by AWS SDK
- **Parallel uploads** supported via multiple `UploadAsync` calls
- **Presigned URLs** offload download traffic from your application
- **ListAsync pagination** automatically handled for large result sets

---

**License:** Proprietary — Clarivex Technologies Private Limited  
**Product:** Pervaxis Platform  
**Website:** https://clarivex.tech
