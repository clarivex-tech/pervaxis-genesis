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

namespace Pervaxis.Genesis.Sanitization.Abstractions;

/// <summary>
/// Primary abstraction for input sanitization operations.
/// All implementations must be thread-safe for concurrent calls.
/// </summary>
public interface ISanitizer
{
    /// <summary>
    /// Strips ALL HTML tags and script content — returns plain text only.
    /// Decodes HTML entities in the output.
    /// </summary>
    /// <param name="input">The input string to sanitize. Null/empty returns unchanged.</param>
    /// <returns>Plain text with all HTML removed, or null if input was null.</returns>
    string? StripAll(string? input);

    /// <summary>
    /// Allows safe HTML (bold, italic, links, lists) — strips scripts, event handlers, iframes.
    /// Uses the SafeHtml built-in profile.
    /// </summary>
    /// <param name="input">The input string to sanitize. Null/empty returns unchanged.</param>
    /// <returns>HTML with only safe elements preserved, or null if input was null.</returns>
    string? SanitizeHtml(string? input);

    /// <summary>
    /// Applies sanitization using the specified profile instance.
    /// </summary>
    /// <param name="input">The input string to sanitize. Null/empty returns unchanged.</param>
    /// <param name="profile">The sanitization profile defining allowed elements.</param>
    /// <returns>Sanitized string per profile rules, or null if input was null.</returns>
    string? Sanitize(string? input, SanitizationProfile profile);

    /// <summary>
    /// Applies sanitization using a named profile from the registry.
    /// </summary>
    /// <param name="input">The input string to sanitize. Null/empty returns unchanged.</param>
    /// <param name="profileName">The registered profile name.</param>
    /// <returns>Sanitized string per profile rules, or null if input was null.</returns>
    /// <exception cref="ArgumentException">Thrown when profileName is not found in registry.</exception>
    string? Sanitize(string? input, string profileName);
}
