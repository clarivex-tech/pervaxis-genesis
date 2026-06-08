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

using FluentValidation;
using Pervaxis.Genesis.Sanitization.Abstractions;

namespace Pervaxis.Genesis.Sanitization.Extensions;

/// <summary>
/// FluentValidation extensions for input sanitization.
/// Provides both transform (.Sanitized) and validate (.MustBeSanitized) modes.
/// </summary>
public static class SanitizationRuleBuilderExtensions
{
    /// <summary>
    /// Validates and sanitizes the property value using the default profile (StripAll).
    /// The sanitized value replaces the original on the instance via reflection.
    /// Subsequent rules operate on the sanitized value.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="sanitizer">The sanitizer instance.</param>
    /// <returns>The rule builder options for further chaining.</returns>
    public static IRuleBuilderOptions<T, string> Sanitized<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        ISanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        return ruleBuilder.Must((instance, value, context) =>
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var sanitized = sanitizer.StripAll(value) ?? string.Empty;
            SetPropertyValue(instance!, context.PropertyPath, sanitized);
            return true;
        });
    }

    /// <summary>
    /// Validates and sanitizes the property value using the named profile.
    /// The sanitized value replaces the original on the instance via reflection.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="sanitizer">The sanitizer instance.</param>
    /// <param name="profileName">The profile name to use.</param>
    /// <returns>The rule builder options for further chaining.</returns>
    public static IRuleBuilderOptions<T, string> Sanitized<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        ISanitizer sanitizer,
        string profileName)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        return ruleBuilder.Must((instance, value, context) =>
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var sanitized = sanitizer.Sanitize(value, profileName) ?? string.Empty;
            SetPropertyValue(instance!, context.PropertyPath, sanitized);
            return true;
        });
    }

    /// <summary>
    /// Validates and sanitizes the property value using the specified profile.
    /// The sanitized value replaces the original on the instance via reflection.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="sanitizer">The sanitizer instance.</param>
    /// <param name="profile">The sanitization profile to use.</param>
    /// <returns>The rule builder options for further chaining.</returns>
    public static IRuleBuilderOptions<T, string> Sanitized<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        ISanitizer sanitizer,
        SanitizationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentNullException.ThrowIfNull(profile);

        return ruleBuilder.Must((instance, value, context) =>
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var sanitized = sanitizer.Sanitize(value, profile) ?? string.Empty;
            SetPropertyValue(instance!, context.PropertyPath, sanitized);
            return true;
        });
    }

    /// <summary>
    /// Validates that the input does NOT contain content that would be stripped by the default profile.
    /// Fails validation (does not transform) if dangerous content is detected.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="sanitizer">The sanitizer instance.</param>
    /// <returns>The rule builder options for further chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeSanitized<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        ISanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        return ruleBuilder.Must(value =>
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var sanitized = sanitizer.StripAll(value);
            return string.Equals(value, sanitized, StringComparison.Ordinal);
        }).WithMessage("'{PropertyName}' contains disallowed content that would be removed by sanitization.");
    }

    /// <summary>
    /// Validates that the input does NOT contain content that would be stripped by the named profile.
    /// Fails validation (does not transform) if dangerous content is detected.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <param name="sanitizer">The sanitizer instance.</param>
    /// <param name="profileName">The profile name to validate against.</param>
    /// <returns>The rule builder options for further chaining.</returns>
    public static IRuleBuilderOptions<T, string> MustBeSanitized<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        ISanitizer sanitizer,
        string profileName)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        return ruleBuilder.Must(value =>
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            var sanitized = sanitizer.Sanitize(value, profileName);
            return string.Equals(value, sanitized, StringComparison.Ordinal);
        }).WithMessage("'{PropertyName}' contains disallowed content that would be removed by sanitization.");
    }

    private static void SetPropertyValue<T>(T instance, string propertyName, string value)
    {
        var property = typeof(T).GetProperty(propertyName);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(instance, value);
        }
    }
}
