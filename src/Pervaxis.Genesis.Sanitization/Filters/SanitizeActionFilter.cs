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

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.Sanitization.Options;
using Pervaxis.Genesis.Sanitization.Services;

namespace Pervaxis.Genesis.Sanitization.Filters;

/// <summary>
/// ASP.NET Core action filter that processes <see cref="SanitizeAttribute"/> on DTO properties.
/// Runs before validation to ensure validators operate on sanitized data.
/// </summary>
internal sealed class SanitizeActionFilter : IAsyncActionFilter
{
    private readonly GenesisSanitizer _sanitizer;
    private readonly string _defaultProfile;

    /// <summary>
    /// Initializes a new instance of the <see cref="SanitizeActionFilter"/> class.
    /// </summary>
    /// <param name="sanitizer">The sanitizer instance.</param>
    /// <param name="options">Sanitization options for default profile resolution.</param>
    internal SanitizeActionFilter(GenesisSanitizer sanitizer, IOptions<SanitizationOptions> options)
    {
        _sanitizer = sanitizer;
        _defaultProfile = options.Value.DefaultProfile;
    }

    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            SanitizeProperties(argument);
        }

        await next().ConfigureAwait(false);
    }

    private void SanitizeProperties(object target)
    {
        var type = target.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.PropertyType != typeof(string) || !property.CanRead || !property.CanWrite)
            {
                continue;
            }

            var sanitizeAttr = property.GetCustomAttribute<SanitizeAttribute>();
            if (sanitizeAttr is null)
            {
                continue;
            }

            var currentValue = (string?)property.GetValue(target);
            if (currentValue is null)
            {
                continue;
            }

            var profileName = sanitizeAttr.Profile ?? _defaultProfile;
            var sanitizedValue = _sanitizer.SanitizeCore(currentValue, profileName, "attribute");
            property.SetValue(target, sanitizedValue);
        }
    }
}
