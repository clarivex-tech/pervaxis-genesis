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

using System.Linq.Expressions;

namespace Pervaxis.Genesis.OData.Configuration;

/// <summary>
/// Fluent configuration for defining OData query permissions per entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type to configure.</typeparam>
public sealed class EntityODataConfiguration<TEntity> where TEntity : class
{
    private readonly EntityPropertyDescriptor _descriptor;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityODataConfiguration{TEntity}"/> class.
    /// </summary>
    public EntityODataConfiguration()
    {
        _descriptor = new EntityPropertyDescriptor { EntityType = typeof(TEntity) };
    }

    /// <summary>
    /// Marks a property as filterable (allowed in $filter expressions).
    /// </summary>
    public EntityODataConfiguration<TEntity> ConfigureFilter(
        Expression<Func<TEntity, object>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        _descriptor.FilterableProperties.Add(GetPropertyName(propertyExpression));
        return this;
    }

    /// <summary>
    /// Marks a property as sortable (allowed in $orderby expressions).
    /// </summary>
    public EntityODataConfiguration<TEntity> ConfigureSort(
        Expression<Func<TEntity, object>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        _descriptor.SortableProperties.Add(GetPropertyName(propertyExpression));
        return this;
    }

    /// <summary>
    /// Marks a property as selectable (allowed in $select expressions).
    /// </summary>
    public EntityODataConfiguration<TEntity> ConfigureSelect(
        Expression<Func<TEntity, object>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        _descriptor.SelectableProperties.Add(GetPropertyName(propertyExpression));
        return this;
    }

    /// <summary>
    /// Marks a navigation property as expandable (allowed in $expand expressions).
    /// </summary>
    public EntityODataConfiguration<TEntity> ConfigureExpand(
        Expression<Func<TEntity, object>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        _descriptor.ExpandableProperties.Add(GetPropertyName(propertyExpression));
        return this;
    }

    /// <summary>
    /// Excludes a property from all OData query operations.
    /// </summary>
    public EntityODataConfiguration<TEntity> ExcludeProperty(
        Expression<Func<TEntity, object>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        var name = GetPropertyName(propertyExpression);
        _descriptor.ExcludedProperties.Add(name);
        _descriptor.FilterableProperties.Remove(name);
        _descriptor.SortableProperties.Remove(name);
        _descriptor.SelectableProperties.Remove(name);
        _descriptor.ExpandableProperties.Remove(name);
        return this;
    }

    /// <summary>
    /// Configures the tenant property for this entity type.
    /// </summary>
    public EntityODataConfiguration<TEntity> ConfigureTenantProperty(
        Expression<Func<TEntity, string>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        _descriptor.TenantPropertyName = GetPropertyName(propertyExpression);
        return this;
    }

    /// <summary>
    /// Builds the entity property descriptor from the current configuration.
    /// </summary>
    internal EntityPropertyDescriptor Build() => _descriptor;

    private static string GetPropertyName<TProperty>(Expression<Func<TEntity, TProperty>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        if (expression.Body is UnaryExpression { Operand: MemberExpression unaryMember })
        {
            return unaryMember.Member.Name;
        }

        throw new ArgumentException("Expression must be a property access expression.", nameof(expression));
    }
}
