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

using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pervaxis.Genesis.Base.Exceptions;
using Pervaxis.Genesis.Workflow.AWS.Options;
using Pervaxis.Genesis.Workflow.AWS.Providers;

namespace Pervaxis.Genesis.Workflow.AWS.Tests.Providers;

public class StepFunctionsWorkflowProviderTests
{
    private readonly Mock<IAmazonStepFunctions> _mockClient;
    private readonly Mock<ILogger<StepFunctionsWorkflowProvider>> _mockLogger;
    private readonly WorkflowOptions _validOptions;

    public StepFunctionsWorkflowProviderTests()
    {
        _mockClient = new Mock<IAmazonStepFunctions>();
        _mockLogger = new Mock<ILogger<StepFunctionsWorkflowProvider>>();

        _validOptions = new WorkflowOptions
        {
            Region = "us-east-1",
            MaxRetries = 3,
            RequestTimeoutSeconds = 30
        };
        _validOptions.StateMachineArns.Add("TestWorkflow", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new StepFunctionsWorkflowProvider(
            null!,
            _mockClient.Object,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_WithNullClient_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new StepFunctionsWorkflowProvider(
            _validOptions,
            null!,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("client");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithIOptions_AndInvalidOptions_ShouldThrowGenesisConfigurationException()
    {
        // Arrange
        var invalidOptions = new WorkflowOptions
        {
            Region = "us-east-1"
            // Missing StateMachineArns
        };

        var mockOptions = new Mock<IOptions<WorkflowOptions>>();
        mockOptions.Setup(o => o.Value).Returns(invalidOptions);

        // Act
        var act = () => new StepFunctionsWorkflowProvider(
            mockOptions.Object,
            _mockLogger.Object);

        // Assert
        act.Should().Throw<GenesisConfigurationException>();
    }

    #endregion

    #region StartExecutionAsync Tests

    [Fact]
    public async Task StartExecutionAsync_WithValidParameters_ShouldReturnExecutionArn()
    {
        // Arrange
        var expectedArn = "arn:aws:states:us-east-1:123456789012:execution:TestWorkflow:exec-123";
        _mockClient.Setup(x => x.StartExecutionAsync(
                It.IsAny<StartExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartExecutionResponse { ExecutionArn = expectedArn });

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        var input = new { orderId = "12345", customerId = "C001" };

        // Act
        var result = await provider.StartExecutionAsync("TestWorkflow", input);

        // Assert
        result.Should().Be(expectedArn);
        _mockClient.Verify(x => x.StartExecutionAsync(
            It.Is<StartExecutionRequest>(r =>
                r.StateMachineArn == _validOptions.StateMachineArns["TestWorkflow"] &&
                r.Input.Contains("orderId") &&
                r.Input.Contains("12345")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartExecutionAsync_WithNullWorkflowName_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.StartExecutionAsync(null!, new { });

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("workflowName");
    }

    [Fact]
    public async Task StartExecutionAsync_WithNullInput_ShouldThrowArgumentNullException()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.StartExecutionAsync("TestWorkflow", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("input");
    }

    [Fact]
    public async Task StartExecutionAsync_WithUnknownWorkflow_ShouldThrowGenesisException()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.StartExecutionAsync("UnknownWorkflow", new { });

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*State machine ARN not found*");
    }

    [Fact]
    public async Task StartExecutionAsync_WhenClientThrowsException_ShouldThrowGenesisException()
    {
        // Arrange
        _mockClient.Setup(x => x.StartExecutionAsync(
                It.IsAny<StartExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonStepFunctionsException("AWS error"));

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.StartExecutionAsync("TestWorkflow", new { });

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Failed to start workflow*");
    }

    #endregion

    #region GetExecutionStatusAsync Tests

    [Fact]
    public async Task GetExecutionStatusAsync_WithValidArn_ShouldReturnStatus()
    {
        // Arrange
        var executionArn = "arn:aws:states:us-east-1:123456789012:execution:TestWorkflow:exec-123";
        _mockClient.Setup(x => x.DescribeExecutionAsync(
                It.IsAny<DescribeExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeExecutionResponse { Status = ExecutionStatus.RUNNING });

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.GetExecutionStatusAsync(executionArn);

        // Assert
        result.Should().Be("RUNNING");
    }

    [Fact]
    public async Task GetExecutionStatusAsync_WithNullArn_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.GetExecutionStatusAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("executionId");
    }

    [Fact]
    public async Task GetExecutionStatusAsync_WithNonExistentExecution_ShouldThrowGenesisException()
    {
        // Arrange
        _mockClient.Setup(x => x.DescribeExecutionAsync(
                It.IsAny<DescribeExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExecutionDoesNotExistException("Execution not found"));

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.GetExecutionStatusAsync("invalid-arn");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Execution not found*");
    }

    #endregion

    #region GetExecutionOutputAsync Tests

    public record TestOutput(string OrderId, string Status);

    [Fact]
    public async Task GetExecutionOutputAsync_WithSucceededExecution_ShouldReturnOutput()
    {
        // Arrange
        var executionArn = "arn:aws:states:us-east-1:123456789012:execution:TestWorkflow:exec-123";
        var outputJson = "{\"orderId\":\"12345\",\"status\":\"Completed\"}";

        _mockClient.Setup(x => x.DescribeExecutionAsync(
                It.IsAny<DescribeExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeExecutionResponse
            {
                Status = ExecutionStatus.SUCCEEDED,
                Output = outputJson
            });

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.GetExecutionOutputAsync<TestOutput>(executionArn);

        // Assert
        result.Should().NotBeNull();
        result!.OrderId.Should().Be("12345");
        result.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task GetExecutionOutputAsync_WithRunningExecution_ShouldReturnNull()
    {
        // Arrange
        var executionArn = "arn:aws:states:us-east-1:123456789012:execution:TestWorkflow:exec-123";

        _mockClient.Setup(x => x.DescribeExecutionAsync(
                It.IsAny<DescribeExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeExecutionResponse
            {
                Status = ExecutionStatus.RUNNING
            });

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.GetExecutionOutputAsync<TestOutput>(executionArn);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetExecutionOutputAsync_WithNullOutput_ShouldReturnNull()
    {
        // Arrange
        var executionArn = "arn:aws:states:us-east-1:123456789012:execution:TestWorkflow:exec-123";

        _mockClient.Setup(x => x.DescribeExecutionAsync(
                It.IsAny<DescribeExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeExecutionResponse
            {
                Status = ExecutionStatus.SUCCEEDED,
                Output = null
            });

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.GetExecutionOutputAsync<TestOutput>(executionArn);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetExecutionOutputAsync_WithNullArn_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.GetExecutionOutputAsync<TestOutput>(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("executionId");
    }

    #endregion

    #region StopExecutionAsync Tests

    [Fact]
    public async Task StopExecutionAsync_WithValidArn_ShouldReturnTrue()
    {
        // Arrange
        var executionArn = "arn:aws:states:us-east-1:123456789012:execution:TestWorkflow:exec-123";
        _mockClient.Setup(x => x.StopExecutionAsync(
                It.IsAny<StopExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StopExecutionResponse());

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.StopExecutionAsync(executionArn);

        // Assert
        result.Should().BeTrue();
        _mockClient.Verify(x => x.StopExecutionAsync(
            It.Is<StopExecutionRequest>(r =>
                r.ExecutionArn == executionArn &&
                r.Error == "ManualStop"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopExecutionAsync_WithNullArn_ShouldThrowArgumentException()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.StopExecutionAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("executionId");
    }

    [Fact]
    public async Task StopExecutionAsync_WithNonExistentExecution_ShouldThrowGenesisException()
    {
        // Arrange
        _mockClient.Setup(x => x.StopExecutionAsync(
                It.IsAny<StopExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExecutionDoesNotExistException("Execution not found"));

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var act = async () => await provider.StopExecutionAsync("invalid-arn");

        // Assert
        await act.Should().ThrowAsync<GenesisException>()
            .WithMessage("*Execution not found*");
    }

    [Fact]
    public async Task StopExecutionAsync_WithInvalidArn_ShouldReturnFalse()
    {
        // Arrange
        _mockClient.Setup(x => x.StopExecutionAsync(
                It.IsAny<StopExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidArnException("Invalid ARN"));

        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        var result = await provider.StopExecutionAsync("invalid-arn");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldDisposeClient()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        provider.Dispose();

        // Assert - Just verify it doesn't throw
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var provider = new StepFunctionsWorkflowProvider(
            _validOptions,
            _mockClient.Object,
            _mockLogger.Object);

        // Act
        provider.Dispose();
        var act = () => provider.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
