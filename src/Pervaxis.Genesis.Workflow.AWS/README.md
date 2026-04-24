# Pervaxis.Genesis.Workflow.AWS

AWS Step Functions implementation for the Pervaxis Genesis platform. Orchestrate complex workflows and distributed processes.

## Features

- ✅ **Start Executions** - Launch workflow executions with typed input
- ✅ **Get Status** - Check execution status (RUNNING, SUCCEEDED, FAILED, etc.)
- ✅ **Get Output** - Retrieve typed output from completed executions
- ✅ **Stop Executions** - Cancel running executions
- ✅ **State Machine Mapping** - Map logical workflow names to ARNs
- ✅ **LocalStack Support** - Test locally without AWS
- ✅ **Auto-Generated Names** - Unique execution names with timestamps
- ✅ **Structured Logging** - Full logging with Microsoft.Extensions.Logging
- ✅ **Async/Await** - Modern async patterns with CancellationToken support

## Installation

```bash
dotnet add package Pervaxis.Genesis.Workflow.AWS
```

## Configuration

### appsettings.json

```json
{
  "Workflow": {
    "Region": "us-east-1",
    "StateMachineArns": {
      "OrderProcessing": "arn:aws:states:us-east-1:123456789012:stateMachine:OrderWorkflow",
      "PaymentProcessing": "arn:aws:states:us-east-1:123456789012:stateMachine:PaymentWorkflow",
      "InventorySync": "arn:aws:states:us-east-1:123456789012:stateMachine:InventoryWorkflow"
    },
    "ExecutionNamePrefix": "pervaxis",
    "MaxRetries": 3,
    "RequestTimeoutSeconds": 30,
    "UseLocalEmulator": false,
    "LocalEmulatorUrl": "http://localhost:4566"
  }
}
```

### Configuration Options

| Option | Type | Required | Default | Description |
|--------|------|----------|---------|-------------|
| `Region` | string | Yes | - | AWS region (e.g., "us-east-1") |
| `StateMachineArns` | Dictionary | Yes | - | Map of workflow names to state machine ARNs |
| `ExecutionNamePrefix` | string | No | workflow name | Prefix for execution names |
| `MaxRetries` | int | No | 3 | Maximum retry attempts |
| `RequestTimeoutSeconds` | int | No | 30 | Request timeout in seconds |
| `UseLocalEmulator` | bool | No | false | Use LocalStack for local testing |
| `LocalEmulatorUrl` | string | No | - | LocalStack endpoint URL |

## Usage

### 1. Register in Dependency Injection

#### Using Configuration

```csharp
using Pervaxis.Genesis.Workflow.AWS.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add workflow services from appsettings.json
builder.Services.AddGenesisWorkflow(
    builder.Configuration.GetSection("Workflow"));

var app = builder.Build();
```

#### Using Action Configuration

```csharp
builder.Services.AddGenesisWorkflow(options =>
{
    options.Region = "us-east-1";
    options.StateMachineArns = new Dictionary<string, string>
    {
        ["OrderProcessing"] = "arn:aws:states:us-east-1:123456789012:stateMachine:OrderWorkflow"
    };
    options.UseLocalEmulator = true;
    options.LocalEmulatorUrl = new Uri("http://localhost:4566");
});
```

### 2. Start Workflow Execution

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class OrderService
{
    private readonly IWorkflow _workflow;

    public OrderService(IWorkflow workflow)
    {
        _workflow = workflow;
    }

    public async Task ProcessOrderAsync(Order order, CancellationToken ct)
    {
        var input = new
        {
            orderId = order.Id,
            customerId = order.CustomerId,
            items = order.Items,
            totalAmount = order.TotalAmount
        };

        try
        {
            var executionArn = await _workflow.StartExecutionAsync(
                "OrderProcessing",
                input,
                ct);

            _logger.LogInformation("Started order workflow: {ExecutionArn}", executionArn);
            
            // Store executionArn for later status checks
            order.WorkflowExecutionArn = executionArn;
            await _db.SaveChangesAsync(ct);
        }
        catch (GenesisException ex)
        {
            _logger.LogError(ex, "Failed to start order workflow");
            throw;
        }
    }
}
```

### 3. Check Execution Status

```csharp
public async Task<string> GetOrderStatusAsync(string executionArn, CancellationToken ct)
{
    var status = await _workflow.GetExecutionStatusAsync(executionArn, ct);

    return status switch
    {
        "RUNNING" => "Processing",
        "SUCCEEDED" => "Completed",
        "FAILED" => "Failed",
        "TIMED_OUT" => "Timed Out",
        "ABORTED" => "Cancelled",
        _ => "Unknown"
    };
}
```

### 4. Get Execution Output

```csharp
public record OrderResult(string OrderId, string Status, decimal FinalAmount);

public async Task<OrderResult?> GetOrderResultAsync(string executionArn, CancellationToken ct)
{
    // Returns null if execution hasn't completed yet
    var result = await _workflow.GetExecutionOutputAsync<OrderResult>(executionArn, ct);

    if (result is null)
    {
        _logger.LogInformation("Order workflow still running");
        return null;
    }

    _logger.LogInformation("Order {OrderId} completed with status {Status}",
        result.OrderId, result.Status);

    return result;
}
```

### 5. Stop Running Execution

```csharp
public async Task CancelOrderAsync(string executionArn, CancellationToken ct)
{
    var stopped = await _workflow.StopExecutionAsync(executionArn, ct);

    if (stopped)
    {
        _logger.LogInformation("Order workflow cancelled: {ExecutionArn}", executionArn);
    }
    else
    {
        _logger.LogWarning("Failed to cancel order workflow: {ExecutionArn}", executionArn);
    }
}
```

## Step Functions State Machine Example

### Simple Order Processing Workflow

```json
{
  "Comment": "Order processing workflow",
  "StartAt": "ValidateOrder",
  "States": {
    "ValidateOrder": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:us-east-1:123456789012:function:ValidateOrder",
      "Next": "ProcessPayment",
      "Catch": [{
        "ErrorEquals": ["ValidationError"],
        "Next": "OrderFailed"
      }]
    },
    "ProcessPayment": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:us-east-1:123456789012:function:ProcessPayment",
      "Next": "UpdateInventory",
      "Catch": [{
        "ErrorEquals": ["PaymentError"],
        "Next": "PaymentFailed"
      }]
    },
    "UpdateInventory": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:us-east-1:123456789012:function:UpdateInventory",
      "Next": "OrderCompleted"
    },
    "OrderCompleted": {
      "Type": "Succeed"
    },
    "OrderFailed": {
      "Type": "Fail",
      "Error": "OrderValidationFailed",
      "Cause": "Order validation failed"
    },
    "PaymentFailed": {
      "Type": "Fail",
      "Error": "PaymentProcessingFailed",
      "Cause": "Payment processing failed"
    }
  }
}
```

### Creating State Machine with AWS CLI

```bash
# Create state machine
aws stepfunctions create-state-machine \
  --name OrderWorkflow \
  --definition file://order-workflow.json \
  --role-arn arn:aws:iam::123456789012:role/StepFunctionsExecutionRole

# Get ARN (use this in appsettings.json)
aws stepfunctions list-state-machines --query "stateMachines[?name=='OrderWorkflow'].stateMachineArn" --output text
```

## IAM Permissions

### Application Permissions (Step Functions Client)

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "states:StartExecution",
        "states:DescribeExecution",
        "states:StopExecution"
      ],
      "Resource": "arn:aws:states:us-east-1:123456789012:stateMachine:*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "states:DescribeExecution"
      ],
      "Resource": "arn:aws:states:us-east-1:123456789012:execution:*"
    }
  ]
}
```

### Step Functions Execution Role (for State Machine)

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "lambda:InvokeFunction"
      ],
      "Resource": "arn:aws:lambda:us-east-1:123456789012:function:*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "dynamodb:GetItem",
        "dynamodb:PutItem",
        "dynamodb:UpdateItem"
      ],
      "Resource": "arn:aws:dynamodb:us-east-1:123456789012:table/*"
    }
  ]
}
```

## LocalStack Configuration

For local development without AWS:

### docker-compose.yml

```yaml
services:
  localstack:
    image: localstack/localstack:latest
    ports:
      - "4566:4566"
    environment:
      - SERVICES=stepfunctions,lambda
      - DEBUG=1
      - DATA_DIR=/tmp/localstack/data
      - LAMBDA_EXECUTOR=docker
      - DOCKER_HOST=unix:///var/run/docker.sock
    volumes:
      - "./localstack-data:/tmp/localstack"
      - "/var/run/docker.sock:/var/run/docker.sock"
```

### appsettings.Development.json

```json
{
  "Workflow": {
    "Region": "us-east-1",
    "StateMachineArns": {
      "TestWorkflow": "arn:aws:states:us-east-1:000000000000:stateMachine:TestWorkflow"
    },
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566"
  }
}
```

### Create State Machine in LocalStack

```bash
# Set AWS endpoint for LocalStack
export AWS_ENDPOINT_URL=http://localhost:4566

# Create state machine
awslocal stepfunctions create-state-machine \
  --name TestWorkflow \
  --definition file://test-workflow.json \
  --role-arn arn:aws:iam::000000000000:role/DummyRole

# List state machines
awslocal stepfunctions list-state-machines
```

## Execution Status Values

Step Functions returns the following status values:

| Status | Description |
|--------|-------------|
| `RUNNING` | Execution is in progress |
| `SUCCEEDED` | Execution completed successfully |
| `FAILED` | Execution failed due to an error |
| `TIMED_OUT` | Execution exceeded timeout limit |
| `ABORTED` | Execution was manually stopped |

## Polling Pattern

For long-running workflows, implement a polling pattern:

```csharp
public async Task<OrderResult> WaitForCompletionAsync(
    string executionArn,
    TimeSpan timeout,
    CancellationToken ct)
{
    var pollInterval = TimeSpan.FromSeconds(5);
    var stopwatch = Stopwatch.StartNew();

    while (stopwatch.Elapsed < timeout)
    {
        var status = await _workflow.GetExecutionStatusAsync(executionArn, ct);

        if (status == "SUCCEEDED")
        {
            var result = await _workflow.GetExecutionOutputAsync<OrderResult>(executionArn, ct);
            return result ?? throw new InvalidOperationException("No output from succeeded execution");
        }

        if (status == "FAILED" || status == "TIMED_OUT" || status == "ABORTED")
        {
            throw new InvalidOperationException($"Workflow failed with status: {status}");
        }

        await Task.Delay(pollInterval, ct);
    }

    throw new TimeoutException($"Workflow did not complete within {timeout}");
}
```

## Event-Driven Pattern (Recommended)

Instead of polling, use EventBridge to receive execution completion events:

```csharp
// Subscribe to Step Functions execution events in EventBridge
// Rule pattern:
{
  "source": ["aws.states"],
  "detail-type": ["Step Functions Execution Status Change"],
  "detail": {
    "status": ["SUCCEEDED", "FAILED", "ABORTED", "TIMED_OUT"]
  }
}
```

Then handle events in your application via SQS/SNS subscription.

## Troubleshooting

### 1. Execution Not Found

**Problem:** `GenesisException: Execution not found`

**Solution:**
- Verify the execution ARN is correct (starts with `arn:aws:states:`)
- Check if execution was from a different AWS account or region
- Execution may have been deleted (Step Functions retains history for 90 days)

### 2. State Machine ARN Not Found

**Problem:** `GenesisException: State machine ARN not found for workflow: XYZ`

**Solution:**
- Ensure workflow name matches a key in `StateMachineArns` dictionary (case-sensitive)
- Verify configuration is being loaded correctly
- Check appsettings.json for typos

### 3. Invalid State Machine ARN

**Problem:** Validation fails with `StartsWith("arn:aws:states:")`

**Solution:**
- ARN must start with `arn:aws:states:` (not `arn:aws:stepfunctions:`)
- Format: `arn:aws:states:region:account:stateMachine:name`
- Get ARN from AWS Console or `aws stepfunctions list-state-machines`

### 4. JSON Serialization Errors

**Problem:** `JsonException` when getting execution output

**Solution:**
- Ensure output type `T` matches the structure of Step Functions output
- State machine must output valid JSON
- Use nullable types for optional fields
- Check camelCase vs PascalCase naming (configured as camelCase by default)

### 5. Execution Fails Immediately

**Problem:** Execution status is `FAILED` immediately after start

**Solution:**
- Check Step Functions console for execution details
- Verify Lambda functions or tasks referenced in state machine exist
- Check IAM permissions for Step Functions execution role
- Review CloudWatch Logs for Lambda errors

## Best Practices

1. **State Machine Design**
   - Keep state machines focused on coordination, not business logic
   - Use Lambda functions for complex processing
   - Implement error handling with Catch and Retry
   - Set appropriate timeouts for each state

2. **Execution Names**
   - Use meaningful prefixes via `ExecutionNamePrefix`
   - Names are auto-generated with timestamps and UUIDs
   - Avoid hardcoding execution names (leads to conflicts)

3. **Input/Output**
   - Keep inputs and outputs under 256 KB (Step Functions limit)
   - For large data, pass S3 keys instead of full payloads
   - Use strongly-typed records for type safety

4. **Error Handling**
   - Always wrap workflow calls in try-catch
   - Check execution status before getting output
   - Handle `GenesisException` specifically
   - Implement retry logic for transient failures

5. **Monitoring**
   - Enable CloudWatch logging for state machines
   - Set up CloudWatch alarms for failed executions
   - Use X-Ray tracing for distributed workflows
   - Track execution duration and costs

6. **Cost Optimization**
   - Step Functions charges per state transition ($0.025 per 1,000 transitions)
   - Use Express Workflows for high-volume, short-duration workflows (cheaper)
   - Minimize unnecessary state transitions
   - Consider Lambda Step Functions integration for simple workflows

## Related Packages

- **Pervaxis.Genesis.Base** - Base abstractions and exceptions
- **Pervaxis.Core.Abstractions** - Core module interfaces (`IWorkflow`)
- **Pervaxis.Genesis.Messaging.AWS** - For event-driven workflow triggers

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
