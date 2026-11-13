/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.FileStorage.AWS.Options;
using Pervaxis.Genesis.FileStorage.AWS.Providers.S3;
using Pervaxis.Genesis.FileStorage.AWS.Security;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests;

/// <summary>
/// Large uploads go through TransferUtility. TransferUtility itself only switches to S3 multipart
/// above 16 MB, so these tests use a 17 MB payload to exercise the real multipart calls.
/// </summary>
public sealed class MultipartUploadTests
{
    private const int PayloadBytes = 17 * 1024 * 1024;

    private static (S3FileStorageProvider Provider, Mock<IAmazonS3> S3) Create(string tenantId = "tenant-a")
    {
        var s3 = new Mock<IAmazonS3>{ DefaultValue = DefaultValue.Mock };
        s3.Setup(x => x.Config).Returns(new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.USEast1 });

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(x => x.IsResolved).Returns(true);
        tenant.Setup(x => x.TenantId).Returns(new TenantId(tenantId));
        tenant.Setup(x => x.TenantName).Returns("Tenant A");

        var options = new FileStorageOptions
        {
            Region = "us-east-1",
            BucketName = "test-bucket",
            MultipartUploadThresholdBytes = 5 * 1024 * 1024,
            MultipartUploadPartSizeBytes = 5 * 1024 * 1024
        };

        var resolver = new TenantKeyResolver(tenant.Object, Mock.Of<ILogger<TenantKeyResolver>>(), enableTenantIsolation: true);
        var provider = new S3FileStorageProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<S3FileStorageProvider>>(),
            s3.Object,
            resolver,
            tenant.Object);

        return (provider, s3);
    }

    private static void SetupInitiate(Mock<IAmazonS3> s3, List<InitiateMultipartUploadRequest> captured) =>
        s3.Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InitiateMultipartUploadRequest, CancellationToken>((r, _) => captured.Add(r))
            .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = "upload-1", BucketName = "test-bucket", Key = "k" });

    [Fact]
    public async Task MultipartUpload_CarriesTenantStampAndTenantPrefixedKey()
    {
        var (provider, s3) = Create();
        var initiated = new List<InitiateMultipartUploadRequest>();
        SetupInitiate(s3, initiated);
        s3.Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadPartRequest r, CancellationToken _) => new UploadPartResponse { PartNumber = r.PartNumber, ETag = $"etag-{r.PartNumber}" });
        s3.Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompleteMultipartUploadResponse());

        using var payload = new MemoryStream(new byte[PayloadBytes]);
        var key = await provider.UploadAsync("big/video.mp4", payload, metadata: new Dictionary<string, string> { ["owner"] = "alice" });

        key.Should().Be("big/video.mp4");
        initiated.Should().ContainSingle();
        var request = initiated[0];
        request.Key.Should().Be("tenants/tenant-a/big/video.mp4");

        var names = request.Metadata.Keys.ToList();
        names.Should().Contain(n => n.EndsWith(FileStorageConstants.TenantIdMetadataKey, StringComparison.OrdinalIgnoreCase));
        var stampName = names.First(n => n.EndsWith(FileStorageConstants.TenantIdMetadataKey, StringComparison.OrdinalIgnoreCase));
        request.Metadata[stampName].Should().Be("tenant-a");
        names.Should().Contain(n => n.EndsWith("owner", StringComparison.OrdinalIgnoreCase));

        s3.Verify(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
        s3.Verify(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        s3.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MultipartUpload_PartFails_AbortsTheUpload()
    {
        var (provider, s3) = Create();
        var initiated = new List<InitiateMultipartUploadRequest>();
        SetupInitiate(s3, initiated);
        s3.Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("part upload failed") { StatusCode = System.Net.HttpStatusCode.BadRequest });
        var aborted = new List<AbortMultipartUploadRequest>();
        s3.Setup(x => x.AbortMultipartUploadAsync(It.IsAny<AbortMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AbortMultipartUploadRequest, CancellationToken>((r, _) => aborted.Add(r))
            .ReturnsAsync(new AbortMultipartUploadResponse());

        using var payload = new MemoryStream(new byte[PayloadBytes]);
        var act = async () => await provider.UploadAsync("big/video.mp4", payload);

        await act.Should().ThrowAsync<GenesisException>();
        aborted.Should().NotBeEmpty("a failed multipart upload must be aborted so orphaned parts are not billed");
        aborted.Should().OnlyContain(r => r.UploadId == "upload-1" && r.Key == "tenants/tenant-a/big/video.mp4");
        s3.Verify(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MultipartUpload_CallerSetsTenantStamp_IsRejectedBeforeAnyS3Call()
    {
        var (provider, s3) = Create();

        using var payload = new MemoryStream(new byte[PayloadBytes]);
        var act = async () => await provider.UploadAsync(
            "big/video.mp4",
            payload,
            metadata: new Dictionary<string, string> { [FileStorageConstants.TenantIdMetadataKey] = "tenant-b" });

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.ReservedMetadataKey);
        s3.Verify(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
