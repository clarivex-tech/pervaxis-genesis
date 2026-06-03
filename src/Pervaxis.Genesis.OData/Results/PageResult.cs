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

using System.Text.Json.Serialization;

namespace Pervaxis.Genesis.OData.Results;

/// <summary>
/// OData page result envelope containing query results with pagination metadata.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class PageResult<T> where T : class
{
    /// <summary>The array of result items.</summary>
    [JsonPropertyName("value")]
    public IReadOnlyList<T> Value { get; init; } = [];

    /// <summary>The total count of matching items (when $count=true). Null otherwise.</summary>
    [JsonPropertyName("@odata.count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; init; }

    /// <summary>The URL for the next page of results. Null when no more pages.</summary>
    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; init; }

    /// <summary>OData context metadata URL.</summary>
    [JsonPropertyName("@odata.context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; init; }
}
