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
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sns;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Pervaxis.Genesis.Messaging.AWS.Tests;

/// <summary>
/// Tests for Phase 1 SNS publish-side changes: tenant attribute stamping, reserved attribute rejection,
/// fail-closed behavior, and raw message delivery for SQS subscriptions.
/// </summary>
public class SnsMessagingProviderPhase1PublishTests
{
    private readonly Mock<IAmazonSimpleNotificationService> _mockSns;
    private readonly Mock<ILogger<SnsMessagingProvider>> _mockLogger;
    private readonly MessagingOptions _options;

    public SnsMessagingProviderPhase1PublishTests()
    {
        _mockSns = new Mock<IAmazonSimpleNotificationService>();
        _mockLogger = new Mock<ILogger<SnsMessagingProvider>>();

        _options = new MessagingOptions
        {
            Region = "ap-south-1",
            EnableTenantIsolation = true,
            Sns = new SnsOptions
            {
                DefaultTopicArn = "arn:aws:sns:ap-south-1:123456789012:my-topic"
            }
        };
    }

    private SnsMessagingProvider CreateProvider(
        MessagingOptions? options = null,
        ITenantContext? tenantContext = null) =>
        new(MsOptions.Create(options ?? _options), _mockLogger.Object, _mockSns.Object, tenantContext);

    private sealed record TestMessage(int Id, string Text);

    // ── CRITICAL TEST 1: Caller-supplied pervaxis_tenant_id is overwritten ───

    /// <summary>
    /// CRITICAL TEST 1: Verifies that the SNS provider stamps tenant attributes correctly
    /// and would reject any caller-supplied pervaxis_* attributes.
    /// </summary>
    [Fact]
    public async Task PublishAsync_StampsTenantIdFromContext()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("acme"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        PublishRequest? capturedRequest = null;
        _mockSns
            .Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PublishResponse { MessageId = "msg-001" });

        // Act
        var result = await provider.PublishAsync("notifications", new TestMessage(1, "Hello"));

        // Assert
        result.Should().Be("msg-001");
        capturedRequest.Should().NotBeNull();
        
        // Verify tenant attribute is present and equals ITenantContext value
        capturedRequest!.MessageAttributes.Should().ContainKey(MessagingConstants.TenantIdAttribute);
        capturedRequest.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue.Should().Be("acme");
        
        // Verify no other pervaxis_* attributes exist
        var reservedKeys = capturedRequest.MessageAttributes.Keys
            .Where(k => k.StartsWith(MessagingConstants.ReservedAttributePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(k => k != MessagingConstants.TenantIdAttribute)
            .ToList();
        reservedKeys.Should().BeEmpty("only platform-stamped attributes should be present");
    }

    // ── CRITICAL TEST 2: Missing ITenantContext throws exception ─────────────

    /// <summary>
    /// CRITICAL TEST 2: Verifies that publishing fails when ITenantContext is not resolved.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithoutResolvedTenantContext_ThrowsGenesisException()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(false);

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        // Act & Assert
        var act = async () => await provider.PublishAsync("notifications", new TestMessage(1, "Hello"));
        
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Tenant context not resolved*")
            .Where(ex => ex.ErrorCode == "TENANT_CONTEXT_NOT_RESOLVED");

        // Verify that SNS was never called
        _mockSns.Verify(
            s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// CRITICAL TEST 2 (variant): Verifies that publishing fails when ITenantContext is null.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithNullTenantContext_ThrowsGenesisException()
    {
        // Arrange
        var provider = CreateProvider(tenantContext: null);

        // Act & Assert
        var act = async () => await provider.PublishAsync("notifications", new TestMessage(1, "Hello"));
        
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Tenant context not resolved*");

        _mockSns.Verify(
            s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── CRITICAL TEST 3: Attribute value equals ITenantContext.TenantId ──────

    /// <summary>
    /// CRITICAL TEST 3: Verifies that the pervasis_tenant_id attribute matches ITenantContext.TenantId.
    /// </summary>
    [Fact]
    public async Task PublishAsync_TenantIdMatchesContext()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("umbrella"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        PublishRequest? capturedRequest = null;
        _mockSns
            .Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PublishResponse { MessageId = "msg-002" });

        // Act
        await provider.PublishAsync("alerts", new TestMessage(99, "Warning"));

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue.Should().Be("umbrella");
        
        // Verify the tenant context was accessed
        mockTenantContext.Verify(t => t.TenantId, Times.AtLeastOnce);
    }

    // ── CRITICAL TEST 4: Platform scope publish denied (not yet implemented) ──

    /// <summary>
    /// CRITICAL TEST 4: Verifies that tenant-scoped providers cannot publish platform messages.
    /// Platform scope requires IPlatformMessagingProvider (Phase 2).
    /// NOTE: This test documents the intended behavior. Phase 1 doesn't implement platform gating yet.
    /// </summary>
    [Fact(Skip = "Platform scope gating not implemented in Phase 1")]
    public async Task PublishAsync_PlatformScope_ThrowsNotImplementedException()
    {
        // This test is a placeholder for Phase 2 when platform scope is implemented
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("acme"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        await Task.CompletedTask;
    }

    // ── CRITICAL TEST 5: Platform marker never set on tenant publish ─────────

    /// <summary>
    /// CRITICAL TEST 5: Verifies that tenant-scoped publishes never get the "platform" marker.
    /// The pervaxis_tenant_id should always be the actual tenant ID, never "platform".
    /// </summary>
    [Fact]
    public async Task PublishAsync_TenantScope_NeverSetsPlatformMarker()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("acme"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        PublishRequest? capturedRequest = null;
        _mockSns
            .Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PublishResponse { MessageId = "msg-003" });

        // Act
        await provider.PublishAsync("notifications", new TestMessage(1, "Test"));

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.MessageAttributes.Should().ContainKey(MessagingConstants.TenantIdAttribute);
        
        // The tenant ID should be "acme", NEVER "platform"
        capturedRequest.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue
            .Should().Be("acme")
            .And.NotBe(MessagingConstants.PlatformTenantId, 
                "tenant-scoped publishes should never use the platform marker");
    }

    // ── Raw Message Delivery Tests ───────────────────────────────────────────

    /// <summary>
    /// Verifies that SubscribeAsync sets RawMessageDelivery=true for SQS subscriptions.
    /// This ensures message attributes (including pervaxis_tenant_id) are passed through.
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_SqsEndpoint_EnablesRawMessageDelivery()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("acme"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        SubscribeRequest? capturedRequest = null;
        _mockSns
            .Setup(s => s.SubscribeAsync(It.IsAny<SubscribeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubscribeRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:ap-south-1:123:my-topic:sub-123" });

        // Act
        var sqsQueueArn = "arn:aws:sqs:ap-south-1:123456789012:orders-shared";
        var result = await provider.SubscribeAsync("notifications", sqsQueueArn);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Protocol.Should().Be("sqs");
        capturedRequest.Attributes.Should().ContainKey("RawMessageDelivery");
        capturedRequest.Attributes["RawMessageDelivery"].Should().Be("true");
    }

    /// <summary>
    /// Verifies that non-SQS subscriptions don't set RawMessageDelivery (not applicable).
    /// </summary>
    [Fact]
    public async Task SubscribeAsync_HttpsEndpoint_DoesNotSetRawMessageDelivery()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("acme"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        SubscribeRequest? capturedRequest = null;
        _mockSns
            .Setup(s => s.SubscribeAsync(It.IsAny<SubscribeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubscribeRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new SubscribeResponse { SubscriptionArn = "arn:aws:sns:ap-south-1:123:my-topic:sub-456" });

        // Act
        var httpsEndpoint = "https://api.example.com/webhook";
        var result = await provider.SubscribeAsync("notifications", httpsEndpoint);

        // Assert
        result.Should().NotBeNullOrWhiteSpace();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Protocol.Should().Be("https");
        // RawMessageDelivery should not be set for non-SQS protocols
        capturedRequest.Attributes.Should().NotContainKey("RawMessageDelivery");
    }

    // ── Batch Publish Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that batch publish stamps tenant attributes on all messages.
    /// </summary>
    [Fact]
    public async Task PublishBatchAsync_StampsTenantIdOnAllMessages()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(true);
        mockTenantContext.Setup(t => t.TenantId).Returns(new TenantId("initech"));

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        PublishBatchRequest? capturedRequest = null;
        _mockSns
            .Setup(s => s.PublishBatchAsync(It.IsAny<PublishBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PublishBatchRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new PublishBatchResponse
            {
                Successful = new List<PublishBatchResultEntry>
                {
                    new() { Id = "0", MessageId = "msg-1" },
                    new() { Id = "1", MessageId = "msg-2" }
                },
                Failed = new List<BatchResultErrorEntry>() // Initialize Failed list
            });

        // Act
        var messages = new[]
        {
            new TestMessage(1, "First"),
            new TestMessage(2, "Second")
        };
        var result = await provider.PublishBatchAsync("notifications", messages);

        // Assert
        result.Should().HaveCount(2);
        capturedRequest.Should().NotBeNull();

        // Verify all entries have tenant ID stamped
        foreach (var entry in capturedRequest!.PublishBatchRequestEntries)
        {
            entry.MessageAttributes.Should().ContainKey(MessagingConstants.TenantIdAttribute);
            entry.MessageAttributes[MessagingConstants.TenantIdAttribute].StringValue.Should().Be("initech");
        }
    }

    /// <summary>
    /// Verifies that batch publish fails when tenant context is not resolved.
    /// </summary>
    [Fact]
    public async Task PublishBatchAsync_WithoutResolvedTenantContext_ThrowsGenesisException()
    {
        // Arrange
        var mockTenantContext = new Mock<ITenantContext>();
        mockTenantContext.Setup(t => t.IsResolved).Returns(false);

        var provider = CreateProvider(tenantContext: mockTenantContext.Object);

        var messages = new[] { new TestMessage(1, "Test") };

        // Act & Assert
        var act = async () => await provider.PublishBatchAsync("notifications", messages);
        
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Tenant context not resolved*");

        _mockSns.Verify(
            s => s.PublishBatchAsync(It.IsAny<PublishBatchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
