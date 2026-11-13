/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using System.Collections.Concurrent;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;

namespace Pervaxis.Genesis.FileStorage.AWS.Tests.Fakes;

/// <summary>
/// A stateful, in-memory stand-in for one S3 bucket, shared by every tenant in a test.
/// It reproduces the S3 behaviour the isolation checks depend on:
/// unknown keys return 404, metadata names come back lower-cased with the
/// <c>x-amz-meta-</c> prefix, and list filters by key prefix.
/// </summary>
internal sealed class InMemoryS3
{
    private const string MetaPrefix = "x-amz-meta-";

    private readonly ConcurrentDictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    public InMemoryS3()
    {
        Mock = new Mock<IAmazonS3>(MockBehavior.Strict);

        Mock.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PutObjectRequest r, CancellationToken _) =>
            {
                using var buffer = new MemoryStream();
                r.InputStream.CopyTo(buffer);
                _objects[r.Key] = new StoredObject(buffer.ToArray(), CaptureMetadata(r.Metadata));
                RequestedKeys.Add(("put", r.Key));
                return new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK };
            });

        Mock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetObjectRequest r, CancellationToken _) =>
            {
                RequestedKeys.Add(("get", r.Key));
                var obj = Find(r.Key);
                var response = new GetObjectResponse
                {
                    ResponseStream = new MemoryStream(obj.Content),
                    ContentLength = obj.Content.Length
                };
                ApplyMetadata(response.Metadata, obj.Metadata);
                return response;
            });

        Mock.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetObjectMetadataRequest r, CancellationToken _) =>
            {
                RequestedKeys.Add(("head", r.Key));
                var obj = Find(r.Key);
                var response = new GetObjectMetadataResponse { ContentLength = obj.Content.Length };
                ApplyMetadata(response.Metadata, obj.Metadata);
                return response;
            });

        Mock.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DeleteObjectRequest r, CancellationToken _) =>
            {
                // Like S3: deleting a missing key succeeds silently.
                RequestedKeys.Add(("delete", r.Key));
                _objects.TryRemove(r.Key, out StoredObject? _);
                return new DeleteObjectResponse();
            });

        Mock.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListObjectsV2Request r, CancellationToken _) =>
            {
                RequestedKeys.Add(("list", r.Prefix ?? string.Empty));
                var prefix = r.Prefix ?? string.Empty;
                return new ListObjectsV2Response
                {
                    S3Objects = _objects.Keys
                        .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                        .OrderBy(k => k, StringComparer.Ordinal)
                        .Select(k => new S3Object { Key = k })
                        .ToList(),
                    IsTruncated = false
                };
            });

        Mock.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns((GetPreSignedUrlRequest r) =>
            {
                RequestedKeys.Add(("presign", r.Key));
                LastPresignRequest = r;
                return $"https://test-bucket.s3.amazonaws.com/{r.Key}?X-Amz-Signature=fake";
            });
    }

    /// <summary>The mocked client. Strict: any S3 call the fake does not model fails the test.</summary>
    public Mock<IAmazonS3> Mock { get; }

    /// <summary>Every (operation, key or prefix) the provider sent to S3, in order.</summary>
    public ConcurrentBag<(string Operation, string Key)> RequestedKeys { get; } = new();

    /// <summary>The most recent presigned URL request.</summary>
    public GetPreSignedUrlRequest? LastPresignRequest { get; private set; }

    /// <summary>All keys currently stored in the bucket.</summary>
    public IReadOnlyCollection<string> Keys => _objects.Keys.ToList();

    /// <summary>
    /// Seeds an object directly, bypassing the provider (for example a legacy object, or another tenant's file).
    /// Metadata names are given without the x-amz-meta- prefix.
    /// </summary>
    public void Seed(string fullKey, string content, IDictionary<string, string>? metadata = null)
    {
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                stored[MetaPrefix + kvp.Key.ToLowerInvariant()] = kvp.Value;
            }
        }

        _objects[fullKey] = new StoredObject(System.Text.Encoding.UTF8.GetBytes(content), stored);
    }

    /// <summary>Returns the stored metadata for a key, with names as S3 would return them.</summary>
    public IReadOnlyDictionary<string, string> MetadataOf(string fullKey) => _objects[fullKey].Metadata;

    /// <summary>Returns the stored content for a key as UTF-8 text.</summary>
    public string ContentOf(string fullKey) => System.Text.Encoding.UTF8.GetString(_objects[fullKey].Content);

    private StoredObject Find(string key) =>
        _objects.TryGetValue(key, out var obj)
            ? obj
            : throw new AmazonS3Exception("Not Found") { StatusCode = HttpStatusCode.NotFound };

    // S3 stores user metadata names lower-cased and returns them with the x-amz-meta- prefix.
    private static Dictionary<string, string> CaptureMetadata(MetadataCollection metadata)
    {
        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in metadata.Keys)
        {
            var normalized = name.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase)
                ? name[MetaPrefix.Length..]
                : name;
            stored[MetaPrefix + normalized.ToLowerInvariant()] = metadata[name];
        }

        return stored;
    }

    private static void ApplyMetadata(MetadataCollection target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var kvp in source)
        {
            target[kvp.Key] = kvp.Value;
        }
    }

    private sealed record StoredObject(byte[] Content, IReadOnlyDictionary<string, string> Metadata);
}
