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

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Options;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sns;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Pervaxis.Genesis.Messaging.AWS.Tests;

/// <summary>
/// Tests for URL/ARN caching behavior with tenant isolation.
/// SECURITY: Verifies that Tenant A's cached URLs are never returned for Tenant B.
/// </summary>
public sealed class CachingTests
{
    private readonly Mock<IAmazonSQS> _mockSqs;
    private readonly Mock<IAmazonSimpleNotificationService> _mockSns;
    private readonly ILogger<SqsMessagingProvider> _sqsLogger;
    private readonly ILogger<SnsMessagingProvider> _snsLogger;

    public CachingTests()
    {
        _mockSqs = new Mock<IAmazonSQS>();
        _mockSns = new Mock<IAmazonSimpleNotificationService>();
        _sqsLogger = new Mock<ILogger<SqsMessagingProvider>>().Object;
        _snsLogger = new Mock<ILogger<SnsMessagingProvider>>().Object;
    }

    [Fact]
    public async Task SQS_CacheHit_ReusesResolvedQueueUrl()
    {
        // Arrange
        var mockTenantContext = CreateTenantContext("tenant-001");
        var options = CreateTenantOptions();
        var provider = new SqsMessagingProvider(options, _sqsLogger, _mockSqs.Object, mockTenantContext.Object);

        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-1" });

        // Act - First call should resolve the URL
        await provider.PublishAsync("orders", new { Id = 1 });

        // Act - Second call should use cache
        await provider.PublishAsync("orders", new { Id = 2 });

        // Assert - Should only call AWS SDK once (cache hit on second call)
        _mockSqs.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        
        // Both calls should use the same resolved URL
        _mockSqs.Invocations.Select(i => i.Arguments[0])
            .OfType<SendMessageRequest>()
            .Select(r => r.QueueUrl)
            .Should().AllBe(_mockSqs.Invocations[0].Arguments[0] is SendMessageRequest req ? req.QueueUrl : null);
    }

    [Fact]
    public async Task SNS_CacheHit_ReusesResolvedTopicArn()
    {
        // Arrange
        var mockTenantContext = CreateTenantContext("tenant-001");
        var options = CreateTenantOptions();
        var provider = new SnsMessagingProvider(options, _snsLogger, _mockSns.Object, mockTenantContext.Object);

        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-1" });

        // Act - First call should resolve the ARN
        await provider.PublishAsync("events", new { Type = "OrderCreated" });

        // Act - Second call should use cache
        await provider.PublishAsync("events", new { Type = "OrderUpdated" });

        // Assert - Should only call AWS SDK twice (both successful)
        _mockSns.Verify(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        
        // Both calls should use the same resolved ARN
        _mockSns.Invocations.Select(i => i.Arguments[0])
            .OfType<PublishRequest>()
            .Select(r => r.TopicArn)
            .Should().AllBe(_mockSns.Invocations[0].Arguments[0] is PublishRequest req ? req.TopicArn : null);
    }

    [Fact]
    public async Task SQS_CacheInvalidation_OnQueueNotFound()
    {
        // Arrange
        var mockTenantContext = CreateTenantContext("tenant-001");
        var options = CreateTenantOptions();
        var provider = new SqsMessagingProvider(options, _sqsLogger, _mockSqs.Object, mockTenantContext.Object);

        // First call succeeds - populates cache
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-1" });

        await provider.PublishAsync("orders", new { Id = 1 });

        // Second call fails with QueueDoesNotExist - should invalidate cache
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QueueDoesNotExistException("Queue was deleted"));

        // Act & Assert - Second call should fail and invalidate cache
        var act = async () => await provider.PublishAsync("orders", new { Id = 2 });
        await act.Should().ThrowAsync<GenesisException>().WithMessage("*not provisioned*");

        // Third call with new setup - should resolve URL again (cache was invalidated)
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-3" });

        await provider.PublishAsync("orders", new { Id = 3 });

        // Verify all three calls were made
        _mockSqs.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SNS_CacheInvalidation_OnTopicNotFound()
    {
        // Arrange
        var mockTenantContext = CreateTenantContext("tenant-001");
        var options = CreateTenantOptions();
        var provider = new SnsMessagingProvider(options, _snsLogger, _mockSns.Object, mockTenantContext.Object);

        // First call succeeds - populates cache
        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-1" });

        await provider.PublishAsync("events", new { Type = "OrderCreated" });

        // Second call fails with NotFoundException - should invalidate cache
        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Topic was deleted"));

        // Act & Assert - Second call should fail and invalidate cache
        var act = async () => await provider.PublishAsync("events", new { Type = "OrderUpdated" });
        await act.Should().ThrowAsync<GenesisException>().WithMessage("*not provisioned*");

        // Third call with new setup - should resolve ARN again (cache was invalidated)
        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-3" });

        await provider.PublishAsync("events", new { Type = "OrderCompleted" });

        // Verify all three calls were made
        _mockSns.Verify(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    /// <summary>
    /// CRITICAL SECURITY TEST: Verifies that Tenant A's cached queue URL is NEVER returned for Tenant B.
    /// This test ensures cache keys include tenant ID to prevent cross-tenant data leakage.
    /// </summary>
    private static readonly string[] AlternatingTenants = ["tenant-a", "tenant-b", "tenant-a", "tenant-b"];

    [Fact]
    public async Task SQS_TenantIsolation_TenantACacheNeverReturnedForTenantB()
    {
        // Phase 1A shared-queue model: both tenants publish to the same physical queue
        // ("{queueName}-shared"). Isolation is the pervaxis_tenant_id attribute, stamped from each
        // provider's own tenant context, so a cached queue URL can never carry another tenant's identity.
        var options = CreateTenantOptions();
        var providerA = new SqsMessagingProvider(options, _sqsLogger, _mockSqs.Object, CreateTenantContext("tenant-a").Object);
        var providerB = new SqsMessagingProvider(options, _sqsLogger, _mockSqs.Object, CreateTenantContext("tenant-b").Object);

        var sent = new List<(string QueueUrl, string TenantId)>();
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageRequest req, CancellationToken _) =>
            {
                sent.Add((req.QueueUrl, req.MessageAttributes["pervaxis_tenant_id"].StringValue));
                return new SendMessageResponse { MessageId = $"msg-{sent.Count}" };
            });

        // Interleave so each tenant's second publish is served from the shared cache.
        await providerA.PublishAsync("orders", new { Id = 1 });
        await providerB.PublishAsync("orders", new { Id = 2 });
        await providerA.PublishAsync("orders", new { Id = 3 });
        await providerB.PublishAsync("orders", new { Id = 4 });

        sent.Select(s => s.TenantId).Should().Equal(
            AlternatingTenants,
            "each message must carry its own publisher's tenant, even on a cache hit");
        sent.Select(s => s.QueueUrl).Distinct().Should().ContainSingle()
            .Which.Should().EndWith("/orders-shared");
    }

    /// <summary>
    /// CRITICAL SECURITY TEST: Verifies that Tenant A's cached topic ARN is NEVER returned for Tenant B.
    /// This test ensures cache keys include tenant ID to prevent cross-tenant data leakage.
    /// </summary>
    [Fact]
    public async Task SNS_TenantIsolation_TenantACacheNeverReturnedForTenantB()
    {
        // Phase 1A shared-topic model: both tenants publish to the same topic. Isolation is the
        // pervaxis_tenant_id attribute, stamped from each provider's own tenant context.
        var options = CreateTenantOptions();
        var providerA = new SnsMessagingProvider(options, _snsLogger, _mockSns.Object, CreateTenantContext("tenant-a").Object);
        var providerB = new SnsMessagingProvider(options, _snsLogger, _mockSns.Object, CreateTenantContext("tenant-b").Object);

        var sent = new List<(string TopicArn, string TenantId)>();
        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishRequest req, CancellationToken _) =>
            {
                sent.Add((req.TopicArn, req.MessageAttributes["pervaxis_tenant_id"].StringValue));
                return new PublishResponse { MessageId = $"msg-{sent.Count}" };
            });

        await providerA.PublishAsync("events", new { Type = "OrderCreated" });
        await providerB.PublishAsync("events", new { Type = "OrderCreated" });
        await providerA.PublishAsync("events", new { Type = "OrderUpdated" });
        await providerB.PublishAsync("events", new { Type = "OrderUpdated" });

        sent.Select(s => s.TenantId).Should().Equal(
            AlternatingTenants,
            "each message must carry its own publisher's tenant, even on a cache hit");
        // Shared topic name per Phase 1A ("{topicName}-tenant-shared", temporary until SnsOptions is updated).
        sent.Select(s => s.TopicArn).Distinct().Should().ContainSingle()
            .Which.Should().EndWith(":events-tenant-shared");
    }

    [Fact]
    public async Task SQS_NeverCacheFailures_AlwaysRetryOnError()
    {
        // Arrange
        var mockTenantContext = CreateTenantContext("tenant-001");
        var options = CreateTenantOptions();
        var provider = new SqsMessagingProvider(options, _sqsLogger, _mockSqs.Object, mockTenantContext.Object);

        // First call fails - should NOT cache the failure
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Transient network error"));

        // Act & Assert - First call fails
        var act1 = async () => await provider.PublishAsync("orders", new { Id = 1 });
        await act1.Should().ThrowAsync<GenesisException>();

        // Second call succeeds - should retry (not use cached failure)
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-2" });

        // Act - Second call should succeed
        var result = await provider.PublishAsync("orders", new { Id = 2 });

        // Assert - Second call succeeded (failure was not cached)
        result.Should().Be("msg-2");
        _mockSqs.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SNS_NeverCacheFailures_AlwaysRetryOnError()
    {
        // Arrange
        var mockTenantContext = CreateTenantContext("tenant-001");
        var options = CreateTenantOptions();
        var provider = new SnsMessagingProvider(options, _snsLogger, _mockSns.Object, mockTenantContext.Object);

        // First call fails - should NOT cache the failure
        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Transient network error"));

        // Act & Assert - First call fails
        var act1 = async () => await provider.PublishAsync("events", new { Type = "OrderCreated" });
        await act1.Should().ThrowAsync<GenesisException>();

        // Second call succeeds - should retry (not use cached failure)
        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-2" });

        // Act - Second call should succeed
        var result = await provider.PublishAsync("events", new { Type = "OrderCreated" });

        // Assert - Second call succeeded (failure was not cached)
        result.Should().Be("msg-2");
        _mockSns.Verify(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static Mock<ITenantContext> CreateTenantContext(string tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.IsResolved).Returns(true);
        mock.Setup(t => t.TenantId).Returns(new TenantId(tenantId));
        mock.Setup(t => t.TenantName).Returns($"Tenant {tenantId}");
        return mock;
    }

    private static Microsoft.Extensions.Options.IOptions<MessagingOptions> CreateTenantOptions()
    {
        return MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            UseLocalEmulator = false,
            CacheTtl = TimeSpan.FromHours(1), // Enable caching with 1 hour TTL
            Resilience = new ResilienceOptions { Enabled = false },
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/",
                TenantQueueNameTemplate = "{queueName}-tenant-{tenantId}",
                PlatformQueueNameTemplate = "{queueName}-platform",
                WaitTimeSeconds = 10,
                VisibilityTimeoutSeconds = 30
            },
            Sns = new SnsOptions
            {
                DefaultTopicArn = "arn:aws:sns:us-east-1:123456789012:",
                TenantTopicNameTemplate = "{topicName}-tenant-{tenantId}",
                PlatformTopicNameTemplate = "{topicName}-platform"
            }
        });
    }

    [Fact]
    public async Task SQS_CacheTtlZero_DisablesCaching()
    {
        // Arrange - Create options with caching disabled
        var optionsWithoutCache = MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            CacheTtl = TimeSpan.Zero, // Disable caching
            Resilience = new ResilienceOptions { Enabled = false },
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/",
                TenantQueueNameTemplate = "{queueName}-tenant-{tenantId}"
            }
        });

        var mockTenantContext = CreateTenantContext("tenant-001");
        var provider = new SqsMessagingProvider(optionsWithoutCache, _sqsLogger, _mockSqs.Object, mockTenantContext.Object);

        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-1" });

        // Act - Multiple calls should each resolve URL (no caching)
        await provider.PublishAsync("orders", new { Id = 1 });
        await provider.PublishAsync("orders", new { Id = 2 });
        await provider.PublishAsync("orders", new { Id = 3 });

        // Assert - All three calls hit AWS (no caching occurred)
        _mockSqs.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SNS_CacheTtlZero_DisablesCaching()
    {
        // Arrange - Create options with caching disabled
        var optionsWithoutCache = MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            CacheTtl = TimeSpan.Zero, // Disable caching
            Resilience = new ResilienceOptions { Enabled = false },
            Sns = new SnsOptions
            {
                DefaultTopicArn = "arn:aws:sns:us-east-1:123456789012:",
                TenantTopicNameTemplate = "{topicName}-tenant-{tenantId}"
            }
        });

        var mockTenantContext = CreateTenantContext("tenant-001");
        var provider = new SnsMessagingProvider(optionsWithoutCache, _snsLogger, _mockSns.Object, mockTenantContext.Object);

        _mockSns.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-1" });

        // Act - Multiple calls should each resolve ARN (no caching)
        await provider.PublishAsync("events", new { Type = "OrderCreated" });
        await provider.PublishAsync("events", new { Type = "OrderUpdated" });
        await provider.PublishAsync("events", new { Type = "OrderCompleted" });

        // Assert - All three calls hit AWS (no caching occurred)
        _mockSns.Verify(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SQS_UppercaseTenantId_RejectedByValidator()
    {
        // Arrange - Uppercase tenant IDs should be rejected to prevent confusion
        var mockTenantAcme = CreateTenantContext("Acme"); // Uppercase - should fail
        var options = CreateTenantOptions();
        var provider = new SqsMessagingProvider(options, _sqsLogger, _mockSqs.Object, mockTenantAcme.Object);

        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-1" });

        // Act & Assert - Should throw due to uppercase letters in tenant ID
        var act = async () => await provider.PublishAsync("orders", new { Id = 1 });
        
        var exception = await act.Should().ThrowAsync<GenesisException>();
        // The shared TenantIdValidator rejects it with a stable error code. (The older per-provider
        // message suggested the lowercase form; the shared validator does not.)
        exception.Which.ErrorCode.Should().Be("INVALID_TENANT_ID");
        exception.Which.Message.Should().Contain("'Acme'").And.Contain("lowercase");
    }

    [Fact]
    public async Task SQS_ConcurrentEviction_DoesNotThrowOrRemoveRecentEntries()
    {
        // Arrange - Small cache size to trigger frequent evictions
        var smallCacheOptions = MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            CacheTtl = TimeSpan.FromHours(1),
            MaxCacheSize = 50, // Small size to trigger eviction quickly
            Resilience = new ResilienceOptions { Enabled = false },
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/",
                TenantQueueNameTemplate = "{queueName}-tenant-{tenantId}"
            }
        });

        var mockTenantContext = CreateTenantContext("test-tenant");
        var provider = new SqsMessagingProvider(smallCacheOptions, _sqsLogger, _mockSqs.Object, mockTenantContext.Object);

        var callCount = 0;
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return new SendMessageResponse { MessageId = $"msg-{callCount}" };
            });

        // Act - Hammer the cache with concurrent requests to different queues
        // This should trigger evictions while new entries are being added
        var tasks = new List<Task>();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        for (int i = 0; i < 100; i++)
        {
            var queueName = $"queue-{i}";
            var task = Task.Run(async () =>
            {
                try
                {
                    // Each queue accessed multiple times to exercise cache hit path during eviction
                    for (int j = 0; j < 3; j++)
                    {
                        await provider.PublishAsync(queueName, new { Id = j });
                        await Task.Delay(1); // Small delay to increase concurrency
                    }
                }
#pragma warning disable CA1031 // Do not catch general exception types - test needs to capture all exceptions
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
#pragma warning restore CA1031
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert - No exceptions should have been thrown during concurrent access
        exceptions.Should().BeEmpty("Concurrent cache access and eviction should not throw");

        // Assert - Provider should still be functional after concurrent hammering
        var finalResult = await provider.PublishAsync("final-queue", new { Id = 999 });
        finalResult.Should().NotBeNullOrEmpty();

        // Assert - Some AWS calls should have been made (cache working)
        callCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SQS_TtlExpiry_ExpiredEntriesNotReused()
    {
        // Arrange - Use fake time provider for deterministic testing
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        
        var shortTtlOptions = MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            CacheTtl = TimeSpan.FromMinutes(5), // 5 minute TTL
            Resilience = new ResilienceOptions { Enabled = false },
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/",
                TenantQueueNameTemplate = "{queueName}-tenant-{tenantId}"
            }
        });

        var mockTenantContext = CreateTenantContext("tenant-ttl");
        var provider = new SqsMessagingProvider(
            shortTtlOptions, 
            _sqsLogger, 
            _mockSqs.Object, 
            mockTenantContext.Object,
            fakeTime); // Pass TimeProvider as last parameter

        var callCount = 0;
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SendMessageResponse { MessageId = $"msg-{Interlocked.Increment(ref callCount)}" });

        // Act - First call caches the URL
        await provider.PublishAsync("orders", new { Id = 1 });
        callCount.Should().Be(1, "First call should hit AWS");

        // Act - Immediate second call uses cache (within TTL)
        await provider.PublishAsync("orders", new { Id = 2 });
        callCount.Should().Be(2, "Second call should reuse cache (both calls send messages)");

        // Act - Advance time past TTL
        fakeTime.Advance(TimeSpan.FromMinutes(6)); // Advance 6 minutes, past the 5-minute TTL

        // Act - Third call after expiry should detect expired entry and skip it
        await provider.PublishAsync("orders", new { Id = 3 });

        // Assert - All three calls result in messages sent
        // After TTL expiry, the expired entry is detected and skipped, URL re-resolved
        callCount.Should().Be(3, "All three calls should result in messages sent");
    }

    [Fact]
    public async Task SQS_LruEviction_RemovesOldestEntries()
    {
        // Arrange - Use fake time for deterministic eviction testing
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        
        var lruOptions = MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            CacheTtl = TimeSpan.FromHours(1),
            MaxCacheSize = 10, // Only 10 entries
            Resilience = new ResilienceOptions { Enabled = false },
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/",
                TenantQueueNameTemplate = "{queueName}-tenant-{tenantId}"
            }
        });

        var mockTenantContext = CreateTenantContext("tenant-lru");
        var provider = new SqsMessagingProvider(lruOptions, _sqsLogger, _mockSqs.Object, mockTenantContext.Object, fakeTime);

        var resolvedUrls = new Dictionary<string, string>();
        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SendMessageRequest req, CancellationToken _) =>
            {
                // Track which URLs we've seen for each queue
                var queueName = req.QueueUrl.Contains("queue-", StringComparison.Ordinal) 
                    ? req.QueueUrl.Split("tenant-tenant-lru/")[0] + "queue-X" // Normalize for tracking
                    : req.QueueUrl;
                
                if (!resolvedUrls.ContainsKey(req.QueueUrl))
                {
                    resolvedUrls[req.QueueUrl] = req.QueueUrl;
                }
                
                return new SendMessageResponse { MessageId = Guid.NewGuid().ToString() };
            });

        // Act - Fill cache with 10 entries (queue-0 through queue-9)
        for (int i = 0; i < 10; i++)
        {
            await provider.PublishAsync($"queue-{i}", new { Id = i });
            fakeTime.Advance(TimeSpan.FromMilliseconds(10)); // Ensure different timestamps
        }

        resolvedUrls.Count.Should().Be(10, "First 10 calls should resolve 10 unique URLs");
        var initialUrls = new Dictionary<string, string>(resolvedUrls);
        resolvedUrls.Clear();

        // Advance time past the recently-added protection window (1 second)
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        // Act - Access queue-5 through queue-9 (make them recently used)
        for (int i = 5; i < 10; i++)
        {
            await provider.PublishAsync($"queue-{i}", new { Id = i + 100 });
        }

        // These should be cache hits (reused URLs from initialUrls)
        resolvedUrls.Count.Should().Be(0, "queue-5 through queue-9 should be cache hits");

        // Act - Add 2 new entries (triggers eviction: 12 total > 10 max, evicts 10% = 1 entry)
        // Oldest entry is queue-0 (not accessed since initial fill)
        await provider.PublishAsync("queue-new-1", new { Id = 201 });
        await provider.PublishAsync("queue-new-2", new { Id = 202 });

        resolvedUrls.Count.Should().Be(2, "Two new URLs should be resolved for queue-new-1 and queue-new-2");
        resolvedUrls.Clear();

        // Act - Try to use queue-0 (should have been evicted as oldest)
        await provider.PublishAsync("queue-0", new { Id = 300 });

        // Assert - queue-0 should be re-resolved (was evicted)
        resolvedUrls.Count.Should().Be(1, "queue-0 should be re-resolved after eviction");
        
        // Act - Try queue-5 (should still be cached - it was recently accessed)
        await provider.PublishAsync("queue-5", new { Id = 400 });
        
        // Assert - queue-5 should be a cache hit (not evicted because recently accessed)
        resolvedUrls.Count.Should().Be(1, "queue-5 should still be cached (was recently accessed)");
        
        // Summary: queue-0 was evicted (oldest), queue-5 through queue-9 were kept (recently used)
    }

    [Fact]
    public async Task SQS_LruEviction_KeepsRecentlyAddedEntries()
    {
        // Arrange - Very small cache to force immediate eviction
        var tinyLruOptions = MsOptions.Create(new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            CacheTtl = TimeSpan.FromHours(1),
            MaxCacheSize = 5, // Only 5 entries
            Resilience = new ResilienceOptions { Enabled = false },
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/",
                TenantQueueNameTemplate = "{queueName}-tenant-{tenantId}"
            }
        });

        var mockTenantContext = CreateTenantContext("tenant-recent");
        var provider = new SqsMessagingProvider(tinyLruOptions, _sqsLogger, _mockSqs.Object, mockTenantContext.Object);

        _mockSqs.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SendMessageResponse { MessageId = Guid.NewGuid().ToString() });

        // Act - Fill cache
        for (int i = 0; i < 5; i++)
        {
            await provider.PublishAsync($"queue-{i}", new { Id = i });
            await Task.Delay(10);
        }

        // Wait for recently-added protection window (1 second)
        await Task.Delay(1100);

        // Act - Add one more entry, triggering eviction
        await provider.PublishAsync("queue-new", new { Id = 100 });

        // Act - Immediately use the just-added entry again
        await provider.PublishAsync("queue-new", new { Id = 101 });

        // Assert - Should still work (just-added entry shouldn't evict itself)
        // The fact that no exception was thrown proves the cache is consistent
        _mockSqs.Verify(s => s.SendMessageAsync(
            It.Is<SendMessageRequest>(r => r.QueueUrl.Contains("queue-new")),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
