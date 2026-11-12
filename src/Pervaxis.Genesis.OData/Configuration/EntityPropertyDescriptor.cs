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

namespace Pervaxis.Genesis.OData.Configuration;

/// <summary>
/// Describes the OData query permissions for a specific entity type.
/// </summary>
public sealed class EntityPropertyDescriptor
{
    /// <summary>The entity type this descriptor applies to.</summary>
    public Type EntityType { get; init; } = typeof(object);

    /// <summary>Properties allowed in $filter expressions.</summary>
    public ISet<string> FilterableProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties allowed in $orderby expressions.</summary>
    public ISet<string> SortableProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties allowed in $select expressions.</summary>
    public ISet<string> SelectableProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Navigation properties allowed in $expand expressions.</summary>
    public ISet<string> ExpandableProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Properties excluded from all OData operations.</summary>
    public ISet<string> ExcludedProperties { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tenant property name (null = use convention "TenantId").</summary>
    public string? TenantPropertyName { get; set; }

    /// <summary>
    /// Gets the effective tenant property name (configured or convention-based).
    /// </summary>
    public string EffectiveTenantProperty => TenantPropertyName ?? "TenantId";

    /// <summary>
    /// Creates a default descriptor for an entity type where all primitive properties
    /// are filterable, sortable, and selectable, and no navigation properties are expandable.
    /// </summary>
    public static EntityPropertyDescriptor CreateDefault(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var descriptor = new EntityPropertyDescriptor { EntityType = entityType };

        foreach (var property in entityType.GetProperties())
        {
            if (property.PropertyType.IsPrimitive
                || property.PropertyType == typeof(string)
                || property.PropertyType == typeof(decimal)
                || property.PropertyType == typeof(DateTime)
                || property.PropertyType == typeof(DateTimeOffset)
                || property.PropertyType == typeof(Guid)
                || Nullable.GetUnderlyingType(property.PropertyType) is not null)
            {
                descriptor.FilterableProperties.Add(property.Name);
                descriptor.SortableProperties.Add(property.Name);
                descriptor.SelectableProperties.Add(property.Name);
            }
        }

        return descriptor;
    }
}
