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

using FluentAssertions;
using Pervaxis.Genesis.Workflow.AWS.Options;

namespace Pervaxis.Genesis.Workflow.AWS.Tests.Options;

public class WorkflowOptionsTests
{
    [Fact]
    public void Validate_WithValidOptions_ReturnsTrue()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1",
            MaxRetries = 3,
            RequestTimeoutSeconds = 30
        };
        options.StateMachineArns.Add("TestWorkflow", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMissingRegion_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = ""
        };
        options.StateMachineArns.Add("TestWorkflow", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithEmptyStateMachineArns_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1"
        };

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithEmptyWorkflowName_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1"
        };
        options.StateMachineArns.Add("", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithEmptyStateMachineArn_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1"
        };
        options.StateMachineArns.Add("TestWorkflow", "");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithInvalidArnFormat_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1"
        };
        options.StateMachineArns.Add("TestWorkflow", "invalid-arn");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithWrongArnPrefix_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1"
        };
        options.StateMachineArns.Add("TestWorkflow", "arn:aws:lambda:us-east-1:123456789012:function:TestFunction");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithNegativeMaxRetries_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1",
            MaxRetries = -1
        };
        options.StateMachineArns.Add("TestWorkflow", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithZeroOrNegativeTimeout_ReturnsFalse()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1",
            RequestTimeoutSeconds = 0
        };
        options.StateMachineArns.Add("TestWorkflow", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithMultipleStateMachines_ReturnsTrue()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1"
        };
        options.StateMachineArns.Add("Workflow1", "arn:aws:states:us-east-1:123456789012:stateMachine:Workflow1");
        options.StateMachineArns.Add("Workflow2", "arn:aws:states:us-east-1:123456789012:stateMachine:Workflow2");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var options = new WorkflowOptions();

        // Assert
        options.MaxRetries.Should().Be(3);
        options.RequestTimeoutSeconds.Should().Be(30);
        options.StateMachineArns.Should().BeEmpty();
    }

    [Fact]
    public void ExecutionNamePrefix_CanBeSet()
    {
        // Arrange
        var options = new WorkflowOptions
        {
            Region = "us-east-1",
            ExecutionNamePrefix = "pervaxis"
        };
        options.StateMachineArns.Add("TestWorkflow", "arn:aws:states:us-east-1:123456789012:stateMachine:TestWorkflow");

        // Act
        var result = options.Validate();

        // Assert
        result.Should().BeTrue();
        options.ExecutionNamePrefix.Should().Be("pervaxis");
    }
}
