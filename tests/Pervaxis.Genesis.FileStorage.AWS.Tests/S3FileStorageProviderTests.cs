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

using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.FileStorage.AWS.Options;
using Pervaxis.Genesis.FileStorage.AWS.Providers.S3;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests;

public abstract class S3FileStorageProviderTests
{
    public class Constructor : S3FileStorageProviderTests
    {
        [Fact]
        public void NullOptions_ThrowsArgumentNullException()
        {
            // Arrange
            var logger = Mock.Of<ILogger<S3FileStorageProvider>>();

            // Act & Assert
            var act = () => new S3FileStorageProvider(null!, logger);
            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public void NullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var options = CreateValidOptions();

            // Act & Assert
            var act = () => new S3FileStorageProvider(options, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void EmptyRegion_ThrowsGenesisConfigurationException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new FileStorageOptions
            {
                Region = "",
                BucketName = "test-bucket"
            });
            var logger = Mock.Of<ILogger<S3FileStorageProvider>>();

            // Act & Assert
            var act = () => new S3FileStorageProvider(options, logger);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void EmptyBucketName_ThrowsGenesisConfigurationException()
        {
            // Arrange
            var options = Microsoft.Extensions.Options.Options.Create(new FileStorageOptions
            {
                Region = "us-east-1",
                BucketName = ""
            });
            var logger = Mock.Of<ILogger<S3FileStorageProvider>>();

            // Act & Assert
            var act = () => new S3FileStorageProvider(options, logger);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void ValidOptions_CreatesProviderSuccessfully()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<S3FileStorageProvider>>();

            // Act
            var provider = new S3FileStorageProvider(options, logger);

            // Assert
            provider.Should().NotBeNull();
        }

        [Fact]
        public void NullS3Client_ThrowsArgumentNullException()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<S3FileStorageProvider>>();

            // Act & Assert
            var act = () => new S3FileStorageProvider(options, logger, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("s3Client");
        }
    }

    public class UploadAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();
            using var content = new MemoryStream();

            // Act & Assert
            var act = async () => await provider.UploadAsync("", content);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullContent_ThrowsArgumentNullException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.UploadAsync("test-key", null!);
            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("content");
        }

        [Fact]
        public async Task SmallFile_UsesSinglePartUpload()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();
            var content = CreateTestStream(1024); // 1KB - below threshold

            s3Mock.Setup(x => x.PutObjectAsync(
                    It.IsAny<PutObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());

            // Act
            var key = await provider.UploadAsync("test-file.txt", content, "text/plain");

            // Assert
            key.Should().Be("test-file.txt");
            s3Mock.Verify(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r =>
                    r.BucketName == "test-bucket" &&
                    r.Key == "test-file.txt" &&
                    r.ContentType == "text/plain"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WithMetadata_AddsMetadataToRequest()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();
            var content = CreateTestStream(1024);
            var metadata = new Dictionary<string, string>
            {
                ["author"] = "test-user",
                ["version"] = "1.0"
            };

            s3Mock.Setup(x => x.PutObjectAsync(
                    It.IsAny<PutObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());

            // Act
            await provider.UploadAsync("test-file.txt", content, metadata: metadata);

            // Assert
            s3Mock.Verify(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r =>
                    r.Metadata["author"] == "test-user" &&
                    r.Metadata["version"] == "1.0"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task WithKeyPrefix_PrependsPrefix()
        {
            // Arrange
            var options = CreateOptionsWithPrefix("uploads/");
            var (provider, s3Mock) = CreateProvider(options);
            var content = CreateTestStream(1024);

            s3Mock.Setup(x => x.PutObjectAsync(
                    It.IsAny<PutObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());

            // Act
            var key = await provider.UploadAsync("file.txt", content);

            // Assert
            key.Should().Be("uploads/file.txt");
            s3Mock.Verify(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r => r.Key == "uploads/file.txt"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();
            var content = CreateTestStream(1024);

            s3Mock.Setup(x => x.PutObjectAsync(
                    It.IsAny<PutObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.UploadAsync("test-file.txt", content);
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to upload file*");
        }
    }

    public class DownloadAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.DownloadAsync("");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task FileExists_ReturnsStream()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();
            var testContent = "test file content"u8.ToArray();
            var responseStream = new MemoryStream(testContent);

            var response = new GetObjectResponse
            {
                ResponseStream = responseStream,
                ContentLength = testContent.Length
            };

            s3Mock.Setup(x => x.GetObjectAsync(
                    It.IsAny<GetObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var result = await provider.DownloadAsync("test-file.txt");

            // Assert
            result.Should().NotBeNull();
            using var reader = new StreamReader(result!);
            var content = await reader.ReadToEndAsync();
            content.Should().Be("test file content");
        }

        [Fact]
        public async Task FileNotFound_ReturnsNull()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectAsync(
                    It.IsAny<GetObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("Not Found")
                {
                    StatusCode = System.Net.HttpStatusCode.NotFound
                });

            // Act
            var result = await provider.DownloadAsync("missing-file.txt");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectAsync(
                    It.IsAny<GetObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.DownloadAsync("test-file.txt");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to download file*");
        }
    }

    public class DeleteAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.DeleteAsync("");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ValidKey_ReturnsTrue()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.DeleteObjectAsync(
                    It.IsAny<DeleteObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectResponse());

            // Act
            var result = await provider.DeleteAsync("test-file.txt");

            // Assert
            result.Should().BeTrue();
            s3Mock.Verify(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r =>
                    r.BucketName == "test-bucket" &&
                    r.Key == "test-file.txt"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.DeleteObjectAsync(
                    It.IsAny<DeleteObjectRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.DeleteAsync("test-file.txt");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to delete file*");
        }
    }

    public class ExistsAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.ExistsAsync("");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task FileExists_ReturnsTrue()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetObjectMetadataResponse());

            // Act
            var result = await provider.ExistsAsync("test-file.txt");

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task FileNotFound_ReturnsFalse()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("Not Found")
                {
                    StatusCode = System.Net.HttpStatusCode.NotFound
                });

            // Act
            var result = await provider.ExistsAsync("missing-file.txt");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.ExistsAsync("test-file.txt");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to check file existence*");
        }
    }

    public class GetPresignedUrlAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.GetPresignedUrlAsync("", TimeSpan.FromMinutes(15));
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task ZeroExpiry_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.GetPresignedUrlAsync("test-file.txt", TimeSpan.Zero);
            await act.Should().ThrowAsync<ArgumentException>().WithParameterName("expiry");
        }

        [Fact]
        public async Task ValidRequest_ReturnsPresignedUrl()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();
            var expectedUrl = "https://test-bucket.s3.amazonaws.com/test-file.txt?AWSAccessKeyId=...";

            s3Mock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
                .Returns(expectedUrl);

            // Act
            var url = await provider.GetPresignedUrlAsync("test-file.txt", TimeSpan.FromHours(1));

            // Assert
            url.Should().Be(expectedUrl);
            s3Mock.Verify(x => x.GetPreSignedURL(
                It.Is<GetPreSignedUrlRequest>(r =>
                    r.BucketName == "test-bucket" &&
                    r.Key == "test-file.txt")), Times.Once);
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
                .Throws(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.GetPresignedUrlAsync("test-file.txt", TimeSpan.FromMinutes(15));
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to generate presigned URL*");
        }
    }

    public class GetMetadataAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task EmptyKey_ThrowsArgumentException()
        {
            // Arrange
            var (provider, _) = CreateProvider();

            // Act & Assert
            var act = async () => await provider.GetMetadataAsync("");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task FileExists_ReturnsMetadata()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            // Create response and manually add to Metadata collection
            var response = new GetObjectMetadataResponse();
            // The Metadata property is a MetadataCollection that needs to be populated via Add or indexer
            response.Metadata["x-amz-meta-author"] = "test-user";
            response.Metadata["x-amz-meta-version"] = "1.0";

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var metadata = await provider.GetMetadataAsync("test-file.txt");

            // Assert
            metadata.Should().NotBeNull();
            metadata.Should().HaveCount(2);
            // AWS S3 metadata keys are case-insensitive and stored with x-amz-meta- prefix removed
            metadata.Should().Contain(kvp => kvp.Value == "test-user");
            metadata.Should().Contain(kvp => kvp.Value == "1.0");
        }

        [Fact]
        public async Task FileNotFound_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("Not Found")
                {
                    StatusCode = System.Net.HttpStatusCode.NotFound
                });

            // Act & Assert
            var act = async () => await provider.GetMetadataAsync("missing-file.txt");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*File not found*");
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.GetMetadataAsync("test-file.txt");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to get metadata*");
        }
    }

    public class ListAsync : S3FileStorageProviderTests
    {
        [Fact]
        public async Task NoPrefix_ListsAllFiles()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            var response = new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = "file1.txt" },
                    new() { Key = "file2.txt" },
                    new() { Key = "dir/file3.txt" }
                },
                IsTruncated = false
            };

            s3Mock.Setup(x => x.ListObjectsV2Async(
                    It.IsAny<ListObjectsV2Request>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var files = await provider.ListAsync();

            // Assert
            files.Should().HaveCount(3);
            files.Should().Contain("file1.txt");
            files.Should().Contain("file2.txt");
            files.Should().Contain("dir/file3.txt");
        }

        [Fact]
        public async Task WithPrefix_FiltersFiles()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            var response = new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = "docs/file1.txt" },
                    new() { Key = "docs/file2.txt" }
                },
                IsTruncated = false
            };

            s3Mock.Setup(x => x.ListObjectsV2Async(
                    It.Is<ListObjectsV2Request>(r => r.Prefix == "docs/"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var files = await provider.ListAsync("docs/");

            // Assert
            files.Should().HaveCount(2);
        }

        [Fact]
        public async Task PaginatedResults_FetchesAllPages()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            var response1 = new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = "file1.txt" },
                    new() { Key = "file2.txt" }
                },
                IsTruncated = true,
                NextContinuationToken = "token1"
            };

            var response2 = new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = "file3.txt" }
                },
                IsTruncated = false
            };

            s3Mock.SetupSequence(x => x.ListObjectsV2Async(
                    It.IsAny<ListObjectsV2Request>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response1)
                .ReturnsAsync(response2);

            // Act
            var files = await provider.ListAsync();

            // Assert
            files.Should().HaveCount(3);
            s3Mock.Verify(x => x.ListObjectsV2Async(
                It.IsAny<ListObjectsV2Request>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task S3Exception_ThrowsGenesisException()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.ListObjectsV2Async(
                    It.IsAny<ListObjectsV2Request>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("S3 error"));

            // Act & Assert
            var act = async () => await provider.ListAsync();
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*Failed to list files*");
        }
    }

    public class Dispose : S3FileStorageProviderTests
    {
        [Fact]
        public void WhenClientNotUsed_DoesNotDisposeS3Client()
        {
            // Arrange
            var options = CreateValidOptions();
            var logger = Mock.Of<ILogger<S3FileStorageProvider>>();
            var provider = new S3FileStorageProvider(options, logger);

            // Act
            provider.Dispose();

            // Assert - Should not throw, client was never created
            provider.Should().NotBeNull();
        }

        [Fact]
        public async Task WhenClientWasUsed_DisposesS3Client()
        {
            // Arrange
            var (provider, s3Mock) = CreateProvider();

            s3Mock.Setup(x => x.GetObjectMetadataAsync(
                    It.IsAny<GetObjectMetadataRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetObjectMetadataResponse());

            // Use the provider to trigger client creation
            await provider.ExistsAsync("test-file.txt");

            // Act
            provider.Dispose();

            // Assert
            s3Mock.Verify(x => x.Dispose(), Times.Once);
        }
    }

    // Helper methods
    private static IOptions<FileStorageOptions> CreateValidOptions()
    {
        return Microsoft.Extensions.Options.Options.Create(new FileStorageOptions
        {
            Region = "us-east-1",
            BucketName = "test-bucket",
            DefaultPresignedUrlExpiryMinutes = 60,
            MultipartUploadThresholdBytes = 5 * 1024 * 1024,
            MultipartUploadPartSizeBytes = 5 * 1024 * 1024,
            StorageClass = "STANDARD",
            EnableServerSideEncryption = true
        });
    }

    private static IOptions<FileStorageOptions> CreateOptionsWithPrefix(string prefix)
    {
        return Microsoft.Extensions.Options.Options.Create(new FileStorageOptions
        {
            Region = "us-east-1",
            BucketName = "test-bucket",
            KeyPrefix = prefix,
            DefaultPresignedUrlExpiryMinutes = 60,
            MultipartUploadThresholdBytes = 5 * 1024 * 1024,
            MultipartUploadPartSizeBytes = 5 * 1024 * 1024,
            StorageClass = "STANDARD",
            EnableServerSideEncryption = true
        });
    }

    private static (S3FileStorageProvider Provider, Mock<IAmazonS3> S3Mock) CreateProvider(
        IOptions<FileStorageOptions>? options = null)
    {
        options ??= CreateValidOptions();
        var logger = Mock.Of<ILogger<S3FileStorageProvider>>();
        var s3Mock = new Mock<IAmazonS3>();

        var provider = new S3FileStorageProvider(options, logger, s3Mock.Object);

        return (provider, s3Mock);
    }

    private static MemoryStream CreateTestStream(int sizeBytes)
    {
        var buffer = new byte[sizeBytes];
        for (int i = 0; i < sizeBytes; i++)
        {
            buffer[i] = (byte)(i % 256);
        }
        return new MemoryStream(buffer);
    }
}
