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

using Pervaxis.Core.Abstractions.Genesis;

namespace Pervaxis.Genesis.Workflow.AWS.Options;

/// <summary>
/// Configuration options for AWS Step Functions workflow provider.
/// </summary>
public sealed class WorkflowOptions : GenesisOptionsBase
{
    /// <summary>
    /// Dictionary mapping workflow names to their state machine ARNs.
    /// Key: Logical workflow name, Value: State machine ARN.
    /// Example: { "OrderProcessing": "arn:aws:states:us-east-1:123456789012:stateMachine:OrderWorkflow" }
    /// </summary>
    public Dictionary<string, string> StateMachineArns { get; } = new();

    /// <summary>
    /// Maximum number of retries for transient failures.
    /// Default: 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Request timeout in seconds.
    /// Default: 30.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Default execution name prefix.
    /// If not specified, a GUID will be used.
    /// </summary>
    public string? ExecutionNamePrefix { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable tenant isolation.
    /// When enabled, workflow executions include tenant metadata.
    /// Default is true.
    /// </summary>
    public bool EnableTenantIsolation { get; set; } = true;

    /// <summary>
    /// Validates the workflow options.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }

        if (StateMachineArns.Count == 0)
        {
            return false;
        }

        foreach (var kvp in StateMachineArns)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
            {
                return false;
            }

            // Validate ARN format
            if (!kvp.Value.StartsWith("arn:aws:states:", StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (MaxRetries < 0)
        {
            return false;
        }

        if (RequestTimeoutSeconds <= 0)
        {
            return false;
        }

        return true;
    }
}
