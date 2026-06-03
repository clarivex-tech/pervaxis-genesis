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

using System.Collections.Concurrent;

namespace Pervaxis.Genesis.OData.Configuration;

/// <summary>
/// Registry for per-entity OData configurations. Returns default configuration
/// for entity types without explicit registration.
/// </summary>
public sealed class EntityConfigurationRegistry
{
    private readonly ConcurrentDictionary<Type, EntityPropertyDescriptor> _configurations = new();

    /// <summary>
    /// Registers a configuration for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="configure">The configuration action.</param>
    public void Register<TEntity>(Action<EntityODataConfiguration<TEntity>> configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(configure);

        var config = new EntityODataConfiguration<TEntity>();
        configure(config);
        _configurations[typeof(TEntity)] = config.Build();
    }

    /// <summary>
    /// Gets the configuration for the specified entity type.
    /// Returns default configuration if none is explicitly registered.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>The entity property descriptor.</returns>
    public EntityPropertyDescriptor GetConfiguration<TEntity>() where TEntity : class
    {
        return GetConfiguration(typeof(TEntity));
    }

    /// <summary>
    /// Gets the configuration for the specified entity type.
    /// Returns default configuration if none is explicitly registered.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The entity property descriptor.</returns>
    public EntityPropertyDescriptor GetConfiguration(Type entityType)
    {
        return _configurations.GetOrAdd(entityType, EntityPropertyDescriptor.CreateDefault);
    }
}
