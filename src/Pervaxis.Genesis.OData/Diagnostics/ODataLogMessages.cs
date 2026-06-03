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

namespace Pervaxis.Genesis.OData.Diagnostics;

/// <summary>
/// Source-generated log messages for the Genesis OData module.
/// </summary>
internal static partial class ODataLogMessages
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "OData query processed successfully: Endpoint={Endpoint}, ResultCount={ResultCount}, DurationMs={DurationMs}, ComplexityScore={ComplexityScore}.")]
    internal static partial void LogQueryProcessed(
        ILogger logger, string endpoint, int resultCount, double durationMs, int complexityScore);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "OData query validation failed: ErrorCode={ErrorCode}, Endpoint={Endpoint}, Query={QueryString}.")]
    internal static partial void LogValidationFailed(
        ILogger logger, string errorCode, string endpoint, string queryString);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "OData query parse error: Endpoint={Endpoint}, Query={QueryString}, Error={ErrorDescription}.")]
    internal static partial void LogParseError(
        ILogger logger, string endpoint, string queryString, string errorDescription);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "OData query not translatable: Endpoint={Endpoint}, Error={ErrorDescription}.")]
    internal static partial void LogNotTranslatable(
        ILogger logger, string endpoint, string errorDescription);
}
