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

using System.Text.Json;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.TransactionalLogging.AWS.Options;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Sanitization;

/// <summary>
/// Sanitizes operation parameters by redacting sensitive values.
/// Thread-safe, stateless after construction.
/// </summary>
internal sealed class ParameterSanitizer
{
    private static readonly HashSet<string> DefaultSensitivePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "secret",
        "token",
        "key",
        "credential",
        "auth",
        "connectionstring",
        "apikey",
        "private"
    };

    private readonly HashSet<string> _allPatterns;
    private readonly bool _sanitizeEnabled;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ParameterSanitizer"/> class.
    /// </summary>
    /// <param name="options">Transactional logging options containing sanitization configuration.</param>
    public ParameterSanitizer(IOptions<TransactionalLoggingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var opts = options.Value;
        _sanitizeEnabled = opts.SanitizeParameters;
        _allPatterns = new HashSet<string>(DefaultSensitivePatterns, StringComparer.OrdinalIgnoreCase);

        foreach (var custom in opts.SensitiveKeys)
        {
            _allPatterns.Add(custom);
        }
    }

    /// <summary>
    /// Sanitizes the given parameters by redacting sensitive values.
    /// </summary>
    /// <param name="parameters">The parameters object to sanitize. Can be an anonymous object or dictionary.</param>
    /// <returns>A sanitized dictionary of parameter key-value pairs, or null if input is null.</returns>
    public Dictionary<string, object?>? Sanitize(object? parameters)
    {
        if (parameters is null)
        {
            return null;
        }

        var dict = ConvertToDictionary(parameters);
        if (dict is null || dict.Count == 0)
        {
            return dict;
        }

        if (!_sanitizeEnabled)
        {
            return dict;
        }

        foreach (var key in dict.Keys.ToList())
        {
            if (IsSensitive(key))
            {
                dict[key] = "[REDACTED]";
            }
        }

        return dict;
    }

    private bool IsSensitive(string key)
    {
        return _allPatterns.Any(pattern =>
            key.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, object?>? ConvertToDictionary(object parameters)
    {
        if (parameters is Dictionary<string, object?> alreadyDict)
        {
            return new Dictionary<string, object?>(alreadyDict);
        }

        if (parameters is IDictionary<string, string> stringDict)
        {
            return stringDict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        }

        // Serialize and deserialize to flatten anonymous objects
        try
        {
            var json = JsonSerializer.Serialize(parameters, JsonOptions);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // If serialization fails, return a single-entry dict with the toString
            return new Dictionary<string, object?> { ["value"] = parameters.ToString() };
        }
    }
}
