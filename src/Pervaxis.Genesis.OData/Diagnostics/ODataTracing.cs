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
using Pervaxis.Core.Observability.Tracing;

namespace Pervaxis.Genesis.OData.Diagnostics;

/// <summary>
/// Distributed tracing instrumentation for the Genesis OData module.
/// </summary>
internal static class ODataTracing
{
    /// <summary>
    /// Starts a trace activity for OData query processing.
    /// </summary>
    internal static Activity? StartQueryActivity()
    {
        return PervaxisActivitySource.StartActivity("odata.query", ActivityKind.Internal);
    }

    /// <summary>
    /// Starts a trace activity for OData query validation.
    /// </summary>
    internal static Activity? StartValidationActivity()
    {
        return PervaxisActivitySource.StartActivity("odata.validate", ActivityKind.Internal);
    }
}
