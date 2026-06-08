/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Genesis.Sanitization.Abstractions;
using Pervaxis.Genesis.Sanitization.Extensions;
using Pervaxis.Genesis.Sanitization.Options;

namespace Pervaxis.Genesis.Sanitization.Tests.Services;

public class ProfileRegistryTests
{
    [Fact]
    public void Registry_ContainsAllBuiltInProfiles()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options => { });
        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetRequiredService<ISanitizer>();

        // Verify all built-in profiles work without exception
        sanitizer.Sanitize("test", "PlainText").Should().Be("test");
        sanitizer.Sanitize("<b>test</b>", "SafeHtml").Should().Be("<b>test</b>");
        sanitizer.Sanitize("<h1>test</h1>", "Markdown").Should().Be("<h1>test</h1>");
    }

    [Fact]
    public void Registry_LoadsCustomProfile()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.AllowCustomProfiles = true;
            options.CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["custom"] = new CustomProfileDefinition
                {
                    Name = "Custom",
                    AllowedTags = new List<string> { "b", "i" },
                    AllowedAttributes = new Dictionary<string, List<string>>(),
                    AllowedUrlSchemes = new List<string> { "https" }
                }
            };
        });
        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetRequiredService<ISanitizer>();

        var result = sanitizer.Sanitize("<b>bold</b><script>bad</script>", "Custom");
        result.Should().Be("<b>bold</b>");
    }

    [Fact]
    public void Registry_CustomProfileNameConflict_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.AllowCustomProfiles = true;
            options.CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["conflict"] = new CustomProfileDefinition
                {
                    Name = "SafeHtml", // Conflicts with built-in
                    AllowedTags = new List<string> { "b" }
                }
            };
        });

        var act = () => services.BuildServiceProvider().GetRequiredService<ISanitizer>();
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("conflicts with a built-in profile name");
    }

    [Fact]
    public void Registry_CustomProfileDisabled_IgnoresCustomProfiles()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.AllowCustomProfiles = false;
            options.CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["custom"] = new CustomProfileDefinition
                {
                    Name = "CustomIgnored",
                    AllowedTags = new List<string> { "b" }
                }
            };
        });
        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetRequiredService<ISanitizer>();

        var act = () => sanitizer.Sanitize("test", "CustomIgnored");
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("is not registered");
    }

    [Fact]
    public void Registry_CustomProfileWithEmptyTags_BehavesLikePlainText()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.AllowCustomProfiles = true;
            options.CustomProfiles = new Dictionary<string, CustomProfileDefinition>
            {
                ["strict"] = new CustomProfileDefinition
                {
                    Name = "Strict",
                    AllowedTags = new List<string>() // Empty = strip all
                }
            };
        });
        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetRequiredService<ISanitizer>();

        var result = sanitizer.Sanitize("<b>hello</b>", "Strict");
        result.Should().Be("hello");
    }
}
