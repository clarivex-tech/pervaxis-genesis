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

namespace Pervaxis.Genesis.Sanitization.Tests.Services;

public class GenesisSanitizerTests
{
    private readonly ISanitizer _sanitizer;

    public GenesisSanitizerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.DefaultProfile = "PlainText";
            options.MaxInputLength = 1_000_000;
        });
        var provider = services.BuildServiceProvider();
        _sanitizer = provider.GetRequiredService<ISanitizer>();
    }

    // === StripAll ===

    [Fact]
    public void StripAll_WithNull_ReturnsNull()
    {
        _sanitizer.StripAll(null).Should().BeNull();
    }

    [Fact]
    public void StripAll_WithEmpty_ReturnsEmpty()
    {
        _sanitizer.StripAll(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void StripAll_WithPlainText_ReturnsUnchanged()
    {
        _sanitizer.StripAll("Hello World").Should().Be("Hello World");
    }

    [Fact]
    public void StripAll_WithHtmlTags_StripsEverything()
    {
        _sanitizer.StripAll("<b>Hello</b> <i>World</i>").Should().Be("Hello World");
    }

    [Fact]
    public void StripAll_WithScriptTag_StripsCompletely()
    {
        _sanitizer.StripAll("<script>alert('xss')</script>Hello").Should().Be("Hello");
    }

    [Fact]
    public void StripAll_WithNestedTags_StripsAll()
    {
        _sanitizer.StripAll("<div><p><b>Text</b></p></div>").Should().Be("Text");
    }

    // === SanitizeHtml ===

    [Fact]
    public void SanitizeHtml_WithNull_ReturnsNull()
    {
        _sanitizer.SanitizeHtml(null).Should().BeNull();
    }

    [Fact]
    public void SanitizeHtml_WithEmpty_ReturnsEmpty()
    {
        _sanitizer.SanitizeHtml(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeHtml_AllowsBoldAndItalic()
    {
        var input = "<b>bold</b> and <i>italic</i>";
        _sanitizer.SanitizeHtml(input).Should().Be(input);
    }

    [Fact]
    public void SanitizeHtml_AllowsStrong()
    {
        var input = "<strong>important</strong>";
        _sanitizer.SanitizeHtml(input).Should().Be(input);
    }

    [Fact]
    public void SanitizeHtml_AllowsLinks()
    {
        var input = """<a href="https://example.com">link</a>""";
        _sanitizer.SanitizeHtml(input).Should().Be(input);
    }

    [Fact]
    public void SanitizeHtml_AllowsLists()
    {
        var input = "<ul><li>item 1</li><li>item 2</li></ul>";
        _sanitizer.SanitizeHtml(input).Should().Be(input);
    }

    [Fact]
    public void SanitizeHtml_StripsScript()
    {
        var input = "<b>Hello</b><script>alert('xss')</script>";
        _sanitizer.SanitizeHtml(input).Should().Be("<b>Hello</b>");
    }

    [Fact]
    public void SanitizeHtml_StripsIframe()
    {
        var input = "<p>Text</p><iframe src=\"evil.com\"></iframe>";
        _sanitizer.SanitizeHtml(input).Should().Be("<p>Text</p>");
    }

    [Fact]
    public void SanitizeHtml_StripsEventHandlers()
    {
        var input = """<b onclick="alert('xss')">text</b>""";
        _sanitizer.SanitizeHtml(input).Should().Be("<b>text</b>");
    }

    [Fact]
    public void SanitizeHtml_StripsJavascriptUrl()
    {
        var input = """<a href="javascript:alert('xss')">click</a>""";
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("javascript:");
    }

    [Fact]
    public void SanitizeHtml_StripsDisallowedTags()
    {
        var input = "<div>content</div><object>data</object>";
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("<div>").And.NotContain("<object>");
    }

    // === Sanitize by profile name ===

    [Fact]
    public void Sanitize_WithValidProfileName_Sanitizes()
    {
        var input = "<script>bad</script><b>good</b>";
        var result = _sanitizer.Sanitize(input, "SafeHtml");
        result.Should().Be("<b>good</b>");
    }

    [Fact]
    public void Sanitize_WithInvalidProfileName_ThrowsArgumentException()
    {
        var act = () => _sanitizer.Sanitize("test", "NonExistentProfile");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sanitize_WithNullProfileName_ThrowsArgumentException()
    {
        var act = () => _sanitizer.Sanitize("test", (string)null!);
        act.Should().Throw<ArgumentException>();
    }

    // === Sanitize by profile instance ===

    [Fact]
    public void Sanitize_WithPlainTextProfile_StripsAll()
    {
        var input = "<b>hello</b>";
        _sanitizer.Sanitize(input, SanitizationProfile.PlainText).Should().Be("hello");
    }

    [Fact]
    public void Sanitize_WithMarkdownProfile_AllowsCodeBlocks()
    {
        var input = "<pre><code class=\"language-csharp\">var x = 1;</code></pre>";
        _sanitizer.Sanitize(input, SanitizationProfile.Markdown).Should().Be(input);
    }

    [Fact]
    public void Sanitize_WithMarkdownProfile_AllowsHeadings()
    {
        var input = "<h1>Title</h1><h2>Subtitle</h2>";
        _sanitizer.Sanitize(input, SanitizationProfile.Markdown).Should().Be(input);
    }

    [Fact]
    public void Sanitize_WithMarkdownProfile_AllowsImages()
    {
        var input = """<img src="https://example.com/img.png" alt="photo">""";
        _sanitizer.Sanitize(input, SanitizationProfile.Markdown).Should().Be(input);
    }

    [Fact]
    public void Sanitize_WithMarkdownProfile_StripsScript()
    {
        var input = "<h1>Title</h1><script>alert('xss')</script>";
        _sanitizer.Sanitize(input, SanitizationProfile.Markdown).Should().Be("<h1>Title</h1>");
    }

    // === MaxInputLength ===

    [Fact]
    public void StripAll_ExceedingMaxLength_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options =>
        {
            options.MaxInputLength = 10;
        });
        var provider = services.BuildServiceProvider();
        var sanitizer = provider.GetRequiredService<ISanitizer>();

        var act = () => sanitizer.StripAll(new string('a', 11));
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("exceeds the maximum allowed length");
    }

    // === Thread safety ===

    [Fact]
    public void Sanitize_ConcurrentCalls_AllReturnSameResult()
    {
        var input = "<script>xss</script><b>safe</b>";
        var expected = "<b>safe</b>";
        var results = new string?[100];

        Parallel.For(0, 100, i =>
        {
            results[i] = _sanitizer.SanitizeHtml(input);
        });

        results.Should().AllBe(expected);
    }
}
