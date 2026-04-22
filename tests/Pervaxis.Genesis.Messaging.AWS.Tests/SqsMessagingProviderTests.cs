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

using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Pervaxis.Genesis.Messaging.AWS.Tests;

public class SqsMessagingProviderTests
{
    private readonly Mock<IAmazonSQS> _mockSqs;
    private readonly Mock<ILogger<SqsMessagingProvider>> _mockLogger;
    private readonly MessagingOptions _options;

    public SqsMessagingProviderTests()
    {
        _mockSqs = new Mock<IAmazonSQS>();
        _mockLogger = new Mock<ILogger<SqsMessagingProvider>>();

        _options = new MessagingOptions
        {
            Region = "ap-south-1",
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = "https://sqs.ap-south-1.amazonaws.com/123/my-queue"
            }
        };
    }

    private SqsMessagingProvider CreateProvider(MessagingOptions? options = null) =>
        new(MsOptions.Create(options ?? _options), _mockLogger.Object, _mockSqs.Object);

    private sealed record TestMessage(int Id, string Text);

    // ── Constructor ──────────────────────────────────────────────────────────

    public class Constructor : SqsMessagingProviderTests
    {
        [Fact]
        public void NullOptions_ThrowsArgumentNullException()
        {
            var act = () => new SqsMessagingProvider(null!, _mockLogger.Object, _mockSqs.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public void NullLogger_ThrowsArgumentNullException()
        {
            var act = () => new SqsMessagingProvider(MsOptions.Create(_options), null!, _mockSqs.Object);
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void NullSqsClient_ThrowsArgumentNullException()
        {
            var act = () => new SqsMessagingProvider(MsOptions.Create(_options), _mockLogger.Object, null!);
            act.Should().Throw<ArgumentNullException>().WithParameterName("sqsClient");
        }

        [Fact]
        public void EmptyRegion_ThrowsGenesisConfigurationException()
        {
            var invalid = new MessagingOptions { Region = string.Empty };
            var act = () => new SqsMessagingProvider(MsOptions.Create(invalid), _mockLogger.Object, _mockSqs.Object);
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

    public class PublishAsync : SqsMessagingProviderTests
    {
        [Fact]
        public async Task ValidMessage_ReturnsMessageId()
        {
            _mockSqs
                .Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendMessageResponse { MessageId = "msg-001" });

            var result = await CreateProvider().PublishAsync("orders", new TestMessage(1, "Hello"));

            result.Should().Be("msg-001");
        }

        [Fact]
        public async Task UsesQueueUrlMapping_WhenDestinationMapped()
        {
            var options = new MessagingOptions
            {
                Region = "ap-south-1",
                Sqs = new SqsOptions
                {
                    DefaultQueueUrl = "https://sqs.ap-south-1.amazonaws.com/123/default"
                }
            };
            options.Sqs.QueueUrlMappings["orders"] = "https://sqs.ap-south-1.amazonaws.com/123/orders-queue";

            _mockSqs
                .Setup(s => s.SendMessageAsync(
                    It.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.ap-south-1.amazonaws.com/123/orders-queue"),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendMessageResponse { MessageId = "msg-mapped" });

            var result = await CreateProvider(options).PublishAsync("orders", new TestMessage(1, "Hello"));

            result.Should().Be("msg-mapped");
        }

        [Fact]
        public async Task UsesDefaultQueueUrl_WhenNoMapping()
        {
            _mockSqs
                .Setup(s => s.SendMessageAsync(
                    It.Is<SendMessageRequest>(r => r.QueueUrl == _options.Sqs.DefaultQueueUrl),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendMessageResponse { MessageId = "msg-default" });

            var result = await CreateProvider().PublishAsync("unknown-destination", new TestMessage(1, "Hi"));

            result.Should().Be("msg-default");
        }

        [Fact]
        public async Task EmptyDestination_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().PublishAsync<TestMessage>("", new TestMessage(1, "Hi"));
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task NullMessage_ThrowsArgumentNullException()
        {
            var act = async () => await CreateProvider().PublishAsync<TestMessage>("orders", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SqsException_ThrowsGenesisException()
        {
            _mockSqs
                .Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSQSException("Queue not found"));

            var act = async () => await CreateProvider().PublishAsync("orders", new TestMessage(1, "Hi"));
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*SQS publish operation failed*");
        }
    }

    // ── PublishBatchAsync ────────────────────────────────────────────────────

    public class PublishBatchAsync : SqsMessagingProviderTests
    {
        [Fact]
        public async Task EmptyMessages_ReturnsEmptyWithoutCallingAws()
        {
            var result = await CreateProvider().PublishBatchAsync<TestMessage>("orders", []);

            result.Should().BeEmpty();
            _mockSqs.Verify(s => s.SendMessageBatchAsync(It.IsAny<SendMessageBatchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ValidMessages_ReturnsAllMessageIds()
        {
            _mockSqs
                .Setup(s => s.SendMessageBatchAsync(It.IsAny<SendMessageBatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendMessageBatchResponse
                {
                    Successful = [
                        new SendMessageBatchResultEntry { MessageId = "id-1" },
                        new SendMessageBatchResultEntry { MessageId = "id-2" }
                    ],
                    Failed = []
                });

            var messages = new[] { new TestMessage(1, "A"), new TestMessage(2, "B") };
            var result = await CreateProvider().PublishBatchAsync("orders", messages);

            result.Should().BeEquivalentTo(["id-1", "id-2"]);
        }

        [Fact]
        public async Task NullMessages_ThrowsArgumentNullException()
        {
            var act = async () => await CreateProvider().PublishBatchAsync<TestMessage>("orders", null!);
            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public async Task SqsException_ThrowsGenesisException()
        {
            _mockSqs
                .Setup(s => s.SendMessageBatchAsync(It.IsAny<SendMessageBatchRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSQSException("Batch failed"));

            var act = async () => await CreateProvider().PublishBatchAsync("orders", [new TestMessage(1, "Hi")]);
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*SQS batch publish operation failed*");
        }
    }

    // ── ReceiveAsync ─────────────────────────────────────────────────────────

    public class ReceiveAsync : SqsMessagingProviderTests
    {
        [Fact]
        public async Task MessagesAvailable_ReturnsDeserializedMessages()
        {
            var body = """{"Id":1,"Text":"Hello"}""";
            _mockSqs
                .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceiveMessageResponse
                {
                    Messages = [new Message { Body = body, ReceiptHandle = "rh-1" }]
                });

            var result = (await CreateProvider().ReceiveAsync<TestMessage>("orders")).ToList();

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(1);
            result[0].Text.Should().Be("Hello");
        }

        [Fact]
        public async Task NoMessages_ReturnsEmptyCollection()
        {
            _mockSqs
                .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceiveMessageResponse { Messages = [] });

            var result = await CreateProvider().ReceiveAsync<TestMessage>("orders");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task EmptyQueue_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().ReceiveAsync<TestMessage>("");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task SqsException_ThrowsGenesisException()
        {
            _mockSqs
                .Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSQSException("Queue unavailable"));

            var act = async () => await CreateProvider().ReceiveAsync<TestMessage>("orders");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*SQS receive operation failed*");
        }
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    public class DeleteAsync : SqsMessagingProviderTests
    {
        [Fact]
        public async Task ValidReceiptHandle_ReturnsTrue()
        {
            _mockSqs
                .Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMessageResponse());

            var result = await CreateProvider().DeleteAsync("orders", "receipt-handle-abc");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyQueue_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().DeleteAsync("", "rh");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task EmptyReceiptHandle_ThrowsArgumentException()
        {
            var act = async () => await CreateProvider().DeleteAsync("orders", "");
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task SqsException_ThrowsGenesisException()
        {
            _mockSqs
                .Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonSQSException("Delete failed"));

            var act = async () => await CreateProvider().DeleteAsync("orders", "rh");
            await act.Should().ThrowAsync<GenesisException>()
                .WithMessage("*SQS delete operation failed*");
        }
    }

    // ── SubscribeAsync ───────────────────────────────────────────────────────

    public class SubscribeAsync : SqsMessagingProviderTests
    {
        [Fact]
        public async Task Always_ThrowsNotSupportedException()
        {
            var act = async () => await CreateProvider().SubscribeAsync("topic", "endpoint");
            await act.Should().ThrowAsync<NotSupportedException>();
        }
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    public class Dispose : SqsMessagingProviderTests
    {
        [Fact]
        public void WhenClientNotUsed_DoesNotDisposeSqsClient()
        {
            var provider = CreateProvider();
            provider.Dispose();

            _mockSqs.Verify(s => s.Dispose(), Times.Never);
        }

        [Fact]
        public async Task WhenClientWasUsed_DisposesSqsClient()
        {
            _mockSqs
                .Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendMessageResponse { MessageId = "id" });

            var provider = CreateProvider();
            await provider.PublishAsync("orders", new TestMessage(1, "Hi"));
            provider.Dispose();

            _mockSqs.Verify(s => s.Dispose(), Times.Once);
        }
    }
}
