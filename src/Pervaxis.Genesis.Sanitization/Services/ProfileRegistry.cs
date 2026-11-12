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

using System.Collections.Frozen;
using System.Collections.Immutable;
using Ganss.Xss;
using Pervaxis.Genesis.Sanitization.Abstractions;
using Pervaxis.Genesis.Sanitization.Options;

namespace Pervaxis.Genesis.Sanitization.Services;

/// <summary>
/// Registry of sanitization profiles and their pre-configured HtmlSanitizer instances.
/// Populated at startup, immutable after initialization.
/// </summary>
internal sealed class ProfileRegistry
{
    private readonly FrozenDictionary<string, (SanitizationProfile Profile, HtmlSanitizer Sanitizer)> _profiles;

    /// <summary>
    /// Initializes the registry with built-in profiles and optional custom profiles from configuration.
    /// </summary>
    /// <param name="options">Sanitization options containing custom profile definitions.</param>
    internal ProfileRegistry(SanitizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var profiles = new Dictionary<string, (SanitizationProfile, HtmlSanitizer)>(StringComparer.OrdinalIgnoreCase)
        {
            [SanitizationProfile.PlainText.Name] = (SanitizationProfile.PlainText, CreateSanitizer(SanitizationProfile.PlainText)),
            [SanitizationProfile.SafeHtml.Name] = (SanitizationProfile.SafeHtml, CreateSanitizer(SanitizationProfile.SafeHtml)),
            [SanitizationProfile.Markdown.Name] = (SanitizationProfile.Markdown, CreateSanitizer(SanitizationProfile.Markdown))
        };

        if (options.AllowCustomProfiles)
        {
            foreach (var (_, definition) in options.CustomProfiles)
            {
                if (SanitizationProfile.BuiltInNames.Contains(definition.Name))
                {
                    throw new InvalidOperationException(
                        $"Custom profile name '{definition.Name}' conflicts with a built-in profile name.");
                }

                var profile = CreateProfileFromDefinition(definition);
                profiles[profile.Name] = (profile, CreateSanitizer(profile));
            }
        }

        _profiles = profiles.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the profile and its configured sanitizer by name.
    /// </summary>
    /// <param name="profileName">The registered profile name.</param>
    /// <returns>The profile and its pre-configured HtmlSanitizer instance.</returns>
    /// <exception cref="ArgumentException">Thrown when profileName is not registered.</exception>
    internal (SanitizationProfile Profile, HtmlSanitizer Sanitizer) Get(string profileName)
    {
        if (!_profiles.TryGetValue(profileName, out var entry))
        {
            throw new ArgumentException(
                $"Sanitization profile '{profileName}' is not registered. " +
                $"Available profiles: {string.Join(", ", _profiles.Keys)}.",
                nameof(profileName));
        }

        return entry;
    }

    /// <summary>
    /// Gets the profile and sanitizer for a profile instance (by name lookup).
    /// </summary>
    /// <param name="profile">The profile instance.</param>
    /// <returns>The profile and its pre-configured HtmlSanitizer instance.</returns>
    internal (SanitizationProfile Profile, HtmlSanitizer Sanitizer) Get(SanitizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Get(profile.Name);
    }

    /// <summary>
    /// Checks whether a profile name is registered.
    /// </summary>
    /// <param name="profileName">The profile name to check.</param>
    /// <returns>True if the profile is registered; false otherwise.</returns>
    internal bool Contains(string profileName) => _profiles.ContainsKey(profileName);

    private static HtmlSanitizer CreateSanitizer(SanitizationProfile profile)
    {
        var sanitizer = new HtmlSanitizer();

        // Clear all defaults — start from empty and add explicitly
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();

        // Add allowed tags
        foreach (var tag in profile.AllowedTags)
        {
            sanitizer.AllowedTags.Add(tag);
        }

        // Add allowed attributes globally — HtmlSanitizer applies them across tags.
        // This is safe: href on a non-anchor tag is stripped because the tag itself isn't allowed.
        foreach (var (_, attributes) in profile.AllowedAttributes)
        {
            foreach (var attr in attributes)
            {
                sanitizer.AllowedAttributes.Add(attr);
            }
        }

        // Add allowed URL schemes
        foreach (var scheme in profile.AllowedUrlSchemes)
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        return sanitizer;
    }

    private static SanitizationProfile CreateProfileFromDefinition(CustomProfileDefinition definition)
    {
        var allowedAttributes = definition.AllowedAttributes
            .ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        return new SanitizationProfile(definition.Name)
        {
            AllowedTags = definition.AllowedTags.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = allowedAttributes,
            AllowedUrlSchemes = definition.AllowedUrlSchemes.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }
}
