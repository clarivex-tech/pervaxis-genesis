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
using Microsoft.Extensions.Logging.Nullogger;
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
    private readonly Mock<ITenantScopeFactory> _mockScopeFactory;
    private readonly MessagingOptions _options;
    private readonly SqsMessagingProvider _provider;
    private const string TestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
    private const string TestDlqUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-dlq";
    private const string ValidTenantId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301"; // Valid lowercase GUID format
    private const string ValidTenantId2 = "7c9e6679-7425-40de-944b-e07fc1f90ae7"; // Second valid tenant

    public SqsMessagingProviderPhase1ConsumerTests()
    {
        _mockSqsClient = new Mock<IAmazonSQS>();
        _mockTenantContext = new Mock<ITenantContext>();
        _mockScopeFactory = new Mock<ITenantScopeFactory>();

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
            _mockScopeFactory.Object,
            Options.Create(_options),
            NullLogger<SqsMessagingProvider>.Instance,
            TimeProvider.System);
    }

    public void Dispose()
    {
        _provider?.Dispose();
    }

    #region Validation Tests

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

    [Theory(DisplayName = "PHASE1B-CRITICAL-2: Invalid tenant ID formats send message to DLQ")]
    [InlineData("INVALID-FORMAT-123", "not a GUID")]
    [InlineData("Acme", "uppercase")]
    [InlineData("acme-platform", "contains reserved substring")]
    [InlineData("a-tenant-b", "contains -tenant- substring")]
    [InlineData("3F2504E0-4F89-41D3-9A0C-0305E82C3301", "uppercase GUID")]
    [InlineData("", "empty string")]
    [InlineData("  ", "whitespace")]
    public async Task ReceiveAsync_InvalidTenantIdFormat_SendsToDlq(string invalidTenantId, string reason)
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
                    StringValue = invalidTenantId
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
        result.Should().BeEmpty($"invalid tenant ID ({reason}) should be dead-lettered");
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
            _mockScopeFactory.Object,
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

    #endregion

    #region ReceiveWithMetadataAsync Tests

    [Fact(DisplayName = "PHASE1B-METADATA-1: ReceiveWithMetadataAsync returns tenant IDs")]
    public async Task ReceiveWithMetadataAsync_ValidMessages_ReturnsTenantIds()
    {
        // Arrange
        var messages = new List<Message>
        {
            new()
            {
                MessageId = "msg-1",
                Body = "{\"data\":\"test1\"}",
                ReceiptHandle = "receipt-1",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = ValidTenantId
                    }
                }
            },
            new()
            {
                MessageId = "msg-2",
                Body = "{\"data\":\"test2\"}",
                ReceiptHandle = "receipt-2",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = ValidTenantId2
                    }
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });

        // Act
        var result = await _provider.ReceiveWithMetadataAsync<Dictionary<string, string>>("test-queue", 10);

        // Assert
        result.Should().HaveCount(2);
        result.First().TenantId.Should().Be(ValidTenantId);
        result.First().MessageId.Should().Be("msg-1");
        result.Last().TenantId.Should().Be(ValidTenantId2);
        result.Last().MessageId.Should().Be("msg-2");
    }

    #endregion

    #region ProcessAsync Tests

    [Fact(DisplayName = "PHASE1B-PROCESS-1: ProcessAsync sets tenant context per message")]
    public async Task ProcessAsync_ValidMessages_SetsTenantContext()
    {
        // Arrange
        var messages = new List<Message>
        {
            new()
            {
                MessageId = "msg-1",
                Body = "{\"data\":\"test1\"}",
                ReceiptHandle = "receipt-1",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue { DataType = "String", StringValue = ValidTenantId }
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var mockScope = new Mock<IDisposable>();
        TenantId? capturedTenantId = null;

        _mockScopeFactory.Setup(x => x.BeginScope(It.IsAny<TenantId>()))
            .Callback<TenantId>(tid => capturedTenantId = tid)
            .Returns(mockScope.Object);

        // Act
        await _provider.ProcessAsync<Dictionary<string, string>>(
            "test-queue",
            async msg =>
            {
                await Task.CompletedTask;
            });

        // Assert
        capturedTenantId.Should().NotBeNull();
        capturedTenantId!.Value.Value.Should().Be(ValidTenantId);
        mockScope.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact(DisplayName = "PHASE1B-PROCESS-2: ProcessAsync clears context after handler success")]
    public async Task ProcessAsync_HandlerSucceeds_ClearsContext()
    {
        // Arrange
        var messages = new List<Message>
        {
            new()
            {
                MessageId = "msg-1",
                Body = "{\"data\":\"test\"}",
                ReceiptHandle = "receipt-1",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue { DataType = "String", StringValue = ValidTenantId }
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var mockScope = new Mock<IDisposable>();
        _mockScopeFactory.Setup(x => x.BeginScope(It.IsAny<TenantId>()))
            .Returns(mockScope.Object);

        // Act
        await _provider.ProcessAsync<Dictionary<string, string>>(
            "test-queue",
            async msg => await Task.CompletedTask);

        // Assert
        mockScope.Verify(x => x.Dispose(), Times.Once, "Context should be cleared after handler");
    }

    [Fact(DisplayName = "PHASE1B-PROCESS-3: ProcessAsync clears context after handler failure")]
    public async Task ProcessAsync_HandlerFails_ClearsContext()
    {
        // Arrange
        var messages = new List<Message>
        {
            new()
            {
                MessageId = "msg-fail",
                Body = "{\"data\":\"test\"}",
                ReceiptHandle = "receipt-fail",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue { DataType = "String", StringValue = ValidTenantId }
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });

        var mockScope = new Mock<IDisposable>();
        _mockScopeFactory.Setup(x => x.BeginScope(It.IsAny<TenantId>()))
            .Returns(mockScope.Object);

        // Act
        await _provider.ProcessAsync<Dictionary<string, string>>(
            "test-queue",
            async msg => { throw new InvalidOperationException("Handler failed"); });

        // Assert
        mockScope.Verify(x => x.Dispose(), Times.Once, "Context must be cleared even after handler failure");
        
        // Message should NOT be deleted when handler fails
        _mockSqsClient.Verify(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "PHASE1B-PROCESS-4: ProcessAsync deletes message only after handler success")]
    public async Task ProcessAsync_HandlerSucceeds_DeletesMessage()
    {
        // Arrange
        var messages = new List<Message>
        {
            new()
            {
                MessageId = "msg-success",
                Body = "{\"data\":\"test\"}",
                ReceiptHandle = "receipt-success",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue { DataType = "String", StringValue = ValidTenantId }
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var mockScope = new Mock<IDisposable>();
        _mockScopeFactory.Setup(x => x.BeginScope(It.IsAny<TenantId>()))
            .Returns(mockScope.Object);

        // Act
        await _provider.ProcessAsync<Dictionary<string, string>>(
            "test-queue",
            async msg => await Task.CompletedTask);

        // Assert
        _mockSqsClient.Verify(x => x.DeleteMessageAsync(
            It.Is<DeleteMessageRequest>(r => r.ReceiptHandle == "receipt-success"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "PHASE1B-LEAK-TEST: Concurrent messages never see another tenant's context")]
    public async Task ProcessAsync_ConcurrentMessages_NoContextLeakage()
    {
        // Arrange
        var messages = new List<Message>
        {
            new()
            {
                MessageId = "msg-tenant1",
                Body = "{\"tenant\":\"1\"}",
                ReceiptHandle = "receipt-1",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue { DataType = "String", StringValue = ValidTenantId }
                }
            },
            new()
            {
                MessageId = "msg-tenant2",
                Body = "{\"tenant\":\"2\"}",
                ReceiptHandle = "receipt-2",
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["pervaxis_tenant_id"] = new MessageAttributeValue { DataType = "String", StringValue = ValidTenantId2 }
                }
            }
        };

        _mockSqsClient.Setup(x => x.ReceiveMessageAsync(
            It.IsAny<ReceiveMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = messages });

        _mockSqsClient.Setup(x => x.DeleteMessageAsync(
            It.IsAny<DeleteMessageRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var tenant1Scope = new Mock<IDisposable>();
        var tenant2Scope = new Mock<IDisposable>();

        _mockScopeFactory.Setup(x => x.BeginScope(It.Is<TenantId>(t => t.Value == ValidTenantId)))
            .Returns(tenant1Scope.Object);
        _mockScopeFactory.Setup(x => x.BeginScope(It.Is<TenantId>(t => t.Value == ValidTenantId2)))
            .Returns(tenant2Scope.Object);

        var processedTenants = new System.Collections.Concurrent.ConcurrentBag<string>();
        var barrier = new System.Threading.Barrier(2); // Ensure both handlers run concurrently

        // Act
        await _provider.ProcessAsync<Dictionary<string, string>>(
            "test-queue",
            async msg =>
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(5)); // Force concurrent execution
                await Task.Delay(10); // Simulate work
                processedTenants.Add(msg["tenant"]);
            });

        // Assert
        processedTenants.Should().HaveCount(2);
        processedTenants.Should().Contain("1");
        processedTenants.Should().Contain("2");

        // Each tenant's scope was created and disposed
        _mockScopeFactory.Verify(x => x.BeginScope(It.Is<TenantId>(t => t.Value == ValidTenantId)), Times.Once);
        _mockScopeFactory.Verify(x => x.BeginScope(It.Is<TenantId>(t => t.Value == ValidTenantId2)), Times.Once);
        tenant1Scope.Verify(x => x.Dispose(), Times.Once);
        tenant2Scope.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion
}
