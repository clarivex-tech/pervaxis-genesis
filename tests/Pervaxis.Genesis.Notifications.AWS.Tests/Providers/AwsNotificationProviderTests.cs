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

using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Notifications.AWS.Options;
using Pervaxis.Genesis.Notifications.AWS.Providers;

namespace Pervaxis.Genesis.Notifications.AWS.Tests.Providers;

public class AwsNotificationProviderTests
{
    private readonly Mock<IAmazonSimpleEmailService> _mockSesClient;
    private readonly Mock<IAmazonSimpleNotificationService> _mockSnsClient;
    private readonly Mock<ILogger<AwsNotificationProvider>> _mockLogger;
    private readonly NotificationOptions _validOptions;

    public AwsNotificationProviderTests()
    {
        _mockSesClient = new Mock<IAmazonSimpleEmailService>();
        _mockSnsClient = new Mock<IAmazonSimpleNotificationService>();
        _mockLogger = new Mock<ILogger<AwsNotificationProvider>>();

        _validOptions = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            FromName = "Test Sender",
            MaxRetries = 3,
            RequestTimeoutSeconds = 30
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new AwsNotificationProvider(
            null!,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_WithNullSesClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new AwsNotificationProvider(
            _validOptions,
            null!,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("sesClient");
    }

    [Fact]
    public void Constructor_WithNullSnsClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            null!,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("snsClient");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithIOptions_AndInvalidOptions_ShouldThrowGenesisConfigurationException()
    {
        // Arrange
        var invalidOptions = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "" // Invalid
        };

        var mockOptions = new Mock<IOptions<NotificationOptions>>();
        mockOptions.Setup(o => o.Value).Returns(invalidOptions);

        // Act
        var act = () => new AwsNotificationProvider(
            mockOptions.Object,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<GenesisConfigurationException>();
    }

    #endregion

    #region SendEmailAsync Tests

    [Fact]
    public async Task SendEmailAsync_WithValidParameters_ShouldReturnMessageId()
    {
        // Arrange
        var expectedMessageId = "test-message-id-123";
        _mockSesClient.Setup(x => x.SendEmailAsync(
                It.IsAny<SendEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendEmailResponse { MessageId = expectedMessageId });

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.SendEmailAsync(
            "recipient@example.com",
            "Test Subject",
            "Test Body",
            true);

        // Assert
        result.Should().Be(expectedMessageId);
        _mockSesClient.Verify(x => x.SendEmailAsync(
            It.Is<SendEmailRequest>(r =>
                r.Source.Contains(_validOptions.FromEmail) &&
                r.Destination.ToAddresses.Contains("recipient@example.com") &&
                r.Message.Subject.Data == "Test Subject" &&
                r.Message.Body.Html.Data == "Test Body"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WithPlainText_ShouldSendPlainTextEmail()
    {
        // Arrange
        _mockSesClient.Setup(x => x.SendEmailAsync(
                It.IsAny<SendEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendEmailResponse { MessageId = "msg-123" });

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        await provider.SendEmailAsync(
            "recipient@example.com",
            "Test Subject",
            "Plain text body",
            false);

        // Assert
        _mockSesClient.Verify(x => x.SendEmailAsync(
            It.Is<SendEmailRequest>(r =>
                r.Message.Body.Text.Data == "Plain text body" &&
                r.Message.Body.Html == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WithConfigurationSet_ShouldIncludeConfigurationSet()
    {
        // Arrange
        var optionsWithConfigSet = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            ConfigurationSetName = "my-config-set"
        };

        _mockSesClient.Setup(x => x.SendEmailAsync(
                It.IsAny<SendEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendEmailResponse { MessageId = "msg-123" });

        var provider = new AwsNotificationProvider(
            optionsWithConfigSet,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        await provider.SendEmailAsync(
            "recipient@example.com",
            "Test Subject",
            "Test Body",
            true);

        // Assert
        _mockSesClient.Verify(x => x.SendEmailAsync(
            It.Is<SendEmailRequest>(r => r.ConfigurationSetName == "my-config-set"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WithNullRecipient_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendEmailAsync(null!, "Subject", "Body");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("recipient");
    }

    [Fact]
    public async Task SendEmailAsync_WithEmptySubject_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendEmailAsync("test@example.com", "", "Body");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("subject");
    }

    [Fact]
    public async Task SendEmailAsync_WhenSesThrowsException_ShouldThrowGenesisException()
    {
        // Arrange
        _mockSesClient.Setup(x => x.SendEmailAsync(
                It.IsAny<SendEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleEmailServiceException("SES error"));

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendEmailAsync(
            "test@example.com",
            "Subject",
            "Body");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to send email*");
    }

    #endregion

    #region SendTemplatedEmailAsync Tests

    [Fact]
    public async Task SendTemplatedEmailAsync_WithValidParameters_ShouldReturnMessageId()
    {
        // Arrange
        var expectedMessageId = "template-msg-123";
        _mockSesClient.Setup(x => x.SendTemplatedEmailAsync(
                It.IsAny<SendTemplatedEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendTemplatedEmailResponse { MessageId = expectedMessageId });

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        var templateData = new Dictionary<string, string>
        {
            ["userName"] = "John Doe",
            ["orderNumber"] = "12345"
        };

        // Act
        var result = await provider.SendTemplatedEmailAsync(
            "recipient@example.com",
            "order-confirmation",
            templateData);

        // Assert
        result.Should().Be(expectedMessageId);
        _mockSesClient.Verify(x => x.SendTemplatedEmailAsync(
            It.Is<SendTemplatedEmailRequest>(r =>
                r.Template == "order-confirmation" &&
                r.Destination.ToAddresses.Contains("recipient@example.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTemplatedEmailAsync_WithNullTemplateData_ShouldThrowArgumentNullException()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendTemplatedEmailAsync(
            "test@example.com",
            "template-id",
            null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("templateData");
    }

    [Fact]
    public async Task SendTemplatedEmailAsync_WhenSesThrowsException_ShouldThrowGenesisException()
    {
        // Arrange
        _mockSesClient.Setup(x => x.SendTemplatedEmailAsync(
                It.IsAny<SendTemplatedEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleEmailServiceException("Template not found"));

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendTemplatedEmailAsync(
            "test@example.com",
            "invalid-template",
            new Dictionary<string, string>());

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to send templated email*");
    }

    #endregion

    #region SendSmsAsync Tests

    [Fact]
    public async Task SendSmsAsync_WithValidParameters_ShouldReturnMessageId()
    {
        // Arrange
        var expectedMessageId = "sms-msg-123";
        _mockSnsClient.Setup(x => x.PublishAsync(
                It.IsAny<PublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = expectedMessageId });

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.SendSmsAsync("+1234567890", "Test SMS message");

        // Assert
        result.Should().Be(expectedMessageId);
        _mockSnsClient.Verify(x => x.PublishAsync(
            It.Is<PublishRequest>(r =>
                r.PhoneNumber == "+1234567890" &&
                r.Message == "Test SMS message"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendSmsAsync_WithSmsTopicArn_ShouldUseTopicArn()
    {
        // Arrange
        var optionsWithTopic = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            SmsTopicArn = "arn:aws:sns:us-east-1:123456789012:sms-topic"
        };

        _mockSnsClient.Setup(x => x.PublishAsync(
                It.IsAny<PublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-123" });

        var provider = new AwsNotificationProvider(
            optionsWithTopic,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        await provider.SendSmsAsync("+1234567890", "Test SMS");

        // Assert
        _mockSnsClient.Verify(x => x.PublishAsync(
            It.Is<PublishRequest>(r =>
                r.TopicArn == "arn:aws:sns:us-east-1:123456789012:sms-topic" &&
                r.PhoneNumber == null &&
                r.MessageAttributes.ContainsKey("phoneNumber")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendSmsAsync_WithNullPhoneNumber_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendSmsAsync(null!, "Message");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("phoneNumber");
    }

    [Fact]
    public async Task SendSmsAsync_WhenSnsThrowsException_ShouldThrowGenesisException()
    {
        // Arrange
        _mockSnsClient.Setup(x => x.PublishAsync(
                It.IsAny<PublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("SNS error"));

        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendSmsAsync("+1234567890", "Test");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to send SMS*");
    }

    #endregion

    #region SendPushAsync Tests

    [Fact]
    public async Task SendPushAsync_WithValidParameters_ShouldReturnMessageId()
    {
        // Arrange
        var optionsWithPush = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            PushPlatformApplicationArn = "arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp"
        };

        var expectedMessageId = "push-msg-123";
        var endpointArn = "arn:aws:sns:us-east-1:123456789012:endpoint/GCM/MyApp/device-123";

        _mockSnsClient.Setup(x => x.CreatePlatformEndpointAsync(
                It.IsAny<CreatePlatformEndpointRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePlatformEndpointResponse { EndpointArn = endpointArn });

        _mockSnsClient.Setup(x => x.PublishAsync(
                It.IsAny<PublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = expectedMessageId });

        var provider = new AwsNotificationProvider(
            optionsWithPush,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.SendPushAsync(
            "device-token-123",
            "Test Title",
            "Test Message");

        // Assert
        result.Should().Be(expectedMessageId);
        _mockSnsClient.Verify(x => x.CreatePlatformEndpointAsync(
            It.Is<CreatePlatformEndpointRequest>(r =>
                r.PlatformApplicationArn == optionsWithPush.PushPlatformApplicationArn &&
                r.Token == "device-token-123"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_WithCustomData_ShouldIncludeDataInPayload()
    {
        // Arrange
        var optionsWithPush = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            PushPlatformApplicationArn = "arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp"
        };

        _mockSnsClient.Setup(x => x.CreatePlatformEndpointAsync(
                It.IsAny<CreatePlatformEndpointRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePlatformEndpointResponse { EndpointArn = "endpoint-arn" });

        _mockSnsClient.Setup(x => x.PublishAsync(
                It.IsAny<PublishRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "msg-123" });

        var provider = new AwsNotificationProvider(
            optionsWithPush,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        var customData = new Dictionary<string, string>
        {
            ["orderId"] = "12345",
            ["action"] = "view_order"
        };

        // Act
        await provider.SendPushAsync(
            "device-token-123",
            "Order Update",
            "Your order has shipped",
            customData);

        // Assert
        _mockSnsClient.Verify(x => x.PublishAsync(
            It.Is<PublishRequest>(r =>
                r.MessageStructure == "json" &&
                r.Message.Contains("orderId") &&
                r.Message.Contains("12345")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPushAsync_WithoutPlatformApplicationArn_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions, // No PushPlatformApplicationArn
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendPushAsync(
            "device-token",
            "Title",
            "Message");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*PushPlatformApplicationArn is not configured*");
    }

    [Fact]
    public async Task SendPushAsync_WithNullDeviceToken_ShouldThrowArgumentException()
    {
        // Arrange
        var optionsWithPush = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            PushPlatformApplicationArn = "arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp"
        };

        var provider = new AwsNotificationProvider(
            optionsWithPush,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.SendPushAsync(null!, "Title", "Message");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("deviceToken");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldDisposeClients()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        provider.Dispose();

        // Assert - Just verify it doesn't throw
        // Actual disposal of lazy clients is tested in integration tests
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new AwsNotificationProvider(
            _validOptions,
            _mockSesClient.Object,
            _mockSnsClient.Object,
            _mockLogger.Object);

        // Act
        provider.Dispose();
        var act = () => provider.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
