/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Net;
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
using Pervaxis.Genesis.FileStorage.AWS.Tests.Fakes;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests;

/// <summary>
/// Cross-tenant isolation tests. Both tenants share one bucket (one <see cref="InMemoryS3"/>) and,
/// where it matters, hold an object under the SAME user key with DIFFERENT content. Every operation
/// therefore has a test that fails if that operation stops applying the tenant prefix.
/// </summary>
public sealed class CrossTenantIsolationTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string StampKey = FileStorageConstants.TenantIdMetadataKey;

    private static string KeyOf(string tenant, string userKey) => $"tenants/{tenant}/{userKey}";

    private static Dictionary<string, string> Stamp(string tenant, params (string Key, string Value)[] extra)
    {
        var metadata = new Dictionary<string, string> { [StampKey] = tenant };
        foreach (var (key, value) in extra)
        {
            metadata[key] = value;
        }

        return metadata;
    }

    private static ITenantContext Tenant(string tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(x => x.IsResolved).Returns(true);
        mock.Setup(x => x.TenantId).Returns(new TenantId(tenantId));
        mock.Setup(x => x.TenantName).Returns($"{tenantId}-corp");
        return mock.Object;
    }

    private static FileStorageOptions DefaultOptions() => new()
    {
        Region = "us-east-1",
        BucketName = "test-bucket",
        EnableTenantIsolation = true,
        DefaultPresignedUrlExpiryMinutes = 60,
        MaxPresignedUrlExpiryMinutes = 1440
    };

    private static S3FileStorageProvider Provider(IAmazonS3 s3, string tenantId, FileStorageOptions? options = null)
    {
        var context = Tenant(tenantId);
        var resolver = new TenantKeyResolver(context, Mock.Of<ILogger<TenantKeyResolver>>(), enableTenantIsolation: true);
        return new S3FileStorageProvider(
            Microsoft.Extensions.Options.Options.Create(options ?? DefaultOptions()),
            Mock.Of<ILogger<S3FileStorageProvider>>(),
            s3,
            resolver,
            context);
    }

    private static async Task<string?> ReadAllAsync(Stream? stream)
    {
        if (stream is null)
        {
            return null;
        }

        await using (stream)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
    }

    // ---------------------------------------------------------------- Upload

    [Fact]
    public async Task Upload_SameKeyFromTwoTenantsConcurrently_StoresSeparateObjectsWithOwnContentAndStamp()
    {
        var s3 = new InMemoryS3();
        var providerA = Provider(s3.Mock.Object, TenantA);
        var providerB = Provider(s3.Mock.Object, TenantB);

        // Interleave many uploads of the same user keys from both tenants on one shared client.
        var uploads = Enumerable.Range(0, 25)
            .SelectMany(i => new[] { (providerA, $"docs/file-{i}.txt", $"A-{i}"), (providerB, $"docs/file-{i}.txt", $"B-{i}") })
            .Select(u => Task.Run(async () => await UploadTextAsync(u.Item1, u.Item2, u.Item3)));
        await Task.WhenAll(uploads);

        s3.Keys.Should().HaveCount(50);
        for (var i = 0; i < 25; i++)
        {
            s3.ContentOf(KeyOf(TenantA, $"docs/file-{i}.txt")).Should().Be($"A-{i}");
            s3.ContentOf(KeyOf(TenantB, $"docs/file-{i}.txt")).Should().Be($"B-{i}");
            s3.MetadataOf(KeyOf(TenantA, $"docs/file-{i}.txt"))["x-amz-meta-" + StampKey].Should().Be(TenantA);
            s3.MetadataOf(KeyOf(TenantB, $"docs/file-{i}.txt"))["x-amz-meta-" + StampKey].Should().Be(TenantB);
        }

        static async Task UploadTextAsync(S3FileStorageProvider provider, string key, string text)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            var returned = await provider.UploadAsync(key, stream);
            returned.Should().Be(key, "the API returns the caller's key, never the tenant-prefixed S3 key");
        }
    }

    [Theory]
    [InlineData("pervaxis-tenant-id")]
    [InlineData("PERVAXIS-TENANT-ID")]
    [InlineData("x-amz-meta-pervaxis-tenant-id")]
    [InlineData("X-Amz-Meta-Pervaxis-Tenant-Id")]
    public async Task Upload_CallerMetadataTriesToSetTenantStamp_IsRejectedAndNothingIsStored(string reservedKey)
    {
        var s3 = new InMemoryS3();
        var providerA = Provider(s3.Mock.Object, TenantA);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var act = async () => await providerA.UploadAsync(
            "spoof.pdf",
            stream,
            metadata: new Dictionary<string, string> { [reservedKey] = TenantB });

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.ReservedMetadataKey);
        s3.Keys.Should().BeEmpty("a rejected upload must not reach S3");
    }

    [Theory]
    [InlineData("TenantId")]
    [InlineData("x-amz-meta-tenantname")]
    public async Task Upload_CallerMetadataTriesToSetTenantTag_IsRejected(string reservedKey)
    {
        var s3 = new InMemoryS3();
        var providerA = Provider(s3.Mock.Object, TenantA);
        using var stream = new MemoryStream(new byte[] { 1 });

        var act = async () => await providerA.UploadAsync(
            "spoof.pdf",
            stream,
            metadata: new Dictionary<string, string> { [reservedKey] = TenantB });

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.ReservedMetadataKey);
        s3.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ThenDownload_RoundTripsForOwnerAndIsInvisibleToOtherTenant()
    {
        var s3 = new InMemoryS3();
        var providerA = Provider(s3.Mock.Object, TenantA);
        var providerB = Provider(s3.Mock.Object, TenantB);

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("owned by A")))
        {
            await providerA.UploadAsync("reports/q1.pdf", stream, metadata: new Dictionary<string, string> { ["owner"] = "alice" });
        }

        (await ReadAllAsync(await providerA.DownloadAsync("reports/q1.pdf"))).Should().Be("owned by A");
        (await providerB.DownloadAsync("reports/q1.pdf")).Should().BeNull();
        (await providerB.ExistsAsync("reports/q1.pdf")).Should().BeFalse();
    }

    // -------------------------------------------------------------- Download

    [Fact]
    public async Task Download_SameKeyInBothTenants_EachTenantGetsOnlyItsOwnContent()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "report.pdf"), "A content", Stamp(TenantA));
        s3.Seed(KeyOf(TenantB, "report.pdf"), "B content", Stamp(TenantB));

        (await ReadAllAsync(await Provider(s3.Mock.Object, TenantA).DownloadAsync("report.pdf"))).Should().Be("A content");
        (await ReadAllAsync(await Provider(s3.Mock.Object, TenantB).DownloadAsync("report.pdf"))).Should().Be("B content");
    }

    [Fact]
    public async Task Download_KeyExistsOnlyForOtherTenant_ReturnsNullAndOnlyRequestsOwnPrefix()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantB, "secrets/confidential.pdf"), "B secret", Stamp(TenantB));

        var result = await Provider(s3.Mock.Object, TenantA).DownloadAsync("secrets/confidential.pdf");

        result.Should().BeNull();
        s3.RequestedKeys.Should().ContainSingle()
            .Which.Should().Be(("get", KeyOf(TenantA, "secrets/confidential.pdf")));
    }

    [Fact]
    public async Task Download_ObjectUnderOwnPrefixStampedForOtherTenant_IsRejected()
    {
        // e.g. an object copied into the wrong tenant folder by an out-of-band tool.
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "suspicious.pdf"), "B data", Stamp(TenantB));

        var act = () => Provider(s3.Mock.Object, TenantA).DownloadAsync("suspicious.pdf");

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
    }

    [Fact]
    public async Task Download_LegacyObjectWithoutStamp_IsRejected()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "legacy.pdf"), "pre-stamp object");

        var act = () => Provider(s3.Mock.Object, TenantA).DownloadAsync("legacy.pdf");

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantStampMissing);
    }

    [Theory]
    [InlineData("x-amz-meta-pervaxis-tenant-id")]
    [InlineData("X-Amz-Meta-Pervaxis-Tenant-Id")]
    [InlineData("pervaxis-tenant-id")]
    public async Task Download_StampNameInAnyCaseOrPrefixForm_IsRecognised(string stampName)
    {
        // S3 lower-cases metadata names and returns them with the x-amz-meta- prefix.
        // The check must not depend on the exact casing or prefix form.
        var s3 = new Mock<IAmazonS3>();
        var response = new GetObjectResponse { ResponseStream = new MemoryStream("ok"u8.ToArray()) };
        response.Metadata[stampName] = TenantA;
        s3.Setup(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r => r.Key == KeyOf(TenantA, "file.txt")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        (await ReadAllAsync(await Provider(s3.Object, TenantA).DownloadAsync("file.txt"))).Should().Be("ok");
    }

    // ---------------------------------------------------------------- Exists

    [Fact]
    public async Task Exists_KeyOnlyInOtherTenant_IsFalseForCallerAndTrueForOwner()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantB, "only-b.txt"), "B", Stamp(TenantB));

        (await Provider(s3.Mock.Object, TenantA).ExistsAsync("only-b.txt")).Should().BeFalse();
        (await Provider(s3.Mock.Object, TenantB).ExistsAsync("only-b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_ObjectStampedForOtherTenant_IsRejected()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "planted.txt"), "B", Stamp(TenantB));

        var act = () => Provider(s3.Mock.Object, TenantA).ExistsAsync("planted.txt");

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
    }

    [Fact]
    public async Task Exists_LegacyObjectWithoutStamp_IsRejected()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "legacy.txt"), "old");

        var act = () => Provider(s3.Mock.Object, TenantA).ExistsAsync("legacy.txt");

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantStampMissing);
    }

    // ----------------------------------------------------------- GetMetadata

    [Fact]
    public async Task GetMetadata_SameKeyInBothTenants_ReturnsOwnMetadataAndHidesStamp()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "data.json"), "A", Stamp(TenantA, ("owner", "alice")));
        s3.Seed(KeyOf(TenantB, "data.json"), "B", Stamp(TenantB, ("owner", "bob")));

        var metadataA = await Provider(s3.Mock.Object, TenantA).GetMetadataAsync("data.json");

        metadataA.Values.Should().Contain("alice").And.NotContain("bob");
        metadataA.Keys.Should().NotContain(k => k.Contains(StampKey, StringComparison.OrdinalIgnoreCase),
            "the isolation stamp is internal and is not returned to callers");
    }

    [Fact]
    public async Task GetMetadata_KeyOnlyInOtherTenant_ThrowsNotFound()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantB, "data.json"), "B", Stamp(TenantB, ("owner", "bob")));

        var act = () => Provider(s3.Mock.Object, TenantA).GetMetadataAsync("data.json");

        await act.Should().ThrowAsync<GenesisException>().WithMessage("*not found*");
        s3.RequestedKeys.Should().ContainSingle().Which.Should().Be(("head", KeyOf(TenantA, "data.json")));
    }

    [Fact]
    public async Task GetMetadata_ObjectStampedForOtherTenant_IsRejected()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "planted.json"), "B", Stamp(TenantB, ("owner", "bob")));

        var act = () => Provider(s3.Mock.Object, TenantA).GetMetadataAsync("planted.json");

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
    }

    [Fact]
    public async Task GetMetadata_LegacyObjectWithoutStamp_IsRejected()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "legacy.json"), "old", new Dictionary<string, string> { ["owner"] = "alice" });

        var act = () => Provider(s3.Mock.Object, TenantA).GetMetadataAsync("legacy.json");

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantStampMissing);
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public async Task Delete_SameKeyInBothTenants_RemovesOnlyCallersObject()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "target.pdf"), "A", Stamp(TenantA));
        s3.Seed(KeyOf(TenantB, "target.pdf"), "B", Stamp(TenantB));

        var deleted = await Provider(s3.Mock.Object, TenantA).DeleteAsync("target.pdf");

        deleted.Should().BeTrue();
        s3.Keys.Should().BeEquivalentTo(new[] { KeyOf(TenantB, "target.pdf") });
    }

    // ------------------------------------------------------------------ List

    [Fact]
    public async Task List_SharedBucket_ReturnsOnlyCallersKeysWithPrefixStripped()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "a1.pdf"), "A", Stamp(TenantA));
        s3.Seed(KeyOf(TenantA, "docs/a2.pdf"), "A", Stamp(TenantA));
        s3.Seed(KeyOf(TenantB, "b1.pdf"), "B", Stamp(TenantB));
        s3.Seed("unscoped-root.pdf", "legacy");

        var files = (await Provider(s3.Mock.Object, TenantA).ListAsync(prefix: null)).ToList();

        files.Should().BeEquivalentTo("a1.pdf", "docs/a2.pdf");
    }

    [Fact]
    public async Task List_WithUserPrefix_StaysInsideCallersTenant()
    {
        var s3 = new InMemoryS3();
        s3.Seed(KeyOf(TenantA, "docs/a.pdf"), "A", Stamp(TenantA));
        s3.Seed(KeyOf(TenantB, "docs/b.pdf"), "B", Stamp(TenantB));

        var files = (await Provider(s3.Mock.Object, TenantA).ListAsync("docs/")).ToList();

        files.Should().BeEquivalentTo("docs/a.pdf");
        s3.RequestedKeys.Should().ContainSingle().Which.Should().Be(("list", KeyOf(TenantA, "docs/")));
    }

    [Fact]
    public async Task List_S3ReturnsKeyOutsideTenantPrefix_ThrowsInsteadOfReturningIt()
    {
        // Defends against a misbehaving S3 endpoint or a bug that ignores the requested prefix.
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = new List<S3Object>
                {
                    new() { Key = KeyOf(TenantA, "mine.pdf") },
                    new() { Key = KeyOf(TenantB, "theirs.pdf") }
                },
                IsTruncated = false
            });

        var act = () => Provider(s3.Object, TenantA).ListAsync();

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
    }

    [Fact]
    public async Task List_EmptyListingWithNullS3Objects_ReturnsEmpty()
    {
        // AWS SDK v4 returns S3Objects == null (not an empty list) when nothing matches the prefix.
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = null, IsTruncated = false });

        var files = await Provider(s3.Object, TenantA).ListAsync();

        files.Should().BeEmpty();
    }

    [Fact]
    public async Task List_PageWithNullS3Objects_IsSkippedAndPagingContinues()
    {
        var s3 = new Mock<IAmazonS3>();
        var pages = new Queue<ListObjectsV2Response>(new[]
        {
            new ListObjectsV2Response { S3Objects = null, IsTruncated = true, NextContinuationToken = "page-2" },
            new ListObjectsV2Response
            {
                S3Objects = new List<S3Object> { new() { Key = KeyOf(TenantA, "late.pdf") } },
                IsTruncated = false
            }
        });
        s3.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => pages.Dequeue());

        var files = await Provider(s3.Object, TenantA).ListAsync();

        files.Should().BeEquivalentTo("late.pdf");
    }

    [Fact]
    public async Task List_ContinuationPages_EveryPageRequestKeepsTenantPrefix()
    {
        var s3 = new Mock<IAmazonS3>();
        var requests = new List<(string Prefix, string? Token)>();
        var pages = new Queue<ListObjectsV2Response>(new[]
        {
            new ListObjectsV2Response
            {
                S3Objects = new List<S3Object> { new() { Key = KeyOf(TenantA, "p1.pdf") } },
                IsTruncated = true,
                NextContinuationToken = "page-2"
            },
            new ListObjectsV2Response
            {
                S3Objects = new List<S3Object> { new() { Key = KeyOf(TenantA, "p2.pdf") } },
                IsTruncated = false
            }
        });
        s3.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((r, _) => requests.Add((r.Prefix, r.ContinuationToken)))
            .ReturnsAsync(() => pages.Dequeue());

        var files = (await Provider(s3.Object, TenantA).ListAsync()).ToList();

        files.Should().BeEquivalentTo("p1.pdf", "p2.pdf");
        requests.Should().Equal(
            (KeyOf(TenantA, string.Empty), (string?)null),
            (KeyOf(TenantA, string.Empty), "page-2"));
    }

    // ------------------------------------------------------------- Presigned

    [Fact]
    public async Task PresignedUrl_IsSignedForCallersTenantPrefixedKey()
    {
        // A presigned URL downloads straight from S3 and skips the stamp check,
        // so the tenant-prefixed key is the only protection.
        var s3 = new InMemoryS3();

        var url = await Provider(s3.Mock.Object, TenantA).GetPresignedUrlAsync("report.pdf", TimeSpan.FromMinutes(60));

        s3.LastPresignRequest!.Key.Should().Be(KeyOf(TenantA, "report.pdf"));
        url.Should().Contain(KeyOf(TenantA, "report.pdf"));
    }

    [Fact]
    public async Task PresignedUrl_AboveMaximum_ThrowsAndNeverSigns()
    {
        var s3 = new InMemoryS3();

        var act = () => Provider(s3.Mock.Object, TenantA).GetPresignedUrlAsync("report.pdf", TimeSpan.FromMinutes(1441));

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.PresignedUrlExpiryExceeded);
        s3.LastPresignRequest.Should().BeNull("an over-limit request must not be silently clamped and signed");
    }

    [Fact]
    public async Task PresignedUrl_AtConfiguredMaximum_IsAllowed()
    {
        var s3 = new InMemoryS3();
        var before = DateTime.UtcNow;

        await Provider(s3.Mock.Object, TenantA).GetPresignedUrlAsync("report.pdf", TimeSpan.FromMinutes(1440));

        s3.LastPresignRequest!.Expires.Should().BeCloseTo(before.AddMinutes(1440), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task PresignedUrl_LowerConfiguredMaximum_IsEnforced()
    {
        var s3 = new InMemoryS3();
        var options = DefaultOptions();
        options.MaxPresignedUrlExpiryMinutes = 60;

        var act = () => Provider(s3.Mock.Object, TenantA, options).GetPresignedUrlAsync("report.pdf", TimeSpan.FromMinutes(61));

        (await act.Should().ThrowAsync<GenesisException>())
            .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.PresignedUrlExpiryExceeded);
    }

    [Theory]
    [InlineData(1441)]
    [InlineData(10080)]
    public void Options_MaximumAboveAbsoluteCeiling_FailsValidation(int maxMinutes)
    {
        var options = DefaultOptions();
        options.MaxPresignedUrlExpiryMinutes = maxMinutes;

        var act = () => options.Validate();

        act.Should().Throw<GenesisConfigurationException>()
            .WithMessage($"*{nameof(FileStorageOptions.MaxPresignedUrlExpiryMinutes)}*");
    }

    [Fact]
    public void Options_DefaultPresignedExpiry_IsSixtyMinutes()
    {
        new FileStorageOptions().DefaultPresignedUrlExpiryMinutes.Should().Be(60);
        new FileStorageOptions().MaxPresignedUrlExpiryMinutes.Should().Be(1440);
    }

    // ------------------------------------------------------------- KeyPrefix

    [Fact]
    public async Task KeyPrefix_IsAppliedOutsideTenantPrefixForWritesAndLists()
    {
        var s3 = new InMemoryS3();
        var options = DefaultOptions();
        options.KeyPrefix = "prod/";
        var providerA = Provider(s3.Mock.Object, TenantA, options);

        using (var stream = new MemoryStream(new byte[] { 1 }))
        {
            await providerA.UploadAsync("x.pdf", stream);
        }

        s3.Keys.Should().BeEquivalentTo(new[] { "prod/" + KeyOf(TenantA, "x.pdf") });
        (await providerA.ListAsync()).Should().BeEquivalentTo("x.pdf");
        (await providerA.ExistsAsync("x.pdf")).Should().BeTrue();
    }

    // ---------------------------------------------------------- Unresolved

    [Fact]
    public async Task UnresolvedTenant_EveryOperationFailsClosedWithoutCallingS3()
    {
        var s3 = new InMemoryS3();
        var context = new Mock<ITenantContext>();
        context.Setup(x => x.IsResolved).Returns(false);
        var resolver = new TenantKeyResolver(context.Object, Mock.Of<ILogger<TenantKeyResolver>>(), enableTenantIsolation: true);
        var provider = new S3FileStorageProvider(
            Microsoft.Extensions.Options.Options.Create(DefaultOptions()),
            Mock.Of<ILogger<S3FileStorageProvider>>(),
            s3.Mock.Object,
            resolver,
            context.Object);
        using var stream = new MemoryStream(new byte[] { 1 });

        var operations = new Func<Task>[]
        {
            async () => await provider.UploadAsync("k", stream),
            () => provider.DownloadAsync("k"),
            () => provider.DeleteAsync("k"),
            () => provider.ExistsAsync("k"),
            () => provider.GetMetadataAsync("k"),
            () => provider.ListAsync(),
            () => provider.GetPresignedUrlAsync("k", TimeSpan.FromMinutes(5))
        };

        foreach (var operation in operations)
        {
            (await operation.Should().ThrowAsync<GenesisException>())
                .Which.ErrorCode.Should().Be(FileStorageConstants.FailureReasons.TenantContextNotResolved);
        }

        s3.RequestedKeys.Should().BeEmpty();
    }
}
