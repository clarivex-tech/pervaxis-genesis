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
using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Pervaxis.Core.Abstractions.Genesis.Modules;
using Pervaxis.Core.Abstractions.MultiTenancy;
using Pervaxis.Core.Observability.Tracing;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Base.Resilience;
using Pervaxis.Genesis.Workflow.AWS.Options;

namespace Pervaxis.Genesis.Workflow.AWS.Providers;

/// <summary>
/// AWS Step Functions implementation of the IWorkflow interface.
/// </summary>
public sealed class StepFunctionsWorkflowProvider : IWorkflow, IDisposable
{
    private readonly ILogger<StepFunctionsWorkflowProvider> _logger;
    private readonly WorkflowOptions _options;
    private readonly ITenantContext? _tenantContext;
    private readonly Lazy<IAmazonStepFunctions> _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ResiliencePipeline _resiliencePipeline;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepFunctionsWorkflowProvider"/> class.
    /// </summary>
    /// <param name="options">The workflow options.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="tenantContext">Optional tenant context for multi-tenancy support.</param>
    /// <exception cref="ArgumentNullException">Thrown when options or logger is null.</exception>
    /// <exception cref="GenesisConfigurationException">Thrown when options validation fails.</exception>
    public StepFunctionsWorkflowProvider(
        IOptions<WorkflowOptions> options,
        ILogger<StepFunctionsWorkflowProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _tenantContext = tenantContext;

        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(nameof(WorkflowOptions), "Invalid workflow options configuration");
        }

        _client = new Lazy<IAmazonStepFunctions>(CreateClient);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "StepFunctionsWorkflow");

        _logger.LogInformation(
            "StepFunctionsWorkflowProvider initialized for region {Region} with {Count} state machines, tenant isolation: {TenantIsolation}, resilience: {Resilience}",
            _options.Region, _options.StateMachineArns.Count,
            _options.EnableTenantIsolation && _tenantContext?.IsResolved == true,
            _options.Resilience.Enabled);
    }

    /// <summary>
    /// Internal constructor for testing with injected client.
    /// </summary>
    internal StepFunctionsWorkflowProvider(
        WorkflowOptions options,
        IAmazonStepFunctions client,
        ILogger<StepFunctionsWorkflowProvider> logger,
        ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _tenantContext = tenantContext;
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _client = new Lazy<IAmazonStepFunctions>(() => client);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _resiliencePipeline = GenesisResiliencePipelineBuilder.BuildPipeline(
            _options.Resilience,
            _logger,
            "StepFunctionsWorkflow");
    }

    /// <inheritdoc />
    public async Task<string> StartExecutionAsync(
        string workflowName,
        object input,
        CancellationToken cancellationToken = default)
    {
        using var activity = PervaxisActivitySource.StartActivity("workflow.start", ActivityKind.Client);
        activity?.SetTag("workflow.system", "stepfunctions");
        activity?.SetTag("workflow.operation", "start");
        activity?.SetTag("workflow.name", workflowName);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName, nameof(workflowName));
        ArgumentNullException.ThrowIfNull(input);

        if (!_options.StateMachineArns.TryGetValue(workflowName, out var stateMachineArn))
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"State machine ARN not found for workflow: {workflowName}");
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"State machine ARN not found for workflow: {workflowName}");
        }

        try
        {
            var inputJson = JsonSerializer.Serialize(input, _jsonOptions);
            var executionName = GenerateExecutionName(workflowName);

            var request = new StartExecutionRequest
            {
                StateMachineArn = stateMachineArn,
                Name = executionName,
                Input = inputJson
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.StartExecutionAsync(request, ct),
                cancellationToken);

            activity?.SetTag("workflow.execution_id", response.ExecutionArn);
            _logger.LogInformation(
                "Started workflow execution for {WorkflowName} with ExecutionArn {ExecutionArn}",
                workflowName, response.ExecutionArn);

            return response.ExecutionArn;
        }
        catch (Exception ex) when (ex is not GenesisException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to start workflow execution for {WorkflowName}", workflowName);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Failed to start workflow: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetExecutionStatusAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        using var activity = PervaxisActivitySource.StartActivity("workflow.get_status", ActivityKind.Client);
        activity?.SetTag("workflow.system", "stepfunctions");
        activity?.SetTag("workflow.operation", "get_status");
        activity?.SetTag("workflow.execution_id", executionId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(executionId, nameof(executionId));

        try
        {
            var request = new DescribeExecutionRequest
            {
                ExecutionArn = executionId
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.DescribeExecutionAsync(request, ct),
                cancellationToken);

            activity?.SetTag("workflow.status", response.Status.Value);
            _logger.LogDebug(
                "Retrieved status {Status} for execution {ExecutionArn}",
                response.Status, executionId);

            return response.Status.Value;
        }
        catch (ExecutionDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Execution not found");
            _logger.LogWarning("Execution not found: {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Execution not found: {executionId}",
                ex);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get execution status for {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Failed to get execution status: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetExecutionOutputAsync<T>(
        string executionId,
        CancellationToken cancellationToken = default) where T : class
    {
        using var activity = PervaxisActivitySource.StartActivity("workflow.get_output", ActivityKind.Client);
        activity?.SetTag("workflow.system", "stepfunctions");
        activity?.SetTag("workflow.operation", "get_output");
        activity?.SetTag("workflow.execution_id", executionId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(executionId, nameof(executionId));

        try
        {
            var request = new DescribeExecutionRequest
            {
                ExecutionArn = executionId
            };

            var response = await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.DescribeExecutionAsync(request, ct),
                cancellationToken);

            activity?.SetTag("workflow.status", response.Status.Value);

            // Only return output if execution succeeded
            if (response.Status != ExecutionStatus.SUCCEEDED)
            {
                _logger.LogDebug(
                    "Execution {ExecutionArn} is not in SUCCEEDED state (current: {Status}), returning null",
                    executionId, response.Status);
                return null;
            }

            if (string.IsNullOrWhiteSpace(response.Output))
            {
                _logger.LogWarning("Execution {ExecutionArn} succeeded but has no output", executionId);
                return null;
            }

            var output = JsonSerializer.Deserialize<T>(response.Output, _jsonOptions);

            _logger.LogInformation(
                "Retrieved output for execution {ExecutionArn}",
                executionId);

            return output;
        }
        catch (ExecutionDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Execution not found");
            _logger.LogWarning("Execution not found: {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Execution not found: {executionId}",
                ex);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Failed to deserialize execution output");
            _logger.LogError(ex, "Failed to deserialize execution output for {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Failed to deserialize execution output: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to get execution output for {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Failed to get execution output: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> StopExecutionAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        using var activity = PervaxisActivitySource.StartActivity("workflow.stop", ActivityKind.Client);
        activity?.SetTag("workflow.system", "stepfunctions");
        activity?.SetTag("workflow.operation", "stop");
        activity?.SetTag("workflow.execution_id", executionId);
        AddTenantTags(activity);

        ArgumentException.ThrowIfNullOrWhiteSpace(executionId, nameof(executionId));

        try
        {
            var request = new StopExecutionRequest
            {
                ExecutionArn = executionId,
                Error = "ManualStop",
                Cause = "Execution stopped manually via IWorkflow.StopExecutionAsync"
            };

            await _resiliencePipeline.ExecuteAsync(
                async ct => await _client.Value.StopExecutionAsync(request, ct),
                cancellationToken);

            activity?.SetTag("workflow.success", true);
            _logger.LogInformation(
                "Stopped execution {ExecutionArn}",
                executionId);

            return true;
        }
        catch (ExecutionDoesNotExistException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Execution not found");
            _logger.LogWarning("Execution not found: {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Execution not found: {executionId}",
                ex);
        }
        catch (InvalidArnException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid execution ARN");
            _logger.LogError(ex, "Invalid execution ARN: {ExecutionId}", executionId);
            return false;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Failed to stop execution {ExecutionId}", executionId);
            throw new GenesisException(
                nameof(StepFunctionsWorkflowProvider),
                $"Failed to stop execution: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Generates a unique execution name for a workflow.
    /// </summary>
    private string GenerateExecutionName(string workflowName)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var guid = Guid.NewGuid().ToString("N")[..8];

        var prefix = !string.IsNullOrWhiteSpace(_options.ExecutionNamePrefix)
            ? _options.ExecutionNamePrefix
            : workflowName;

        return $"{prefix}-{timestamp}-{guid}";
    }

    /// <summary>
    /// Creates the Step Functions client with appropriate configuration.
    /// </summary>
    private IAmazonStepFunctions CreateClient()
    {
        var config = new AmazonStepFunctionsConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region),
            MaxErrorRetry = _options.MaxRetries,
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)
        };

        if (_options.UseLocalEmulator && _options.LocalEmulatorUrl is not null)
        {
            config.ServiceURL = _options.LocalEmulatorUrl.AbsoluteUri;
            config.AuthenticationRegion = _options.Region;
        }

        return new AmazonStepFunctionsClient(config);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }

        _disposed = true;
        _logger.LogInformation("StepFunctionsWorkflowProvider disposed");
    }

    private void AddTenantTags(Activity? activity)
    {
        if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
        {
            return;
        }

        activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
        activity.SetTag("tenant.name", _tenantContext.TenantName);
    }
}
