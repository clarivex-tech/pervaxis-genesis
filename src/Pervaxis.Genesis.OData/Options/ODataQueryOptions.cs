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

namespace Pervaxis.Genesis.OData.Options;

/// <summary>
/// Flags enum representing supported OData query options.
/// Used for enabling/disabling specific options globally or per-endpoint.
/// </summary>
[Flags]
public enum ODataQueryOptions
{
    /// <summary>No query options allowed.</summary>
    None = 0,

    /// <summary>$filter query option.</summary>
    Filter = 1,

    /// <summary>$orderby query option.</summary>
    OrderBy = 2,

    /// <summary>$select query option.</summary>
    Select = 4,

    /// <summary>$expand query option.</summary>
    Expand = 8,

    /// <summary>$top query option.</summary>
    Top = 16,

    /// <summary>$skip query option.</summary>
    Skip = 32,

    /// <summary>$count query option.</summary>
    Count = 64,

    /// <summary>All query options enabled.</summary>
    All = Filter | OrderBy | Select | Expand | Top | Skip | Count
}
