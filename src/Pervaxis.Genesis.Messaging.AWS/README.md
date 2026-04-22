# Pervaxis.Genesis.Messaging.AWS

AWS messaging provider for the Pervaxis Genesis platform — SQS for queue-based messaging and SNS for pub/sub topic delivery.

## Overview

`Pervaxis.Genesis.Messaging.AWS` implements the `IMessaging` abstraction from `Pervaxis.Core.Abstractions` using the AWS SDK for SQS and SNS. Both providers share a single `MessagingOptions` and can be registered independently or together as keyed services.

| Provider | Queue/Topic | Publish | Batch | Receive | Delete | Subscribe |
|---|---|---|---|---|---|---|
| `SqsMessagingProvider` | SQS Queue | ✅ | ✅ | ✅ | ✅ | ❌ |
| `SnsMessagingProvider` | SNS Topic | ✅ | ✅ | ❌ | ❌ | ✅ |

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.Messaging.AWS" Version="1.0.0" />
```

## Configuration

### appsettings.json

```json
{
  "Messaging": {
    "Region": "ap-south-1",
    "Sqs": {
      "DefaultQueueUrl": "https://sqs.ap-south-1.amazonaws.com/123456789012/default-queue",
      "QueueUrlMappings": {
        "orders": "https://sqs.ap-south-1.amazonaws.com/123456789012/orders-queue",
        "payments": "https://sqs.ap-south-1.amazonaws.com/123456789012/payments-queue"
      },
      "MaxNumberOfMessages": 10,
      "WaitTimeSeconds": 20,
      "VisibilityTimeoutSeconds": 30
    },
    "Sns": {
      "DefaultTopicArn": "arn:aws:sns:ap-south-1:123456789012:default-topic",
      "TopicArnMappings": {
        "order-events": "arn:aws:sns:ap-south-1:123456789012:order-events-topic"
      }
    }
  }
}
```

### Option properties

**Root (`MessagingOptions`)**

| Property | Type | Default | Description |
|---|---|---|---|
| `Region` | `string` | *(required)* | AWS region (e.g. `ap-south-1`) |
| `UseLocalEmulator` | `bool` | `false` | Route requests to LocalStack |
| `LocalEmulatorUrl` | `Uri?` | `null` | LocalStack endpoint (e.g. `http://localhost:4566`) |

**SQS (`SqsOptions`)**

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultQueueUrl` | `string` | `""` | Fallback queue URL when destination has no explicit mapping |
| `QueueUrlMappings` | `Dictionary<string, string>` | `{}` | Logical name → queue URL overrides |
| `MaxNumberOfMessages` | `int` | `10` | Max messages per receive call (1–10) |
| `WaitTimeSeconds` | `int` | `20` | Long-polling wait time in seconds (0 = short poll) |
| `VisibilityTimeoutSeconds` | `int` | `30` | Message hide duration after receive |

**SNS (`SnsOptions`)**

| Property | Type | Default | Description |
|---|---|---|---|
| `DefaultTopicArn` | `string` | `""` | Fallback topic ARN when destination has no explicit mapping |
| `TopicArnMappings` | `Dictionary<string, string>` | `{}` | Logical name → topic ARN overrides |

## Registration

### SQS only

```csharp
// From IConfiguration
builder.Services.AddGenesisSqsMessaging(
    builder.Configuration.GetSection("Messaging"));

// From action
builder.Services.AddGenesisSqsMessaging(options =>
{
    options.Region = "ap-south-1";
    options.Sqs.DefaultQueueUrl = "https://sqs.ap-south-1.amazonaws.com/123/my-queue";
});
```

### SNS only

```csharp
builder.Services.AddGenesisSnsMess aging(
    builder.Configuration.GetSection("Messaging"));
```

### Both (keyed services)

```csharp
builder.Services.AddGenesisMessaging(
    builder.Configuration.GetSection("Messaging"));
```

Inject by key:

```csharp
public class OrderService(
    [FromKeyedServices("sqs")] IMessaging queue,
    [FromKeyedServices("sns")] IMessaging topic)
```

All providers are registered as **Singleton**. AWS clients use `Lazy<T>` and are created on first use.

## Usage

### Publishing to SQS

```csharp
public class OrderService(IMessaging messaging)
{
    public async Task PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        var messageId = await messaging.PublishAsync("orders", order, ct);
        // messageId = SQS MessageId
    }

    public async Task PlaceOrdersBatchAsync(IEnumerable<Order> orders, CancellationToken ct = default)
    {
        var messageIds = await messaging.PublishBatchAsync("orders", orders, ct);
        // Automatically chunked into batches of 10
    }
}
```

### Receiving and deleting from SQS

```csharp
public class OrderConsumer(IMessaging messaging)
{
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        var orders = await messaging.ReceiveAsync<Order>("orders", maxMessages: 5, ct);
        foreach (var order in orders)
        {
            await ProcessOrderAsync(order, ct);
            // Note: receipt handle is not returned through IMessaging.
            // For full consume-then-delete, inject SqsMessagingProvider directly
            // or use a background worker with the AWS SDK.
        }
    }
}
```

> **Receipt handle note:** `IMessaging.ReceiveAsync<T>` returns deserialized message bodies only. To delete a message after processing via `DeleteAsync`, you need its receipt handle — which requires direct access to the SQS response. For worker/consumer patterns, consider injecting `SqsMessagingProvider` directly, or using a hosted background service.

### Publishing to SNS

```csharp
public class EventPublisher([FromKeyedServices("sns")] IMessaging topic)
{
    public async Task PublishOrderEventAsync(OrderPlacedEvent evt, CancellationToken ct = default)
    {
        var messageId = await topic.PublishAsync("order-events", evt, ct);
    }
}
```

### Subscribing to SNS

```csharp
var subscriptionArn = await topic.SubscribeAsync(
    "order-events",
    "https://my-service.example.com/webhooks/orders");

// Protocol is inferred automatically:
// https:// → "https"
// http://  → "http"
// arn:aws:sqs:... → "sqs"
// user@domain → "email"
```

## IAM Permissions

### SQS

```json
{
  "Effect": "Allow",
  "Action": [
    "sqs:SendMessage",
    "sqs:SendMessageBatch",
    "sqs:ReceiveMessage",
    "sqs:DeleteMessage",
    "sqs:DeleteMessageBatch",
    "sqs:GetQueueAttributes"
  ],
  "Resource": "arn:aws:sqs:ap-south-1:123456789012:*"
}
```

### SNS

```json
{
  "Effect": "Allow",
  "Action": [
    "sns:Publish",
    "sns:PublishBatch",
    "sns:Subscribe"
  ],
  "Resource": "arn:aws:sns:ap-south-1:123456789012:*"
}
```

## Local Development with LocalStack

```json
{
  "Messaging": {
    "Region": "us-east-1",
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566",
    "Sqs": {
      "DefaultQueueUrl": "http://localhost:4566/000000000000/my-queue"
    },
    "Sns": {
      "DefaultTopicArn": "arn:aws:sns:us-east-1:000000000000:my-topic"
    }
  }
}
```

```bash
# Start LocalStack
docker run -d -p 4566:4566 localstack/localstack

# Create queue and topic
awslocal sqs create-queue --queue-name my-queue
awslocal sns create-topic --name my-topic
```

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `GenesisConfigurationException` on startup | `Region` is empty | Set `Region` in options |
| `GenesisException: SQS publish failed` | Queue URL wrong or queue doesn't exist | Verify `DefaultQueueUrl` or `QueueUrlMappings` |
| `GenesisException: SNS publish failed` | Topic ARN wrong | Verify `DefaultTopicArn` or `TopicArnMappings` |
| `NotSupportedException` on `ReceiveAsync` | Using `SnsMessagingProvider` | Use `SqsMessagingProvider` for receive |
| `NotSupportedException` on `SubscribeAsync` | Using `SqsMessagingProvider` | Use `SnsMessagingProvider` for subscribe |
| 403 / credentials error | Missing IAM permissions or no credentials | Check IAM policy and AWS credential chain |
