/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.FileStorage.AWS.Options;
using Pervaxis.Genesis.FileStorage.AWS.Providers.S3;
using Pervaxis.Genesis.FileStorage.AWS.Security;
using Amazon.S3;
using Amazon.S3.Model;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests;

/// <summary>
/// Cross-tenant isolation tests to verify Tenant A cannot access Tenant B's files.
/// These tests MUST pass before any FileStorage code goes to production.
/// </summary>
public sealed class CrossTenantIsolationTests
{
    private readonly Mock<ILogger<S3FileStorageProvider>> _mockProviderLogger;
    private readonly Mock<ILogger<TenantKeyResolver>> _mockResolverLogger;
    private readonly FileStorageOptions _options;

    public CrossTenantIsolationTests()
    {
        _mockProviderLogger = new Mock<ILogger<S3FileStorageProvider>>();
        _mockResolverLogger = new Mock<ILogger<TenantKeyResolver>>();
        
        _options = new FileStorageOptions
        {
            Region = "us-east-1",
            BucketName = "test-bucket",
            EnableTenantIsolation = true,
            DefaultPresignedUrlExpiryMinutes = 60,
            MaxPresignedUrlExpiryMinutes = 1440
        };
    }

    private Mock<ITenantContext> CreateMockTenantContext(string tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(x => x.IsResolved).Returns(true);
        mock.Setup(x => x.TenantId).Returns(new TenantId(tenantId));
        mock.Setup(x => x.TenantName).Returns($"{tenantId}-corp");
        return mock;
    }

    private S3FileStorageProvider CreateProvider(
        Mock<IAmazonS3> mockS3Client,
        ITenantContext tenantContext)
    {
        var resolver = new TenantKeyResolver(
            tenantContext,
            _mockResolverLogger.Object,
            enableTenantIsolation: true);

        return new S3FileStorageProvider(
            Options.Create(_options),
            _mockProviderLogger.Object,
            mockS3Client.Object,
            resolver,
            tenantContext);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 1: Concurrent Tenants Using Same S3 Client
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentTenants_UseSameS3Client_IsolationMaintained()
    {
        // Arrange: Shared S3 client (simulating Scoped provider with Singleton client)
        var mockS3Client = new Mock<IAmazonS3>();
        
        var uploadedKeys = new List<string>();
        mockS3Client
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, _) => uploadedKeys.Add(req.Key))
            .ReturnsAsync(new PutObjectResponse());

        // Create two tenant contexts
        var tenantAContext = CreateMockTenantContext("tenant-a");
        var tenantBContext = CreateMockTenantContext("tenant-b");

        // Create two SEPARATE provider instances (Scoped) with SAME S3 client (Singleton)
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);
        var providerB = CreateProvider(mockS3Client, tenantBContext.Object);

        using var streamA = new MemoryStream(new byte[] { 1, 2, 3 });
        using var streamB = new MemoryStream(new byte[] { 4, 5, 6 });

        // Act: Upload from both tenants concurrently
        var taskA = providerA.UploadAsync("documents/report.pdf", streamA);
        var taskB = providerB.UploadAsync("documents/report.pdf", streamB);

        var results = await Task.WhenAll(taskA, taskB);

        // Assert: Keys must be different and correctly prefixed
        results[0].Should().Be("tenants/tenant-a/documents/report.pdf");
        results[1].Should().Be("tenants/tenant-b/documents/report.pdf");
        
        uploadedKeys.Should().HaveCount(2);
        uploadedKeys[0].Should().Be("tenants/tenant-a/documents/report.pdf");
        uploadedKeys[1].Should().Be("tenants/tenant-b/documents/report.pdf");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 2: Tenant A Cannot Download Tenant B's File
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Download_TenantA_CannotAccessTenantBFile()
    {
        // Arrange: File exists for Tenant B
        var mockS3Client = new Mock<IAmazonS3>();
        var tenantBFileKey = "tenants/tenant-b/secrets/confidential.pdf";
        
        mockS3Client
            .Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == tenantBFileKey),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(new byte[] { 1, 2, 3 }),
                Metadata = { ["pervaxis-tenant-id"] = "tenant-b" }
            });

        // Tenant A's provider tries to access it
        var tenantAContext = CreateMockTenantContext("tenant-a");
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);

        // Act: Tenant A tries to download Tenant B's file by guessing the key
        // This should fail because TenantKeyResolver will build tenants/tenant-a/ prefix
        var act = () => providerA.DownloadAsync("secrets/confidential.pdf");

        // Assert: Should either not find it or fail metadata check
        // The key resolver will transform to "tenants/tenant-a/secrets/confidential.pdf"
        // which won't match the S3 setup, so it should return null (not found)
        var result = await act();
        result.Should().BeNull("Tenant A should not find Tenant B's file");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 3: Tenant A Cannot List Tenant B's Files
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_TenantA_CannotSeeTenantBFiles()
    {
        // Arrange: S3 bucket contains files for both tenants
        var mockS3Client = new Mock<IAmazonS3>();
        
        mockS3Client
            .Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.Prefix == "tenants/tenant-a/"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = "tenants/tenant-a/doc1.pdf" },
                    new() { Key = "tenants/tenant-a/doc2.pdf" }
                }
            });

        var tenantAContext = CreateMockTenantContext("tenant-a");
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);

        // Act: Tenant A lists files with empty prefix
        var files = await providerA.ListAsync(prefix: null);

        // Assert: Should only see Tenant A's files (with tenant prefix stripped)
        var fileList = files.ToList();
        fileList.Should().HaveCount(2);
        fileList.Should().Contain("doc1.pdf");
        fileList.Should().Contain("doc2.pdf");
        
        // Verify the list was called with correct tenant prefix
        mockS3Client.Verify(
            x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.Prefix == "tenants/tenant-a/"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 4: Tenant A Cannot Delete Tenant B's File
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Delete_TenantA_CannotDeleteTenantBFile()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        var deletedKeys = new List<string>();
        
        mockS3Client
            .Setup(x => x.DeleteObjectAsync(
                It.IsAny<DeleteObjectRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((req, _) => deletedKeys.Add(req.Key))
            .ReturnsAsync(new DeleteObjectResponse());

        var tenantAContext = CreateMockTenantContext("tenant-a");
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);

        // Act: Tenant A attempts to delete a file
        // The key will be resolved to "tenants/tenant-a/target.pdf"
        await providerA.DeleteAsync("target.pdf");

        // Assert: Only Tenant A's prefixed key should be deleted, not Tenant B's
        deletedKeys.Should().ContainSingle();
        deletedKeys[0].Should().Be("tenants/tenant-a/target.pdf");
        deletedKeys[0].Should().NotContain("tenant-b");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 5: Tenant A Cannot Get Metadata for Tenant B's File
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMetadata_TenantA_CannotAccessTenantBMetadata()
    {
        // Arrange: File exists for Tenant B
        var mockS3Client = new Mock<IAmazonS3>();
        
        mockS3Client
            .Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r => r.Key == "tenants/tenant-a/data.json"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("Not Found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        var tenantAContext = CreateMockTenantContext("tenant-a");
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);

        // Act: Tenant A tries to get metadata
        var act = () => providerA.GetMetadataAsync("data.json");

        // Assert: Should fail because file doesn't exist in Tenant A's namespace
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*not found*");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 6: Download Verifies Tenant Metadata Stamp
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Download_VerifiesTenantMetadataStamp_ThrowsOnMismatch()
    {
        // Arrange: File has metadata stamp for Tenant B
        var mockS3Client = new Mock<IAmazonS3>();
        
        mockS3Client
            .Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == "tenants/tenant-a/suspicious.pdf"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(new byte[] { 1, 2, 3 }),
                Metadata = { ["pervaxis-tenant-id"] = "tenant-b" } // WRONG TENANT!
            });

        var tenantAContext = CreateMockTenantContext("tenant-a");
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);

        // Act: Tenant A tries to download, but metadata says it belongs to Tenant B
        var act = () => providerA.DownloadAsync("suspicious.pdf");

        // Assert: Should throw tenant mismatch exception
        await act.Should().ThrowAsync<GenesisException>()
            .Where(ex => ex.ErrorCode == "TENANT_MISMATCH_ON_DOWNLOAD" ||
                        ex.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TEST 7: Presigned URL Respects Max Expiry
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetPresignedUrl_ExceedsMaxExpiry_ThrowsException()
    {
        // Arrange
        var mockS3Client = new Mock<IAmazonS3>();
        var tenantAContext = CreateMockTenantContext("tenant-a");
        var providerA = CreateProvider(mockS3Client, tenantAContext.Object);

        // Act: Try to create a 48-hour presigned URL (exceeds 24-hour max)
        var act = () => providerA.GetPresignedUrlAsync(
            "documents/report.pdf",
            TimeSpan.FromHours(48));

        // Assert: Should throw because it exceeds MaxPresignedUrlExpiryMinutes (1440 min / 24 hours)
        await act.Should().ThrowAsync<GenesisException>()
            .Where(ex => ex.ErrorCode == "PRESIGNED_URL_EXPIRY_EXCEEDED" ||
                        ex.Message.Contains("exceed", StringComparison.OrdinalIgnoreCase));
    }
}
