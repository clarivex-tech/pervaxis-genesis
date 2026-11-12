/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Microsoft.Extensions.DependencyInjection;
using Pervaxis.Genesis.Sanitization.Abstractions;
using Pervaxis.Genesis.Sanitization.Extensions;

namespace Pervaxis.Genesis.Sanitization.Tests.Security;

/// <summary>
/// XSS bypass resistance tests based on OWASP XSS Filter Evasion Cheat Sheet vectors.
/// Verifies that the sanitizer handles known bypass techniques.
/// </summary>
public class XssBypassTests
{
    private readonly ISanitizer _sanitizer;

    public XssBypassTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGenesisSanitization(options => { });
        var provider = services.BuildServiceProvider();
        _sanitizer = provider.GetRequiredService<ISanitizer>();
    }

    // === Script Tag Variations ===

    [Theory]
    [InlineData("<script>alert('XSS')</script>")]
    [InlineData("<SCRIPT>alert('XSS')</SCRIPT>")]
    [InlineData("<ScRiPt>alert('XSS')</ScRiPt>")]
    [InlineData("<script src=\"http://evil.com/xss.js\"></script>")]
    [InlineData("<script>document.cookie</script>")]
    public void StripAll_ScriptTagVariations_StripsAll(string input)
    {
        var result = _sanitizer.StripAll(input);
        result.Should().NotContain("<script", because: "script tags must be removed");
        result.Should().NotContain("alert(");
    }

    // === Event Handler Attributes ===

    [Theory]
    [InlineData("""<img src="x" onerror="alert('XSS')">""")]
    [InlineData("""<body onload="alert('XSS')">""")]
    [InlineData("""<div onmouseover="alert('XSS')">hover</div>""")]
    [InlineData("""<input onfocus="alert('XSS')" autofocus>""")]
    [InlineData("""<svg onload="alert('XSS')">""")]
    [InlineData("""<marquee onstart="alert('XSS')">""")]
    public void SanitizeHtml_EventHandlers_StripsHandlers(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("onerror", because: "event handlers must be stripped");
        result.Should().NotContain("onload");
        result.Should().NotContain("onmouseover");
        result.Should().NotContain("onfocus");
        result.Should().NotContain("onstart");
        result.Should().NotContain("alert(");
    }

    // === JavaScript URL Schemes ===

    [Theory]
    [InlineData("""<a href="javascript:alert('XSS')">click</a>""")]
    [InlineData("""<a href="JAVASCRIPT:alert('XSS')">click</a>""")]
    [InlineData("""<a href="javascript&#58;alert('XSS')">click</a>""")]
    [InlineData("""<a href="&#106;avascript:alert('XSS')">click</a>""")]
    [InlineData("""<a href="vbscript:MsgBox('XSS')">click</a>""")]
    public void SanitizeHtml_JavascriptUrls_StripsOrRemovesHref(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("javascript:", because: "javascript: URIs must be stripped");
        result.Should().NotContain("vbscript:");
    }

    // === Iframe and Embed Vectors ===

    [Theory]
    [InlineData("<iframe src=\"http://evil.com\"></iframe>")]
    [InlineData("<object data=\"data:text/html,<script>alert('XSS')</script>\"></object>")]
    [InlineData("<embed src=\"data:text/html,<script>alert('XSS')</script>\">")]
    [InlineData("<iframe src=\"javascript:alert('XSS')\"></iframe>")]
    public void SanitizeHtml_EmbeddedContent_StripsCompletely(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("<iframe");
        result.Should().NotContain("<object");
        result.Should().NotContain("<embed");
    }

    // === Style/CSS Injection ===

    [Theory]
    [InlineData("""<div style="background:url(javascript:alert('XSS'))">text</div>""")]
    [InlineData("""<div style="width:expression(alert('XSS'))">text</div>""")]
    [InlineData("<style>body{background:url(javascript:alert('XSS'))}</style>")]
    public void SanitizeHtml_CssInjection_StripsStyles(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("javascript:");
        result.Should().NotContain("expression(");
        result.Should().NotContain("<style>");
    }

    // === SVG and MathML Vectors ===

    [Theory]
    [InlineData("""<svg onload="alert('XSS')"><circle r="50"/></svg>""")]
    [InlineData("""<svg><script>alert('XSS')</script></svg>""")]
    [InlineData("""<math><maction actiontype="statusline#http://evil.com"><mtext>click</mtext></maction></math>""")]
    public void SanitizeHtml_SvgAndMathMl_StripsCompletely(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("<svg");
        result.Should().NotContain("<math");
        result.Should().NotContain("alert(");
    }

    // === Case Variation Attacks ===

    [Theory]
    [InlineData("<SCRIPT>alert('XSS')</SCRIPT>")]
    [InlineData("<scRiPt>alert('XSS')</ScRIPT>")]
    [InlineData("<Script Src=\"evil.js\"></scRIPT>")]
    public void StripAll_CaseVariation_StripsRegardlessOfCase(string input)
    {
        var result = _sanitizer.StripAll(input);
        result.Should().NotContainEquivalentOf("<script");
        result.Should().NotContain("alert(");
    }

    // === Data URI Scheme ===

    [Theory]
    [InlineData("""<a href="data:text/html,<script>alert('XSS')</script>">click</a>""")]
    [InlineData("""<img src="data:image/svg+xml,<svg onload=alert('XSS')>">""")]
    public void SanitizeHtml_DataUri_StripsOrRemoves(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("data:");
        result.Should().NotContain("alert(");
    }

    // === Encoded Attacks ===

    [Theory]
    [InlineData("<a href=\"&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert('XSS')\">click</a>")]
    [InlineData("""<img src="&#x6A;&#x61;&#x76;&#x61;&#x73;&#x63;&#x72;&#x69;&#x70;&#x74;&#x3A;alert('XSS')">""")]
    public void SanitizeHtml_HtmlEntityEncoded_StillStrips(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("javascript:");
        result.Should().NotContain("alert(");
    }

    // === Null Byte Injection ===

    [Fact]
    public void StripAll_NullByteInTag_Strips()
    {
        var input = "<scr\0ipt>alert('XSS')</scr\0ipt>";
        var result = _sanitizer.StripAll(input);
        result.Should().NotContain("alert(");
    }

    // === Form Injection ===

    [Theory]
    [InlineData("""<form action="http://evil.com"><input type="submit"></form>""")]
    [InlineData("""<input type="image" src="javascript:alert('XSS')">""")]
    [InlineData("""<button formaction="javascript:alert('XSS')">Submit</button>""")]
    public void SanitizeHtml_FormInjection_StripsFormElements(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("<form");
        result.Should().NotContain("<input");
        result.Should().NotContain("<button");
    }

    // === Meta/Base/Link Redirect ===

    [Theory]
    [InlineData("""<meta http-equiv="refresh" content="0;url=http://evil.com">""")]
    [InlineData("""<base href="http://evil.com">""")]
    [InlineData("""<link rel="stylesheet" href="http://evil.com/xss.css">""")]
    public void SanitizeHtml_MetaBaseLink_StripsCompletely(string input)
    {
        var result = _sanitizer.SanitizeHtml(input);
        result.Should().NotContain("<meta");
        result.Should().NotContain("<base");
        result.Should().NotContain("<link");
    }

    // === StripAll ensures no HTML survives ===

    [Fact]
    public void StripAll_ComplexNestedAttack_ReturnsPlainTextOnly()
    {
        var input = """
            <div><p><b onclick="steal()">Hello</b></p>
            <script>document.cookie</script>
            <iframe src="evil.com"></iframe>
            <a href="javascript:void(0)">link</a>
            <img src="x" onerror="alert(1)">
            World</div>
            """;

        var result = _sanitizer.StripAll(input);
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().Contain("Hello");
        result.Should().Contain("World");
    }

    // === Idempotence ===

    [Fact]
    public void StripAll_AppliedTwice_ProducesSameResult()
    {
        var input = "<script>alert('xss')</script><b>Hello</b>";
        var first = _sanitizer.StripAll(input);
        var second = _sanitizer.StripAll(first);

        first.Should().Be(second, because: "StripAll should be idempotent");
    }

    [Fact]
    public void SanitizeHtml_AppliedTwice_ProducesSameResult()
    {
        var input = "<script>alert('xss')</script><b>Hello</b>";
        var first = _sanitizer.SanitizeHtml(input);
        var second = _sanitizer.SanitizeHtml(first);

        first.Should().Be(second, because: "SanitizeHtml should be idempotent");
    }
}
