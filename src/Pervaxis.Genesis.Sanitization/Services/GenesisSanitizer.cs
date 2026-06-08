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

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Sanitization.Abstractions;
using Pervaxis.Genesis.Sanitization.Diagnostics;
using Pervaxis.Genesis.Sanitization.Options;

namespace Pervaxis.Genesis.Sanitization.Services;

/// <summary>
/// Default implementation of <see cref="ISanitizer"/> using HtmlSanitizer under the hood.
/// Thread-safe — each profile uses a pre-configured immutable HtmlSanitizer instance.
/// </summary>
internal sealed partial class GenesisSanitizer : ISanitizer
{
    private readonly ProfileRegistry _registry;
    private readonly int _maxInputLength;
    private readonly ILogger<GenesisSanitizer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisSanitizer"/> class.
    /// </summary>
    /// <param name="registry">The profile registry containing pre-configured sanitizers.</param>
    /// <param name="options">Sanitization options.</param>
    /// <param name="logger">Logger instance.</param>
    internal GenesisSanitizer(
        ProfileRegistry registry,
        IOptions<SanitizationOptions> options,
        ILogger<GenesisSanitizer> logger)
    {
        _registry = registry;
        _maxInputLength = options.Value.MaxInputLength;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string? StripAll(string? input)
    {
        return SanitizeCore(input, SanitizationProfile.PlainText.Name, "explicit");
    }

    /// <inheritdoc/>
    public string? SanitizeHtml(string? input)
    {
        return SanitizeCore(input, SanitizationProfile.SafeHtml.Name, "explicit");
    }

    /// <inheritdoc/>
    public string? Sanitize(string? input, SanitizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return SanitizeCore(input, profile.Name, "explicit");
    }

    /// <inheritdoc/>
    public string? Sanitize(string? input, string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        return SanitizeCore(input, profileName, "explicit");
    }

    /// <summary>
    /// Core sanitization method used by all public methods and integration points.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <param name="profileName">The profile name to use.</param>
    /// <param name="source">The invocation source for metrics/logging (explicit, attribute, middleware, fluentvalidation).</param>
    /// <returns>The sanitized string, or null/empty if input was null/empty.</returns>
    internal string? SanitizeCore(string? input, string profileName, string source)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length > _maxInputLength)
        {
            throw new ArgumentException(
                $"Input length ({input.Length}) exceeds the maximum allowed length ({_maxInputLength}).",
                nameof(input));
        }

        var (profile, sanitizer) = _registry.Get(profileName);

        var stopwatch = Stopwatch.StartNew();
        string result;

        try
        {
            if (profile.AllowedTags.Count == 0)
            {
                // PlainText mode: strip ALL HTML and return text content only
                result = StripAllHtml(input);
            }
            else
            {
                result = sanitizer.Sanitize(input);
            }
        }
        finally
        {
            stopwatch.Stop();
            RecordDuration(profileName, stopwatch.Elapsed.TotalMilliseconds);
        }

        RecordOperation(profileName, source);

        var threatDetected = !string.Equals(input, result, StringComparison.Ordinal);
        if (threatDetected)
        {
            RecordThreatDetected(profileName, source);
            SanitizationLogMessages.LogThreatDetected(
                _logger, profileName, source, input.Length, result.Length);
        }
        else
        {
            SanitizationLogMessages.LogCleanInput(_logger, profileName, source);
        }

        return result;
    }

    /// <summary>
    /// Strips all HTML tags and returns plain text content only.
    /// Decodes HTML entities in the output.
    /// </summary>
    private static string StripAllHtml(string input)
    {
        // Strip null bytes first (null byte injection attack vector)
        var cleaned = input.Replace("\0", string.Empty, StringComparison.Ordinal);
        // Remove script/style tags and their content entirely
        var withoutScripts = ScriptStyleRegex().Replace(cleaned, string.Empty);
        // Remove all remaining HTML tags but keep text content
        var textOnly = HtmlTagRegex().Replace(withoutScripts, string.Empty);
        // Decode HTML entities
        var decoded = WebUtility.HtmlDecode(textOnly);
        // Normalize whitespace (collapse multiple spaces/newlines to single space)
        var normalized = WhitespaceRegex().Replace(decoded, " ").Trim();
        return normalized;
    }

    [GeneratedRegex(@"<(script|style)[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Metric emission failures must not affect sanitization outcome.")]
    private static void RecordOperation(string profile, string source)
    {
        try
        {
            SanitizationMetrics.OperationsCounter.Add(1,
                new KeyValuePair<string, object?>("profile", profile),
                new KeyValuePair<string, object?>("source", source));
        }
        catch
        {
            // Suppress metric emission failures — must not affect sanitization outcome
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Metric emission failures must not affect sanitization outcome.")]
    private static void RecordThreatDetected(string profile, string source)
    {
        try
        {
            SanitizationMetrics.ThreatsDetectedCounter.Add(1,
                new KeyValuePair<string, object?>("profile", profile),
                new KeyValuePair<string, object?>("source", source));
        }
        catch
        {
            // Suppress metric emission failures
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Metric emission failures must not affect sanitization outcome.")]
    private static void RecordDuration(string profile, double durationMs)
    {
        try
        {
            SanitizationMetrics.DurationHistogram.Record(durationMs,
                new KeyValuePair<string, object?>("profile", profile));
        }
        catch
        {
            // Suppress metric emission failures
        }
    }
}
