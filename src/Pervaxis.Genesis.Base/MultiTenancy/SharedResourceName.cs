/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

namespace Pervaxis.Genesis.Base.MultiTenancy;

/// <summary>Validates relative names before adding a trusted resource prefix.</summary>
public static class SharedResourceName
{
    /// <summary>Returns a relative name; path reads may use a leading slash and an empty root.</summary>
    public static string Normalize(string name, bool isPath = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        var relative = isPath ? name.Trim('/') : name;
        if (isPath && (name.Length == 0 || name == "/"))
        {
            return string.Empty;
        }
        if (string.IsNullOrWhiteSpace(relative) || (!isPath && name.StartsWith('/'))
            || name.Contains('\\', StringComparison.Ordinal) || relative.Split('/').Any(s => s.Length == 0 || s is "." or "..")
            || relative.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')))
        {
            throw new ArgumentException("Use a relative resource name with non-empty path segments containing letters, digits, dots, underscores or hyphens.", nameof(name));
        }
        return relative;
    }
}
