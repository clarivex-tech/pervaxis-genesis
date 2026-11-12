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

using System.Collections.Immutable;

namespace Pervaxis.Genesis.Sanitization.Abstractions;

/// <summary>
/// Defines a sanitization profile with allowed tags, attributes, and URL schemes.
/// Built-in profiles are exposed as static readonly instances.
/// </summary>
public sealed class SanitizationProfile
{
    /// <summary>
    /// Strips ALL HTML — returns plain text only.
    /// </summary>
    public static readonly SanitizationProfile PlainText = new("PlainText")
    {
        AllowedTags = ImmutableHashSet<string>.Empty,
        AllowedAttributes = ImmutableDictionary<string, ImmutableHashSet<string>>.Empty,
        AllowedUrlSchemes = ImmutableHashSet<string>.Empty
    };

    /// <summary>
    /// Allows safe structural/formatting HTML. Strips scripts, iframes, event handlers.
    /// </summary>
    public static readonly SanitizationProfile SafeHtml = new("SafeHtml")
    {
        AllowedTags = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "b", "i", "strong", "em", "a", "ul", "ol", "li", "p", "br", "span"),
        AllowedAttributes = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase,
            new[]
            {
                KeyValuePair.Create("a", ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "href"))
            }),
        AllowedUrlSchemes = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "http", "https", "mailto")
    };

    /// <summary>
    /// Allows Markdown-rendered HTML subset. Extends SafeHtml with code blocks, headings, tables, images.
    /// </summary>
    public static readonly SanitizationProfile Markdown = new("Markdown")
    {
        AllowedTags = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "b", "i", "strong", "em", "a", "ul", "ol", "li", "p", "br", "span",
            "code", "pre", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote",
            "table", "thead", "tbody", "tr", "th", "td", "img", "hr", "dl", "dt", "dd"),
        AllowedAttributes = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase,
            new[]
            {
                KeyValuePair.Create("a", ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "href")),
                KeyValuePair.Create("img", ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "src", "alt")),
                KeyValuePair.Create("code", ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "class")),
                KeyValuePair.Create("pre", ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "class"))
            }),
        AllowedUrlSchemes = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "http", "https")
    };

    /// <summary>
    /// Gets the unique name of this profile.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the set of allowed HTML tag names.
    /// </summary>
    public ImmutableHashSet<string> AllowedTags { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Gets the allowed attributes per tag (tag name → set of attribute names).
    /// </summary>
    public ImmutableDictionary<string, ImmutableHashSet<string>> AllowedAttributes { get; init; }
        = ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

    /// <summary>
    /// Gets the allowed URL schemes for URL-accepting attributes.
    /// </summary>
    public ImmutableHashSet<string> AllowedUrlSchemes { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Creates a new sanitization profile with the given name.
    /// </summary>
    /// <param name="name">The unique name for this profile.</param>
    public SanitizationProfile(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the set of built-in profile names.
    /// </summary>
    internal static readonly ImmutableHashSet<string> BuiltInNames = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase, "PlainText", "SafeHtml", "Markdown");
}
