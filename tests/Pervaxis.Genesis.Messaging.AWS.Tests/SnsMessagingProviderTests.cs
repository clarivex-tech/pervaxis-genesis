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

public class SnsMessagingProviderTests
{
    private readonly Mock<IAmazonSimpleNotificationService> _mockSns;
    private readonly Mock<ILogger<SnsMessagingProvider>> _mockLogger;
    private readonly MessagingOptions _options;

    public SnsMessagingProviderTests()
    {
        _mockSns = new Mock<IAmazonSimpleNotificationService>();
        _mockLogger = new Mock<ILogger<SnsMessagingProvider>>();

        _options = new MessagingOptions
        {
            Region = "ap-south-1",
            Sns = new SnsOptions
            {
                DefaultTopicArn = "arn:aws:sns:ap-south-1:123456789012:my-topic"
            }
        };
    }

    private SnsMessagingProvider CreateProvider(MessagingOptions? options = null) =>
        new(MsOptions.Create(options ?? _options), _mockLogger.Object, _mockSns.Object, ResolvedTenant());
    // Phase 1A: publish fails closed without a resolved tenant, so the fixture always supplies one.
    private static ITenantContext ResolvedTenant(string tenantId = "acme")
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.IsResolved).Returns(true);
        tenant.Setup(t => t.TenantId).Returns(new TenantId(tenantId));
        tenant.Setup(t => t.TenantName).Returns($"{tenantId}-corp");
        return tenant.Object;
    }

    private sealed record TestEvent(string EventType, string Payload);

    // ── Constructor ──────────────────────────────────────────────────────────

    public class Constructor : SnsMessagingProviderTests
    {
        [Fact]
        public void NullOptions_ThrowsArgumentNullException()
        {
            var act = () => new SnsMessagingProvider(null!, _mockLogger.Object, _mockSns.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public void NullLogger_ThrowsArgumentNullException()
        {
            var act = () => new SnsMessagingProvider(MsOptions.Create(_options), null!, _mockSns.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void NullSnsClient_ThrowsArgumentNullException()
        {
            var act = () => new SnsMessagingProvider(MsOptions.Create(_options), _mockLogger.Object, (IAmazonSimpleNotificationService)null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("snsClient");
        }

        [Fact]
        public void EmptyRegion_ThrowsGenesisConfigurationException()
        {
            var invalid = new MessagingOptions { Region = string.Empty };
            var act = () => new SnsMessagingProvider(MsOptions.Create(invalid), _mockLogger.Object, _mockSns.Object);
            act.Should().Throw<GenesisConfigurationException>();
        }

        [Fact]
        public void ValidOptions_CreatesProviderSuccessfully()
        {
            var act = () => CreateProvider();
            act.Should().NotThrow();
        }
    }

    // ── PublishAsync ─────────────────────────────────────────────────────────

    public class PublishAsync : SnsMessagingProviderTests
    {
        [Fact]
        public async Task ValidMessage_ReturnsMessageId()
        {
            _mockSns
                .Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublishResponse { MessageId = "sns-001" });

            var result = await CreateProvider().PublishAsync("events", new TestEvent("OrderPlaced", "{}"));

            result.Should().Be("sns-001");
        }

        [Fact]
        public async Task UsesTopicArnMapping_WhenDestinationMapped()
        {
            var options = new MessagingOptions
            {
                Region = "ap-south-1",
                Sns = new SnsOptions
                {
                    DefaultTopicArn = "arn:aws:sns:ap-south-1:123:default"
                }
            };
            options.Sns.TopicArnMappings["orders"] = "arn:aws:sns:ap-south-1:123:orders-topic";

            _mockSns
                .Setup(s => s.PublishAsync(
                    It.Is<PublishRequest>(r => r.TopicArn == "arn:aws:sns:ap-south-1:123:orders-topic"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublishResponse { MessageId = "sns-mapped" });

            var result = await CreateProvider(options).PublishAsync("orders", new TestEvent("OrderPlaced", "{}"));

            result.Should().Be("sns-mapped");
        }

        [Fact]
        public async Task EmptyDestination_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().PublishAsync<TestEvent>("", new TestEvent("e", "{}"));
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullMessage_ThrowsArgumentNullException()
        {
            var act = async () => await CreateProvider().PublishAsync<TestEvent>("events", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SnsException_ThrowsGenesisException()
        {
            _mockSns
                .Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSimpleNotificationServiceException("Topic not found"));

            var act = async () => await CreateProvider().PublishAsync("events", new TestEvent("e", "{}"));
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*SNS publish operation failed*");
        }
    }

    // ── PublishBatchAsync ────────────────────────────────────────────────────

    public class PublishBatchAsync : SnsMessagingProviderTests
    {
        [Fact]
        public async Task EmptyMessages_ReturnsEmptyWithoutCallingAws()
        {
            var result = await CreateProvider().PublishBatchAsync<TestEvent>("events", []);

            result.Should().BeEmpty();
            _mockSns.Verify(s => s.PublishBatchAsync(It.IsAny<PublishBatchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ValidMessages_ReturnsAllMessageIds()
        {
            _mockSns
                .Setup(s => s.PublishBatchAsync(It.IsAny<PublishBatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublishBatchResponse
                {
                    Successful = [
                        new PublishBatchResultEntry { MessageId = "sns-1" },
                        new PublishBatchResultEntry { MessageId = "sns-2" }
                    ],
                    Failed = []
                });

            var messages = new[] { new TestEvent("A", "{}"), new TestEvent("B", "{}") };
            var result = await CreateProvider().PublishBatchAsync("events", messages);

            result.Should().BeEquivalentTo(["sns-1", "sns-2"]);
        }

        [Fact]
        public async Task NullMessages_ThrowsArgumentNullException()
        {
            var act = async () => await CreateProvider().PublishBatchAsync<TestEvent>("events", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SnsException_ThrowsGenesisException()
        {
            _mockSns
                .Setup(s => s.PublishBatchAsync(It.IsAny<PublishBatchRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSimpleNotificationServiceException("Batch failed"));

            var act = async () => await CreateProvider().PublishBatchAsync("events", [new TestEvent("e", "{}")]);
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*SNS batch publish operation failed*");
        }
    }

    // ── ReceiveAsync ─────────────────────────────────────────────────────────

    public class ReceiveAsync : SnsMessagingProviderTests
    {
        [Fact]
        public async Task Always_ThrowsNotSupportedException()
        {
            var act = async () => await CreateProvider().ReceiveAsync<TestEvent>("events");
            await act.Should().ThrowAsync<NotSupportedException>();
        }
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    public class DeleteAsync : SnsMessagingProviderTests
    {
        [Fact]
        public async Task Always_ThrowsNotSupportedException()
        {
            var act = async () => await CreateProvider().DeleteAsync("events", "handle");
            await act.Should().ThrowAsync<NotSupportedException>();
        }
    }

    // ── SubscribeAsync ───────────────────────────────────────────────────────

    public class SubscribeAsync : SnsMessagingProviderTests
    {
        [Theory]
        [InlineData("arn:aws:sqs:ap-south-1:123:shared-queue")]
        [InlineData("https://example.com/webhook")]
        [InlineData("user@example.com")]
        public async Task TenantCannotManageSharedSubscriptions(string endpoint)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => CreateProvider().SubscribeAsync("events", endpoint));
            _mockSns.Verify(c => c.SubscribeAsync(It.IsAny<SubscribeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EmptyTopic_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => CreateProvider().SubscribeAsync("", "endpoint"));
        }

        [Fact]
        public async Task EmptyEndpoint_ThrowsArgumentException()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => CreateProvider().SubscribeAsync("events", ""));
        }
    }
    public class Dispose : SnsMessagingProviderTests
    {
        [Fact]
        public void WhenClientNotUsed_DoesNotDisposeSnsClient()
        {
            var provider = CreateProvider();
            provider.Dispose();

            _mockSns.Verify(s => s.Dispose(), Times.Never);
        }

        [Fact]
        public async Task WhenClientWasUsed_DoesNotDisposeSharedSnsClient()
        {
            _mockSns
                .Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublishResponse { MessageId = "id" });

            var provider = CreateProvider();
            await provider.PublishAsync("events", new TestEvent("e", "{}"));
            provider.Dispose();

            _mockSns.Verify(s => s.Dispose(), Times.Never);
        }
    }
}
