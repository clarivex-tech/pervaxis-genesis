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

using System.IO.Compression;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

/// <summary>
/// Handles S3 overflow for transaction logs that exceed the DynamoDB 400KB item size limit.
/// Writes complete transaction records as GZIP-compressed JSON to S3.
/// </summary>
internal sealed class S3OverflowStore : IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly TransactionalLoggingOptions _options;
    private readonly ILogger<S3OverflowStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="S3OverflowStore"/> class.
    /// </summary>
    public S3OverflowStore(
        IAmazonS3 s3Client,
        IOptions<TransactionalLoggingOptions> options,
        ILogger<S3OverflowStore> logger)
    {
        ArgumentNullException.ThrowIfNull(s3Client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _s3Client = s3Client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Writes a full transaction log to S3 as GZIP-compressed JSON.
    /// </summary>
    /// <param name="context">The finalized transaction context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The S3 object key.</returns>
    public async Task<string> WriteOverflowAsync(TransactionContext context, CancellationToken cancellationToken = default)
    {
        var tenantId = _options.EnableTenantIsolation
            ? (context.TenantId ?? "__global__")
            : "__global__";

        var timestamp = context.StartTimestamp;
        var s3Key = $"{tenantId}/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{context.TransactionId}.json.gz";

        var json = JsonSerializer.Serialize(new
        {
            context.TransactionId,
            context.TraceId,
            context.TenantId,
            context.CorrelationId,
            context.IdempotencyKey,
            context.HttpMethod,
            context.RequestPath,
            StatusCode = context.HttpStatusCode,
            Status = context.Status.ToString(),
            StartTimestamp = context.StartTimestamp.ToString("O"),
            EndTimestamp = context.EndTimestamp?.ToString("O"),
            context.DurationMs,
            BusinessKeys = context.BusinessKeys,
            Entries = context.Entries,
            context.ErrorType,
            context.ErrorMessage
        }, JsonOptions);

        using var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        await using (var writer = new StreamWriter(gzipStream))
        {
            await writer.WriteAsync(json);
        }

        memoryStream.Position = 0;

        var putRequest = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = s3Key,
            InputStream = memoryStream,
            ContentType = "application/gzip"
        };

        if (_options.EnableObjectLock)
        {
            putRequest.ObjectLockMode = ObjectLockMode.Governance;
            putRequest.ObjectLockRetainUntilDate =
                DateTime.UtcNow.AddDays(_options.ColdRetentionDays);
        }

        await _s3Client.PutObjectAsync(putRequest, cancellationToken);

        return s3Key;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _s3Client.Dispose();
    }
}
