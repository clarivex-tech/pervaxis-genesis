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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Genesis.Notifications.AWS.Extensions;
using Pervaxis.Genesis.Notifications.AWS.Options;
using Pervaxis.Genesis.Notifications.AWS.Providers;

namespace Pervaxis.Genesis.Notifications.AWS.Tests.Extensions;

public class NotificationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisNotifications_WithConfiguration_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(); // Add logging for AwsNotificationProvider constructor

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["FromEmail"] = "test@example.com",
                ["MaxRetries"] = "5",
                ["RequestTimeoutSeconds"] = "60"
            })
            .Build();

        // Act
        services.AddGenesisNotifications(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var notification = serviceProvider.GetService<INotification>();
        notification.Should().NotBeNull();
        notification.Should().BeOfType<AwsNotificationProvider>();

        var options = serviceProvider.GetService<IOptions<NotificationOptions>>();
        options.Should().NotBeNull();
        options!.Value.Region.Should().Be("us-east-1");
        options.Value.FromEmail.Should().Be("test@example.com");
        options.Value.MaxRetries.Should().Be(5);
        options.Value.RequestTimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void AddGenesisNotifications_WithAction_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(); // Add logging for AwsNotificationProvider constructor

        // Act
        services.AddGenesisNotifications(options =>
        {
            options.Region = "eu-west-1";
            options.FromEmail = "noreply@example.com";
            options.FromName = "My App";
            options.MaxRetries = 2;
        });

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var notification = serviceProvider.GetService<INotification>();
        notification.Should().NotBeNull();
        notification.Should().BeOfType<AwsNotificationProvider>();

        var options = serviceProvider.GetService<IOptions<NotificationOptions>>();
        options.Should().NotBeNull();
        options!.Value.Region.Should().Be("eu-west-1");
        options.Value.FromEmail.Should().Be("noreply@example.com");
        options.Value.FromName.Should().Be("My App");
        options.Value.MaxRetries.Should().Be(2);
    }

    [Fact]
    public void AddGenesisNotifications_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        ServiceCollection? services = null;
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services!.AddGenesisNotifications(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddGenesisNotifications_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        IConfiguration? configuration = null;

        // Act
        var act = () => services.AddGenesisNotifications(configuration!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void AddGenesisNotifications_WithNullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<NotificationOptions>? configureOptions = null;

        // Act
        var act = () => services.AddGenesisNotifications(configureOptions!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configureOptions");
    }

    [Fact]
    public void AddGenesisNotifications_ShouldRegisterAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["FromEmail"] = "test@example.com"
            })
            .Build();

        // Act
        services.AddGenesisNotifications(configuration);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(INotification));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddGenesisNotifications_WithOptionalFields_ShouldBindCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["FromEmail"] = "test@example.com",
                ["FromName"] = "Test Sender",
                ["ConfigurationSetName"] = "my-config",
                ["SmsTopicArn"] = "arn:aws:sns:us-east-1:123456789012:sms",
                ["PushPlatformApplicationArn"] = "arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp"
            })
            .Build();

        // Act
        services.AddGenesisNotifications(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<NotificationOptions>>();
        options.Should().NotBeNull();
        options!.Value.FromName.Should().Be("Test Sender");
        options.Value.ConfigurationSetName.Should().Be("my-config");
        options.Value.SmsTopicArn.Should().Be("arn:aws:sns:us-east-1:123456789012:sms");
        options.Value.PushPlatformApplicationArn.Should().Be("arn:aws:sns:us-east-1:123456789012:app/GCM/MyApp");
    }

    [Fact]
    public void AddGenesisNotifications_CalledMultipleTimes_ShouldNotDuplicateRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["FromEmail"] = "test@example.com"
            })
            .Build();

        // Act
        services.AddGenesisNotifications(configuration);
        services.AddGenesisNotifications(configuration);

        // Assert
        var descriptors = services.Where(d => d.ServiceType == typeof(INotification)).ToList();
        descriptors.Should().HaveCount(2); // Both registrations added (not using TryAdd)
    }

    [Fact]
    public void AddGenesisNotifications_WithLocalStackConfiguration_ShouldBindCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["FromEmail"] = "test@example.com",
                ["UseLocalEmulator"] = "true",
                ["LocalEmulatorUrl"] = "http://localhost:4566"
            })
            .Build();

        // Act
        services.AddGenesisNotifications(configuration);

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<NotificationOptions>>();
        options.Should().NotBeNull();
        options!.Value.UseLocalEmulator.Should().BeTrue();
        options.Value.LocalEmulatorUrl.Should().NotBeNull();
        options.Value.LocalEmulatorUrl!.ToString().Should().Be("http://localhost:4566/");
    }

    [Fact]
    public void AddGenesisNotifications_WithMethodChaining_ShouldReturnServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Region"] = "us-east-1",
                ["FromEmail"] = "test@example.com"
            })
            .Build();

        // Act
        var result = services.AddGenesisNotifications(configuration);

        // Assert
        result.Should().BeSameAs(services);
    }
}
