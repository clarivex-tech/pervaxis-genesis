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

using Microsoft.Extensions.Logging;

namespace Pervaxis.Genesis.Sanitization.Diagnostics;

/// <summary>
/// Source-generated log messages for the Genesis Sanitization module.
/// Uses LoggerMessage source generation for zero-allocation logging.
/// </summary>
internal static partial class SanitizationLogMessages
{
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Sanitization threat detected: profile={Profile}, source={Source}, originalLength={OriginalLength}, sanitizedLength={SanitizedLength}.")]
    internal static partial void LogThreatDetected(
        ILogger logger, string profile, string source, int originalLength, int sanitizedLength);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Debug,
        Message = "Sanitization completed (clean input): profile={Profile}, source={Source}.")]
    internal static partial void LogCleanInput(
        ILogger logger, string profile, string source);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Information,
        Message = "Custom sanitization profile loaded: name={ProfileName}, allowedTags={TagCount}, allowedAttributes={AttributeCount}.")]
    internal static partial void LogCustomProfileLoaded(
        ILogger logger, string profileName, int tagCount, int attributeCount);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Information,
        Message = "Sanitization middleware processed request: route={Route}, method={HttpMethod}, fieldsModified={FieldsModified}.")]
    internal static partial void LogMiddlewareSanitized(
        ILogger logger, string route, string httpMethod, int fieldsModified);
}
