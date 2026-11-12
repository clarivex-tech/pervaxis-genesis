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
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Metrics;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.FeatureFlags.AWS.Options;

namespace Pervaxis.Genesis.FeatureFlags.AWS.Observability;

/// <summary>
/// Decorates IFeatureManager to add Genesis observability (metrics, traces, state-change logging).
/// Registered as a singleton wrapping the underlying feature manager.
/// </summary>
internal sealed partial class FeatureFlagObservabilityInterceptor : IFeatureManager
{
    private readonly IFeatureManager _inner;
    private readonly FeatureFlagStateTracker _stateTracker;
    private readonly ITenantContext? _tenantContext;
    private readonly FeatureFlagOptions _options;
    private readonly ILogger<FeatureFlagObservabilityInterceptor> _logger;

    // Flag naming convention: {Domain}.{Feature} — alphanumeric segments separated by dot
    [GeneratedRegex(@"^[A-Za-z0-9]+\.[A-Za-z0-9]+$", RegexOptions.Compiled)]
    private static partial Regex NamingConventionRegex();

    private static readonly HashSet<string> WarnedNonConformingFlags = new(StringComparer.Ordinal);

    // Metrics — static readonly per Genesis pattern
    private static readonly Counter<long> EvaluationsCounter = PervaxisMeter.CreateCounter<long>(
        "genesis.featureflags.evaluations", "1",
        "Total number of feature flag evaluations");

    private static readonly Histogram<double> EvaluationDuration = PervaxisMeter.CreateHistogram<double>(
        "genesis.featureflags.evaluation.duration", "ms",
        "Duration of feature flag evaluations in milliseconds");

    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureFlagObservabilityInterceptor"/> class.
    /// </summary>
    public FeatureFlagObservabilityInterceptor(
        IFeatureManager inner,
        FeatureFlagStateTracker stateTracker,
        IOptions<FeatureFlagOptions> options,
        ILogger<FeatureFlagObservabilityInterceptor> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(stateTracker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _stateTracker = stateTracker;
        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(string feature)
    {
        WarnIfNonConforming(feature);

        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity(
            "featureflags.evaluate", ActivityKind.Internal);
        activity?.SetTag("featureflags.flag_name", feature);
        AddTenantTags(activity);

        try
        {
            var result = await _inner.IsEnabledAsync(feature);
            stopwatch.Stop();

            var resultTag = result ? "enabled" : "disabled";
            activity?.SetTag("featureflags.result", resultTag);
            RecordMetrics(feature, resultTag, stopwatch);
            _stateTracker.RecordEvaluation(feature, GetTenantKey(), result, _logger);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordMetrics(feature, "error", stopwatch);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync<TContext>(string feature, TContext context)
    {
        WarnIfNonConforming(feature);

        var stopwatch = Stopwatch.StartNew();
        using var activity = PervaxisActivitySource.StartActivity(
            "featureflags.evaluate", ActivityKind.Internal);
        activity?.SetTag("featureflags.flag_name", feature);
        AddTenantTags(activity);

        try
        {
            var result = await _inner.IsEnabledAsync(feature, context);
            stopwatch.Stop();

            var resultTag = result ? "enabled" : "disabled";
            activity?.SetTag("featureflags.result", resultTag);
            RecordMetrics(feature, resultTag, stopwatch);
            _stateTracker.RecordEvaluation(feature, GetTenantKey(), result, _logger);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            RecordMetrics(feature, "error", stopwatch);
            throw;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> GetFeatureNamesAsync()
        => _inner.GetFeatureNamesAsync();

    private void RecordMetrics(string flagName, string result, Stopwatch stopwatch)
    {
        var tags = new TagList
        {
            { "flag_name", flagName },
            { "result", result }
        };

        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
        }

        EvaluationsCounter.Add(1, tags);
        EvaluationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value.ToString());
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }

    private string GetTenantKey()
    {
        if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
        {
            return _tenantContext.TenantId.Value.ToString();
        }

        return string.Empty;
    }

    private void WarnIfNonConforming(string feature)
    {
        if (string.IsNullOrEmpty(feature) || feature.Length > 256)
        {
            return; // Don't throw — accept any non-empty name up to 256 chars at runtime
        }

        if (feature.Length <= 128 && !NamingConventionRegex().IsMatch(feature))
        {
            // Only warn once per flag name to avoid log spam
            if (WarnedNonConformingFlags.Add(feature))
            {
                _logger.LogWarning(
                    "Feature flag name '{FlagName}' does not conform to the Domain.Feature naming convention. " +
                    "Expected format: alphanumeric segments separated by a single dot (e.g., 'Billing.NewInvoiceFlow').",
                    feature);
            }
        }
    }
}
