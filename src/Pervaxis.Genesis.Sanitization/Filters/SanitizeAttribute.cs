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

namespace Pervaxis.Genesis.Sanitization.Filters;

/// <summary>
/// Declaratively sanitizes a string property at model binding time.
/// Processed by <see cref="SanitizeActionFilter"/> before validation runs.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SanitizeAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the profile name to use for sanitization.
    /// Null means use the default profile from <c>SanitizationOptions.DefaultProfile</c>.
    /// </summary>
    public string? Profile { get; set; }
}
