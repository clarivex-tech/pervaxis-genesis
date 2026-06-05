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
using Microsoft.Extensions.Logging;

namespace Pervaxis.Genesis.FeatureFlags.AWS.Observability;

/// <summary>
/// Tracks the last-known evaluated state per flag per tenant (or per process when no tenant).
/// Emits structured Information-level logs only when state transitions occur.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public sealed class FeatureFlagStateTracker
{
    private readonly ConcurrentDictionary<string, bool> _lastKnownStates = new(StringComparer.Ordinal);

    /// <summary>
    /// Records an evaluation result and logs if a state transition occurred.
    /// </summary>
    /// <param name="flagName">The flag name.</param>
    /// <param name="tenantKey">Tenant-scoped key (or empty for process-level).</param>
    /// <param name="currentResult">The current evaluation result.</param>
    /// <param name="logger">Logger for state transition messages.</param>
    public void RecordEvaluation(
        string flagName, string tenantKey, bool currentResult, ILogger logger)
    {
        var compositeKey = string.IsNullOrEmpty(tenantKey)
            ? flagName
            : $"{flagName}:{tenantKey}";

        var isFirstEvaluation = true;
        var previousState = _lastKnownStates.AddOrUpdate(
            compositeKey,
            addValueFactory: _ =>
            {
                isFirstEvaluation = true;
                return currentResult;
            },
            updateValueFactory: (_, prev) =>
            {
                isFirstEvaluation = false;
                return currentResult;
            });

        // Only log on transition (not first evaluation)
        if (!isFirstEvaluation && previousState != currentResult)
        {
            logger.LogInformation(
                "Feature flag state changed: Flag={FlagName}, Previous={PreviousState}, " +
                "New={NewState}, Tenant={TenantKey}, Timestamp={Timestamp:O}",
                flagName,
                previousState ? "enabled" : "disabled",
                currentResult ? "enabled" : "disabled",
                tenantKey,
                DateTime.UtcNow);
        }
    }
}
