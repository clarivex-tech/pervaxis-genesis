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

using FluentAssertions;
using Pervaxis.Genesis.Notifications.AWS.Options;

namespace Pervaxis.Genesis.Notifications.AWS.Tests.Options;

public class NotificationOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_ReturnsTrue()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            MaxRetries = 3,
            RequestTimeoutSeconds = 30
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMissingRegion_ReturnsFalse()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "",
            FromEmail = "test@example.com"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithMissingFromEmail_ReturnsFalse()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = ""
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithInvalidEmailFormat_ReturnsFalse()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "not-an-email"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithNegativeMaxRetries_ReturnsFalse()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            MaxRetries = -1
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithZeroOrNegativeTimeout_ReturnsFalse()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            RequestTimeoutSeconds = 0
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithOptionalFieldsSet_ReturnsTrue()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            FromName = "Test Sender",
            ConfigurationSetName = "my-config-set",
            SmsTopicArn = "arn:aws:sns:us-east-1:123456789012:sms-topic",
            PushPlatformApplicationArn = "arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var options = new NotificationOptions();

        // Assert
        options.MaxRetries.Should().Be(3);
        options.RequestTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Validate_WithUseLocalEmulatorButNoUrl_ReturnsFalse()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            UseLocalEmulator = true,
            LocalEmulatorUrl = null
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithUseLocalEmulatorAndValidUrl_ReturnsTrue()
    {
        // Arrange
        var options = new NotificationOptions
        {
            Region = "us-east-1",
            FromEmail = "test@example.com",
            UseLocalEmulator = true,
            LocalEmulatorUrl = new Uri("http://localhost:4566")
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }
}
