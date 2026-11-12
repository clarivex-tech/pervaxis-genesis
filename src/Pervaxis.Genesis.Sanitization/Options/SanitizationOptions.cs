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
using Pervaxis.Core.Abstractions.Genesis;
using Pervaxis.Genesis.Sanitization.Abstractions;

namespace Pervaxis.Genesis.Sanitization.Options;

/// <summary>
/// Configuration for the Genesis Sanitization module.
/// Bound from the "Genesis:Sanitization" configuration section.
/// </summary>
public sealed class SanitizationOptions : GenesisOptionsBase
{
    /// <summary>
    /// Gets or sets the default profile name used when no profile is explicitly specified.
    /// Must match a built-in or custom profile name. Default: "PlainText".
    /// </summary>
    public string DefaultProfile { get; set; } = "PlainText";

    /// <summary>
    /// Gets or sets whether custom profiles from configuration are loaded.
    /// Default: true.
    /// </summary>
    public bool AllowCustomProfiles { get; set; } = true;

    /// <summary>
    /// Gets or sets custom profile definitions keyed by name.
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for IConfiguration binding.")]
    public Dictionary<string, CustomProfileDefinition> CustomProfiles { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum input string length accepted for sanitization.
    /// Inputs exceeding this length throw <see cref="ArgumentException"/>.
    /// Valid range: 1–10,000,000. Default: 1,000,000.
    /// </summary>
    public int MaxInputLength { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets whether the global sanitization middleware is active.
    /// Default: false (opt-in).
    /// </summary>
    public bool EnableMiddleware { get; set; }

    /// <summary>
    /// Gets or sets route patterns excluded from middleware sanitization.
    /// Supports literal paths and wildcard patterns (e.g., "/api/admin/*").
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Required for IConfiguration binding.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Required for IConfiguration binding.")]
    public List<string> MiddlewareExcludedRoutes { get; set; } = new();

    /// <inheritdoc/>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(DefaultProfile))
        {
            return false;
        }

        if (MaxInputLength is < 1 or > 10_000_000)
        {
            return false;
        }

        if (AllowCustomProfiles)
        {
            foreach (var (_, definition) in CustomProfiles)
            {
                if (string.IsNullOrWhiteSpace(definition.Name))
                {
                    return false;
                }

                if (SanitizationProfile.BuiltInNames.Contains(definition.Name))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
