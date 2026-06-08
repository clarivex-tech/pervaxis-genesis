/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Genesis.Sanitization.Abstractions;
using Pervaxis.Genesis.Sanitization.Extensions;
using Pervaxis.Genesis.Sanitization.Options;

namespace Pervaxis.Genesis.Sanitization.Tests.Registration;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGenesisSanitization_WithConfiguration_RegistersISanitizer()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Genesis:Sanitization:DefaultProfile"] = "PlainText"
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(config);

        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetService<ISanitizer>();

        sanitizer.Should().NotBeNull();
    }

    [Fact]
    public void AddGenesisSanitization_WithAction_RegistersISanitizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.DefaultProfile = "SafeHtml";
        });

        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetService<ISanitizer>();

        sanitizer.Should().NotBeNull();
    }

    [Fact]
    public void AddGenesisSanitization_WithNullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var config = BuildConfiguration(new Dictionary<string, string?>());

        var act = () => services.AddGenesisSanitization(config);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("services");
    }

    [Fact]
    public void AddGenesisSanitization_WithNullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        IConfiguration config = null!;

        var act = () => services.AddGenesisSanitization(config);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("configuration");
    }

    [Fact]
    public void AddGenesisSanitization_WithNullAction_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action<SanitizationOptions> configureOptions = null!;

        var act = () => services.AddGenesisSanitization(configureOptions);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("configureOptions");
    }

    [Fact]
    public void AddGenesisSanitization_CalledTwice_DoesNotDuplicateRegistrations()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Genesis:Sanitization:DefaultProfile"] = "PlainText"
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(config);
        services.AddGenesisSanitization(config);

        var sanitizerDescriptors = services.Where(d => d.ServiceType == typeof(ISanitizer)).ToList();
        sanitizerDescriptors.Should().HaveCount(1);
    }

    [Fact]
    public void AddGenesisSanitization_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options => { });

        var provider = services.BuildServiceProvider();
        var sanitizer1 = provider.GetService<ISanitizer>();
        var sanitizer2 = provider.GetService<ISanitizer>();

        sanitizer1.Should().BeSameAs(sanitizer2);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
