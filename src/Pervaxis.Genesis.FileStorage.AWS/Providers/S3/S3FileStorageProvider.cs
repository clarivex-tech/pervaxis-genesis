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

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.FileStorage.AWS.Options;

namespace Pervaxis.Genesis.FileStorage.AWS.Providers.S3;

/// <summary>
/// AWS S3 implementation of the <see cref="IFileStorage"/> interface.
/// </summary>
public sealed class S3FileStorageProvider : IFileStorage, IDisposable
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<S3FileStorageProvider> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IAmazonS3> _s3Client;
    private readonly ResiliencePipeline _resiliencePipeline;
    private bool _disposed;

    // Metrics
    private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.filestorage.operations",
        "1",
        "Total number of file storage operations");

    private static readonly Counter<long> _filesUploaded = PervaxisMeter.CreateCounter<long>(
        "genesis.filestorage.files.uploaded",
        "1",
        "Total number of files uploaded");

    private static readonly Histogram<long> _uploadSize = PervaxisMeter.CreateHistogram<long>(
        "genesis.filestorage.upload.size",
        "bytes",
        "Size of uploaded files in bytes");

    private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.filestorage.operation.duration",
        "ms",
        "Duration of file storage operations in milliseconds");

    /// <summary>
    /// Initializes a new instance of the <see cref="S3FileStorageProvider"/> class.
    /// </summary>
    /// <param name="options">File storage configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public S3FileStorageProvider(
        IOptions<FileStorageOptions> options,
        ILogger<S3FileStorageProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        _options.Validate();

        _s3Client = new Lazy<IAmazonS3>(() =>
        {
            var config = new AmazonS3Config
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region)
            };

            if (_options.UseLocalEmulator)
            {
                var emulatorUrl = _options.LocalEmulatorUrl?.ToString() ?? "http://localhost:4566";
                config.ServiceURL = emulatorUrl;
                config.ForcePathStyle = true;
            }

            return new AmazonS3Client(config);
        });

        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "S3FileStorage");

        _logger.LogInformation(
            "S3FileStorageProvider initialized for bucket {BucketName} in region {Region}, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.BucketName,
            _options.Region,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Internal constructor for testing with injected S3 client.
    /// </summary>
    internal S3FileStorageProvider(
        IOptions<FileStorageOptions> options,
        ILogger<S3FileStorageProvider> logger,
        IAmazonS3 s3Client,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(s3Client);

        _options = options.Value;
        _tenantContext = tenantContext;
        _logger = logger;
        _s3Client = new Lazy<IAmazonS3>(() => s3Client);
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "S3FileStorage");

        _options.Validate();
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string key,
        Stream content,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.upload", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.key", key);
        activity?.SetTag("storage.operation", "upload");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            var fullKey = GetFullKey(key);
            var contentLength = content.Length;
            activity?.SetTag("storage.size", contentLength);

            _logger.LogDebug(
                "Uploading file {Key} ({Size} bytes) to bucket {Bucket}",
                fullKey,
                contentLength,
                _options.BucketName);

            if (contentLength > _options.MultipartUploadThresholdBytes)
            {
                await UploadMultipartAsync(fullKey, content, contentType, metadata, cancellationToken);
            }
            else
            {
                await UploadSinglePartAsync(fullKey, content, contentType, metadata, cancellationToken);
            }

            _logger.LogInformation(
                "Successfully uploaded file {Key} ({Size} bytes) to bucket {Bucket}",
                fullKey,
                contentLength,
                _options.BucketName);

            var tags = GetMetricTags("upload", "success");
            _operationsCounter.Add(1, tags);
            _filesUploaded.Add(1, tags);
            _uploadSize.Record(contentLength, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return fullKey;
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to upload file {Key} to S3", key);

            var tags = GetMetricTags("upload", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to upload file: {key}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.download", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.key", key);
        activity?.SetTag("storage.operation", "download");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var fullKey = GetFullKey(key);

            _logger.LogDebug("Downloading file {Key} from bucket {Bucket}", fullKey, _options.BucketName);

            var request = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = fullKey
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _s3Client.Value.GetObjectAsync(request, ct),
                cancellationToken);

            activity?.SetTag("storage.size", response.ContentLength);
            _logger.LogInformation(
                "Successfully downloaded file {Key} ({Size} bytes) from bucket {Bucket}",
                fullKey,
                response.ContentLength,
                _options.BucketName);

            // Copy to MemoryStream so we can dispose the response
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            response.Dispose();

            var tags = GetMetricTags("download", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return memoryStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            activity?.SetTag("storage.found", false);
            _logger.LogWarning("File {Key} not found in bucket {Bucket}", key, _options.BucketName);

            var tags = GetMetricTags("download", "not_found");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return null;
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to download file {Key} from S3", key);

            var tags = GetMetricTags("download", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to download file: {key}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.delete", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.key", key);
        activity?.SetTag("storage.operation", "delete");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var fullKey = GetFullKey(key);

            _logger.LogDebug("Deleting file {Key} from bucket {Bucket}", fullKey, _options.BucketName);

            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = fullKey
            };

            await _resiliencePipeline.ExecuteAsync(
                async ct => await _s3Client.Value.DeleteObjectAsync(request, ct),
                cancellationToken);

            activity?.SetTag("storage.success", true);
            _logger.LogInformation("Successfully deleted file {Key} from bucket {Bucket}", fullKey, _options.BucketName);

            var tags = GetMetricTags("delete", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return true;
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to delete file {Key} from S3", key);

            var tags = GetMetricTags("delete", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to delete file: {key}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.exists", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.key", key);
        activity?.SetTag("storage.operation", "exists");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var fullKey = GetFullKey(key);

            _logger.LogDebug("Checking existence of file {Key} in bucket {Bucket}", fullKey, _options.BucketName);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = fullKey
            };

            await _resiliencePipeline.ExecuteAsync(
                async ct => await _s3Client.Value.GetObjectMetadataAsync(request, ct),
                cancellationToken);

            activity?.SetTag("storage.exists", true);
            _logger.LogDebug("File {Key} exists in bucket {Bucket}", fullKey, _options.BucketName);

            var tags = GetMetricTags("exists", "found");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            activity?.SetTag("storage.exists", false);
            _logger.LogDebug("File {Key} does not exist in bucket {Bucket}", key, _options.BucketName);

            var tags = GetMetricTags("exists", "not_found");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return false;
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to check existence of file {Key} in S3", key);

            var tags = GetMetricTags("exists", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to check file existence: {key}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetPresignedUrlAsync(
        string key,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.get_presigned_url", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.key", key);
        activity?.SetTag("storage.operation", "get_presigned_url");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentException("Expiry must be greater than zero.", nameof(expiry));
        }

        try
        {
            var fullKey = GetFullKey(key);

            _logger.LogDebug(
                "Generating presigned URL for file {Key} with expiry {Expiry}",
                fullKey,
                expiry);

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = fullKey,
                Expires = DateTime.UtcNow.Add(expiry)
            };

            var url = await Task.Run(() => _s3Client.Value.GetPreSignedURL(request), cancellationToken);

            _logger.LogInformation(
                "Generated presigned URL for file {Key} (expires in {Expiry})",
                fullKey,
                expiry);

            var tags = GetMetricTags("get_presigned_url", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return url;
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to generate presigned URL for file {Key}", key);

            var tags = GetMetricTags("get_presigned_url", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to generate presigned URL: {key}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, string>> GetMetadataAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.get_metadata", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.key", key);
        activity?.SetTag("storage.operation", "get_metadata");
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var fullKey = GetFullKey(key);

            _logger.LogDebug("Getting metadata for file {Key} from bucket {Bucket}", fullKey, _options.BucketName);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = fullKey
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _s3Client.Value.GetObjectMetadataAsync(request, ct),
                cancellationToken);

            _logger.LogInformation("Retrieved metadata for file {Key}", fullKey);

            var tags = GetMetricTags("get_metadata", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return response.Metadata.Keys.ToDictionary(k => k, k => response.Metadata[k]);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "File not found");
            _logger.LogWarning("File {Key} not found in bucket {Bucket}", key, _options.BucketName);

            var tags = GetMetricTags("get_metadata", "not_found");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"File not found: {key}", ex);
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get metadata for file {Key} from S3", key);

            var tags = GetMetricTags("get_metadata", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to get metadata: {key}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> ListAsync(
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity("storage.list", ActivityKind.Client);
        activity?.SetTag("storage.system", "s3");
        activity?.SetTag("storage.bucket", _options.BucketName);
        activity?.SetTag("storage.operation", "list");
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            activity?.SetTag("storage.prefix", prefix);
        }
        AddTenantTags(activity);

        try
        {
            var fullPrefix = string.IsNullOrWhiteSpace(prefix)
                ? _options.KeyPrefix
                : GetFullKey(prefix);

            _logger.LogDebug(
                "Listing files with prefix {Prefix} in bucket {Bucket}",
                fullPrefix,
                _options.BucketName);

            var request = new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = fullPrefix
            };

            var keys = new List<string>();
            ListObjectsV2Response response;

            do
            {
                response = await _resiliencePipeline.ExecuteAsync(
                    async ct => await _s3Client.Value.ListObjectsV2Async(request, ct),
                    cancellationToken);

                keys.AddRange(response.S3Objects.Select(obj =>
                    RemoveKeyPrefix(obj.Key)));

                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated);

            activity?.SetTag("storage.object_count", keys.Count);
            _logger.LogInformation(
                "Listed {Count} files with prefix {Prefix} from bucket {Bucket}",
                keys.Count,
                fullPrefix,
                _options.BucketName);

            var tags = GetMetricTags("list", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return keys;
        }
        catch (AmazonS3Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to list files with prefix {Prefix} from S3", prefix);

            var tags = GetMetricTags("list", "error");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            throw new GenesisException(nameof(S3FileStorageProvider), $"Failed to list files with prefix: {prefix}", ex);
        }
    }

    /// <summary>
    /// Uploads a file using single-part upload.
    /// </summary>
    private async Task UploadSinglePartAsync(
        string key,
        Stream content,
        string? contentType,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType ?? "application/octet-stream",
            StorageClass = S3StorageClass.FindValue(_options.StorageClass)
        };

        if (_options.EnableServerSideEncryption)
        {
            request.ServerSideEncryptionMethod = string.IsNullOrWhiteSpace(_options.KmsKeyId)
                ? ServerSideEncryptionMethod.AES256
                : ServerSideEncryptionMethod.AWSKMS;

            if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
            {
                request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
            }
        }

        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                request.Metadata.Add(kvp.Key, kvp.Value);
            }
        }

        // Add tenant tags
        foreach (var tag in BuildTenantTags())
        {
            request.Metadata.Add($"x-amz-meta-{tag.Key}", tag.Value);
        }

        await _resiliencePipeline.ExecuteAsync(
            async ct => await _s3Client.Value.PutObjectAsync(request, ct),
            cancellationToken);
    }

    /// <summary>
    /// Uploads a file using multipart upload.
    /// </summary>
    private async Task UploadMultipartAsync(
        string key,
        Stream content,
        string? contentType,
        IDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        using var transferUtility = new TransferUtility(_s3Client.Value);

        var request = new TransferUtilityUploadRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType ?? "application/octet-stream",
            StorageClass = S3StorageClass.FindValue(_options.StorageClass),
            PartSize = _options.MultipartUploadPartSizeBytes
        };

        if (_options.EnableServerSideEncryption)
        {
            request.ServerSideEncryptionMethod = string.IsNullOrWhiteSpace(_options.KmsKeyId)
                ? ServerSideEncryptionMethod.AES256
                : ServerSideEncryptionMethod.AWSKMS;

            if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
            {
                request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
            }
        }

        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                request.Metadata.Add(kvp.Key, kvp.Value);
            }
        }

        // Add tenant tags
        foreach (var tag in BuildTenantTags())
        {
            request.Metadata.Add($"x-amz-meta-{tag.Key}", tag.Value);
        }

        await _resiliencePipeline.ExecuteAsync(
            async ct => await transferUtility.UploadAsync(request, ct),
            cancellationToken);
    }

    /// <summary>
    /// Gets the full S3 key with configured prefix.
    /// </summary>
    private string GetFullKey(string key)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(_options.KeyPrefix))
        {
            parts.Add(_options.KeyPrefix.TrimEnd('/'));
        }

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            parts.Add($"tenant-{_tenantContext.TenantId.Value}");
        }

        parts.Add(key.TrimStart('/'));

        return string.Join("/", parts);
    }

    private Dictionary<string, string> BuildTenantTags()
    {
        var tags = new Dictionary<string, string>();

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags["TenantId"] = _tenantContext.TenantId.Value;
            tags["TenantName"] = _tenantContext.TenantName;
        }

        return tags;
    }

    /// <summary>
    /// Removes the configured prefix from an S3 key.
    /// </summary>
    private string RemoveKeyPrefix(string fullKey)
    {
        if (string.IsNullOrWhiteSpace(_options.KeyPrefix))
        {
            return fullKey;
        }

        var prefix = _options.KeyPrefix.TrimEnd('/') + "/";
        return fullKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fullKey[prefix.Length..]
            : fullKey;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_s3Client.IsValueCreated)
        {
            _s3Client.Value.Dispose();
            _logger.LogDebug("S3 client disposed");
        }

        _disposed = true;
    }

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }

    private TagList GetMetricTags(string operation, string result)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "result", result }
        };
        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }
        return tags;
    }
}
