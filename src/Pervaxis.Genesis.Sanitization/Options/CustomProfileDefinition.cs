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

using System.Diagnostics.CodeAnalysis;

namespace Pervaxis.Genesis.Sanitization.Options;

/// <summary>
/// Defines a custom sanitization profile via configuration.
/// </summary>
public sealed class CustomProfileDefinition
{
    /// <summary>
    /// Gets or sets the unique profile name. Must not conflict with built-in profile names.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the HTML tags allowed by this profile.
    /// An empty list is equivalent to PlainText (strip all HTML).
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for IConfiguration binding.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Required for IConfiguration binding.")]
    public List<string> AllowedTags { get; set; } = new();

    /// <summary>
    /// Gets or sets the attributes allowed per tag (tag name → list of attribute names).
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for IConfiguration binding.")]
    public Dictionary<string, List<string>> AllowedAttributes { get; set; } = new();

    /// <summary>
    /// Gets or sets the URL schemes allowed in URL-accepting attributes.
    /// Default: ["http", "https"].
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for IConfiguration binding.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Required for IConfiguration binding.")]
    public List<string> AllowedUrlSchemes { get; set; } = new() { "http", "https" };
}
