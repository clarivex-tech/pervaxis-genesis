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
using Microsoft.Extensions.DependencyInjection;
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
using Pervaxis.Genesis.FileStorage.AWS.Security;

namespace Pervaxis.Genesis.FileStorage.AWS.Providers.S3;

/// <summary>
/// AWS S3 implementation of the <see cref="IFileStorage"/> interface.
/// </summary>
public sealed class S3FileStorageProvider : IFileStorage, IDisposable
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<S3FileStorageProvider> _logger;
    private readonly TenantKeyResolver _tenantKeyResolver;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IAmazonS3> _s3Client;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly bool _ownsClient;
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
    /// <param name="tenantKeyResolver">Tenant key resolver for secure key resolution.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public S3FileStorageProvider(
        IOptions<FileStorageOptions> options,
        ILogger<S3FileStorageProvider> logger,
        TenantKeyResolver tenantKeyResolver,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(tenantKeyResolver);

        _options = options.Value;
        _logger = logger;
        _tenantKeyResolver = tenantKeyResolver;
        _tenantContext = tenantContext;

        _options.Validate();

        // This constructor creates its own client, so this instance owns it and disposes it.
        _ownsClient = true;
        _s3Client = new Lazy<IAmazonS3>(() => S3ClientFactory.Create(_options));

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
    /// Initializes a new instance using a shared S3 client. This is the constructor used by dependency injection.
    /// The client is a Singleton shared across scopes, so this provider does NOT dispose it.
    /// </summary>
    /// <param name="options">File storage configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="s3Client">Shared S3 client. Its lifetime is owned by the caller or the DI container.</param>
    /// <param name="tenantKeyResolver">Tenant key resolver for secure key resolution.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    public S3FileStorageProvider(
        IOptions<FileStorageOptions> options,
        ILogger<S3FileStorageProvider> logger,
        [FromKeyedServices(FileStorageConstants.S3ClientServiceKey)] IAmazonS3 s3Client,
        TenantKeyResolver tenantKeyResolver,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(s3Client);
        ArgumentNullException.ThrowIfNull(tenantKeyResolver);

        _options = options.Value;
        _tenantKeyResolver = tenantKeyResolver;
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

            // Return the user key, not the full S3 key (which includes tenant prefix)
            return key;
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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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

            // Defense in depth: the object must carry this tenant's stamp. Dispose the response if rejected.
            try
            {
                VerifyTenantStamp(response.Metadata, fullKey);
            }
            catch (GenesisException)
            {
                response.Dispose();
                throw;
            }
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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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

            var existsResponse = await _resiliencePipeline.ExecuteAsync(
                async ct => await _s3Client.Value.GetObjectMetadataAsync(request, ct),
                cancellationToken);

            // An object under this tenant's prefix with a missing or foreign stamp must not be reported as existing.
            VerifyTenantStamp(existsResponse.Metadata, fullKey);

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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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

        // Enforce maximum expiry cap for security
        var maxExpiry = TimeSpan.FromMinutes(_options.MaxPresignedUrlExpiryMinutes);
        if (expiry > maxExpiry)
        {
            throw new GenesisException(
                nameof(S3FileStorageProvider),
                $"Presigned URL expiry cannot exceed {maxExpiry.TotalHours:F0} hours. Requested: {expiry.TotalHours:F1} hours.",
                FileStorageConstants.FailureReasons.PresignedUrlExpiryExceeded,
                null);
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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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

            VerifyTenantStamp(response.Metadata, fullKey);

            _logger.LogInformation("Retrieved metadata for file {Key}", fullKey);

            var tags = GetMetricTags("get_metadata", "success");
            _operationsCounter.Add(1, tags);
            _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

            return response.Metadata.Keys
                .Where(k => !IsTenantStampKey(k))
                .ToDictionary(k => k, k => response.Metadata[k]);
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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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
            // Use TenantKeyResolver for prefix resolution to ensure tenant isolation
            var fullPrefix = (_options.KeyPrefix ?? string.Empty) + _tenantKeyResolver.ResolvePrefix(prefix);

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

                // AWS SDK v4 returns S3Objects == null (not empty) when the listing has no objects.
                keys.AddRange((response.S3Objects ?? []).Select(obj =>
                    RemoveKeyPrefix(obj.Key)));

                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);

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
        catch (GenesisException gex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, gex.Message);
            activity?.SetTag("exception.id", gex.ExceptionId);
            activity?.SetTag("exception.provider", gex.ProviderName);
            if (!string.IsNullOrWhiteSpace(gex.ErrorCode))
            {
                activity?.SetTag("exception.error_code", gex.ErrorCode);
            }

            _logger.LogError(gex, "Provider {ProviderName} error {ExceptionId}", gex.ProviderName, gex.ExceptionId);
            throw;
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

        // Caller metadata plus the tenant stamp and tags. Reserved keys are rejected, never overridden.
        foreach (var kvp in BuildObjectMetadata(metadata))
        {
            request.Metadata.Add(kvp.Key, kvp.Value);
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

        // Caller metadata plus the tenant stamp and tags. Reserved keys are rejected, never overridden.
        foreach (var kvp in BuildObjectMetadata(metadata))
        {
            request.Metadata.Add(kvp.Key, kvp.Value);
        }

        await _resiliencePipeline.ExecuteAsync(
            async ct => await transferUtility.UploadAsync(request, ct),
            cancellationToken);
    }

    /// <summary>
    /// Gets the full S3 key using TenantKeyResolver for secure tenant isolation.
    /// Applies KeyPrefix at the bucket level if configured.
    /// Final structure: {KeyPrefix}tenants/{tenant-id}/{user-key}
    /// </summary>
    private string GetFullKey(string key)
    {
        // First resolve tenant isolation: tenants/{tenant-id}/{user-key}
        var tenantScopedKey = _tenantKeyResolver.ResolveKey(key);
            
        // Then apply KeyPrefix at the bucket level (if configured)
        // This allows multi-environment buckets: dev/tenants/..., staging/tenants/..., prod/tenants/...
        if (string.IsNullOrEmpty(_options.KeyPrefix))
        {
            return tenantScopedKey;
        }
            
        return _options.KeyPrefix + tenantScopedKey;
    }

    /// <summary>
    /// Normalizes S3 keys by converting backslashes and collapsing multiple slashes.
    /// Note: Preserves leading/trailing slashes as they're meaningful in S3 (prefix matching).
    /// </summary>
    private static string NormalizeS3Key(string key)
    {
        return key
            .Replace("\\", "/", StringComparison.Ordinal)     // Convert backslashes to forward slashes
            .Replace("//", "/", StringComparison.Ordinal)     // Collapse multiple slashes to single slash
            .TrimStart('/');                                  // Only trim leading slashes (trailing slashes matter for prefixes)
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
    /// Converts a full S3 key back to the caller-visible key: removes the configured KeyPrefix,
    /// then the tenant prefix. Throws if the key is outside either prefix instead of returning it.
    /// </summary>
    private string RemoveKeyPrefix(string fullKey)
    {
        var keyPrefix = _options.KeyPrefix ?? string.Empty;
        var withoutKeyPrefix = fullKey;

        if (keyPrefix.Length > 0)
        {
            if (!fullKey.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                _logger.LogError(
                    "S3 returned key {FullKey} outside the configured key prefix {KeyPrefix}",
                    fullKey,
                    keyPrefix);

                throw new GenesisException(
                    nameof(S3FileStorageProvider),
                    $"Key '{fullKey}' is outside the configured key prefix.",
                    FileStorageConstants.FailureReasons.TenantMismatchOnDownload,
                    null);
            }

            withoutKeyPrefix = fullKey[keyPrefix.Length..];
        }

        return _tenantKeyResolver.StripTenantPrefix(withoutKeyPrefix);
    }

    private const string AmzMetaPrefix = "x-amz-meta-";

    private static string NormalizeMetadataKey(string key) =>
        key.StartsWith(AmzMetaPrefix, StringComparison.OrdinalIgnoreCase) ? key[AmzMetaPrefix.Length..] : key;

    private static bool IsTenantStampKey(string key) =>
        NormalizeMetadataKey(key).Equals(FileStorageConstants.TenantIdMetadataKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the metadata written with an object: caller metadata, the tenant stamp and the tenant tags.
    /// Callers may not set the stamp (or the tenant tag keys while isolation is enabled); that is rejected
    /// rather than silently overwritten so a bad caller is visible.
    /// </summary>
    internal Dictionary<string, string> BuildObjectMetadata(IDictionary<string, string>? callerMetadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (callerMetadata != null)
        {
            foreach (var kvp in callerMetadata)
            {
                var normalized = NormalizeMetadataKey(kvp.Key);
                var reserved = IsTenantStampKey(kvp.Key)
                    || (_options.EnableTenantIsolation
                        && (normalized.Equals("TenantId", StringComparison.OrdinalIgnoreCase)
                            || normalized.Equals("TenantName", StringComparison.OrdinalIgnoreCase)));

                if (reserved)
                {
                    _logger.LogWarning("Rejected caller metadata with reserved key {MetadataKey}", kvp.Key);

                    throw new GenesisException(
                        nameof(S3FileStorageProvider),
                        $"Metadata key '{kvp.Key}' is reserved for tenant isolation and cannot be set by callers.",
                        FileStorageConstants.FailureReasons.ReservedMetadataKey,
                        null);
                }

                result[normalized] = kvp.Value;
            }
        }

        if (_options.EnableTenantIsolation)
        {
            result[FileStorageConstants.TenantIdMetadataKey] = _tenantKeyResolver.GetTenantId();
        }

        foreach (var tag in BuildTenantTags())
        {
            result[tag.Key] = tag.Value;
        }

        return result;
    }

    /// <summary>
    /// Verifies that an object carries the current tenant's stamp. S3 returns metadata names lower-cased and
    /// with the x-amz-meta- prefix, so the comparison ignores both. A missing stamp is rejected (fail closed).
    /// </summary>
    private void VerifyTenantStamp(MetadataCollection metadata, string fullKey)
    {
        if (!_options.EnableTenantIsolation)
        {
            return;
        }

        var expectedTenantId = _tenantKeyResolver.GetTenantId();
        string? storedTenantId = null;

        foreach (var name in metadata.Keys)
        {
            if (IsTenantStampKey(name))
            {
                storedTenantId = metadata[name];
                break;
            }
        }

        if (storedTenantId is null)
        {
            _logger.LogError("Object {Key} has no tenant stamp; rejecting for safety", fullKey);

            throw new GenesisException(
                nameof(S3FileStorageProvider),
                $"Object is missing the '{FileStorageConstants.TenantIdMetadataKey}' tenant stamp and is rejected.",
                FileStorageConstants.FailureReasons.TenantStampMissing,
                null);
        }

        if (!string.Equals(storedTenantId, expectedTenantId, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Tenant stamp mismatch on {Key}: expected {ExpectedTenant}, found {StoredTenant}",
                fullKey,
                expectedTenantId,
                storedTenantId);

            throw new GenesisException(
                nameof(S3FileStorageProvider),
                $"Tenant ID mismatch: object belongs to '{storedTenantId}', current tenant is '{expectedTenantId}'",
                FileStorageConstants.FailureReasons.TenantMismatchOnDownload,
                null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Only dispose a client this instance created. An injected client is a shared Singleton
        // and disposing it here would break every other scope.
        if (_ownsClient && _s3Client.IsValueCreated)
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
