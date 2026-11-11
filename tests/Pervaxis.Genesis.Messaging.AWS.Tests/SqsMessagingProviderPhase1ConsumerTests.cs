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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Messaging.AWS.Options;
using Pervaxis.Genesis.Messaging.AWS.Providers.Sqs;
using Xunit;

namespace Pervaxis.Genesis.Messaging.AWS.Tests;

/// <summary>
/// Phase 1B Consumer-Side Tests: Tenant Isolation Message Validation
/// </summary>
public sealed class SqsMessagingProviderPhase1ConsumerTests : IDisposable
{
    private readonly Mock<IAmazonSQS> _mockSqsClient;
    private readonly Mock<ITenantContext> _mockTenantContext;
    private readonly MessagingOptions _options;
    private readonly SqsMessagingProvider _provider;
    private const string TestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
    private const string TestDlqUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-dlq";
    private const string ValidTenantId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301"; // Valid GUID format

    public SqsMessagingProviderPhase1ConsumerTests()
    {
        _mockSqsClient = new Mock<IAmazonSQS>();
        _mockTenantContext = new Mock<ITenantContext>();

        _options = new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = TestQueueUrl,
                DlqUrl = TestDlqUrl
            }
        };

        // Setup basic queue URL resolution
        _mockSqsClient.Setup(x => x.GetQueueUrlAsync(
            It.IsAny<GetQueueUrlRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = TestQueueUrl });

        _provider = new SqsMessagingProvider(
            _mockSqsClient.Object,
            _mockTenantContext.Object,
            Options.Create(_options),
            NullLogger<SqsMessagingProvider>.Instance,
            TimeProvider.System);
    }

    public void Dispose()
    {
        _provider?.Dispose();
    }

    [Fact(DisplayName = "PHASE1B-CRITICAL-1: Missing tenant ID attribute sends message to DLQ")]
    public async Task ReceiveAsync_MissingTenantIdAttribute_SendsToDlq()
    {
        // Arrange
        var messageWithoutTenantId = new Message
        {
            MessageId = "msg-123",
            Body = "{\"data\":\"test\"}",
            ReceiptHandle = "receipt-123",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>() // No pervaxis_tenant_id
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = new List<Message> { messageWithoutTenantId }
            });

        SendMessageRequest? dlqRequest = null;
        _mockSqsClient.Setup(x => x.SendMessageAsync(
            It.Is<SendMessageRequest>(r => r.QueueUrl == TestDlqUrl),
            It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((req, _) => dlqRequest = req)
            .ReturnsAsync(new SendMessageResponse { MessageId = "dlq-msg-123" });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        // Act
        var result = await _provider.ReceiveAsync<Dictionary<string, string>>("test-queue", 10);

        // Assert
        result.Should().BeEmpty(); // Invalid message not returned
        dlqRequest.Should().NotBeNull();
        dlqRequest!.MessageBody.Should().Be(messageWithoutTenantId.Body);
        dlqRequest.MessageAttributes.Should().ContainKey("pervaxis_failure_reason");
        dlqRequest.MessageAttributes["pervaxis_failure_reason"].StringValue.Should().Be("missing_tenant_id");

        _mockSqsClient.Verify(x => x.DeleteMessageAsync(
            It.Is<DeleteMessageRequest>(r => r.ReceiptHandle == "receipt-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PHASE1B-CRITICAL-2: Invalid tenant ID format sends message to DLQ")]
    public async Task ReceiveAsync_InvalidTenantIdFormat_SendsToDlq()
    {
        // Arrange
        var messageWithInvalidTenantId = new Message
        {
            MessageId = "msg-456",
            Body = "{\"data\":\"test\"}",
            ReceiptHandle = "receipt-456",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["pervaxis_tenant_id"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "INVALID-FORMAT-123" // Not a valid GUID
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = new List<Message> { messageWithInvalidTenantId }
            });

        SendMessageRequest? dlqRequest = null;
        _mockSqsClient.Setup(x => x.SendMessageAsync(
            It.Is<SendMessageRequest>(r => r.QueueUrl == TestDlqUrl),
            It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((req, _) => dlqRequest = req)
            .ReturnsAsync(new SendMessageResponse { MessageId = "dlq-msg-456" });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        // Act
        var result = await _provider.ReceiveAsync<Dictionary<string, string>>("test-queue", 10);

        // Assert
        result.Should().BeEmpty();
        dlqRequest.Should().NotBeNull();
        dlqRequest!.MessageAttributes["pervaxis_failure_reason"].StringValue.Should().Be("invalid_tenant_id");

        _mockSqsClient.Verify(x => x.DeleteMessageAsync(
            It.Is<DeleteMessageRequest>(r => r.ReceiptHandle == "receipt-456"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PHASE1B-CRITICAL-3: Platform tenant ID rejected in tenant consumer")]
    public async Task ReceiveAsync_PlatformTenantId_SendsToDlq()
    {
        // Arrange
        var messageWithPlatformId = new Message
        {
            MessageId = "msg-789",
            Body = "{\"data\":\"test\"}",
            ReceiptHandle = "receipt-789",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["pervaxis_tenant_id"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "platform" // Reserved platform ID
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = new List<Message> { messageWithPlatformId }
            });

        SendMessageRequest? dlqRequest = null;
        _mockSqsClient.Setup(x => x.SendMessageAsync(
            It.Is<SendMessageRequest>(r => r.QueueUrl == TestDlqUrl),
            It.IsAny<CancellationToken>()))
            .Callback<SendMessageRequest, CancellationToken>((req, _) => dlqRequest = req)
            .ReturnsAsync(new SendMessageResponse { MessageId = "dlq-msg-789" });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        // Act
        var result = await _provider.ReceiveAsync<Dictionary<string, string>>("test-queue", 10);

        // Assert
        result.Should().BeEmpty();
        dlqRequest.Should().NotBeNull();
        dlqRequest!.MessageAttributes["pervaxis_failure_reason"].StringValue
            .Should().Be("platform_message_not_allowed_in_tenant_consumer");
    }

    [Fact(DisplayName = "PHASE1B-CRITICAL-4: DLQ failure prevents message deletion")]
    public async Task ReceiveAsync_DlqSendFails_DoesNotDeleteMessage()
    {
        // Arrange
        var messageWithoutTenantId = new Message
        {
            MessageId = "msg-999",
            Body = "{\"data\":\"test\"}",
            ReceiptHandle = "receipt-999",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = new List<Message> { messageWithoutTenantId }
            });

        // DLQ send fails
        _mockSqsClient.Setup(x => x.SendMessageAsync(
            It.Is<SendMessageRequest>(r => r.QueueUrl == TestDlqUrl),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("DLQ unavailable"));

        // Act & Assert
        await Assert.ThrowsAsync<GenesisException>(
            async () => await _provider.ReceiveAsync<Dictionary<string, string>>("test-queue", 10));

        // Verify message was NOT deleted from source queue
        _mockSqsClient.Verify(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "PHASE1B-CRITICAL-5: Valid tenant ID allows message through")]
    public async Task ReceiveAsync_ValidTenantId_ReturnsMessage()
    {
        // Arrange
        var validMessage = new Message
        {
            MessageId = "msg-valid",
            Body = "{\"data\":\"success\"}",
            ReceiptHandle = "receipt-valid",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["pervaxis_tenant_id"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = ValidTenantId
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = new List<Message> { validMessage }
            });

        // Act
        var result = await _provider.ReceiveAsync<Dictionary<string, string>>("test-queue", 10);

        // Assert
        result.Should().HaveCount(1);
        result.First()["data"].Should().Be("success");

        // Should NOT send to DLQ
        _mockSqsClient.Verify(x => x.SendMessageAsync(
            It.Is<SendMessageRequest>(r => r.QueueUrl == TestDlqUrl),
            It.IsAny<CancellationToken>()), Times.Never);

        // Should NOT delete (caller's responsibility after processing)
        _mockSqsClient.Verify(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "PHASE1B-CRITICAL-6: No DLQ configured throws exception")]
    public async Task ReceiveAsync_NoDlqConfigured_ThrowsGenesisException()
    {
        // Arrange
        var optionsWithoutDlq = new MessagingOptions
        {
            Region = "us-east-1",
            EnableTenantIsolation = true,
            Sqs = new SqsOptions
            {
                DefaultQueueUrl = TestQueueUrl,
                DlqUrl = null // No DLQ configured
            }
        };

        var provider = new SqsMessagingProvider(
            _mockSqsClient.Object,
            _mockTenantContext.Object,
            Options.Create(optionsWithoutDlq),
            NullLogger<SqsMessagingProvider>.Instance,
            TimeProvider.System);

        var invalidMessage = new Message
        {
            MessageId = "msg-no-dlq",
            Body = "{\"data\":\"test\"}",
            ReceiptHandle = "receipt-no-dlq",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>() // Missing tenant_id
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse
            {
                Messages = new List<Message> { invalidMessage }
            });

        // Act & Assert
        var exception = await Assert.ThrowsAsync<GenesisException>(
            async () => await provider.ReceiveAsync<Dictionary<string, string>>("test-queue", 10));

        exception.ErrorCode.Should().Be("DLQ_NOT_CONFIGURED");
    }
}
