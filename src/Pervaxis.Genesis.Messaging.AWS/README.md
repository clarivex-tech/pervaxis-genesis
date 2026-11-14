# Pervaxis.Genesis.Messaging.AWS

AWS messaging provider for the Pervaxis Genesis platform — SQS for queue-based messaging and SNS for pub/sub topic delivery.

## Overview

`Pervaxis.Genesis.Messaging.AWS` implements the `IMessaging` abstraction from `Pervaxis.Core.Abstractions` using the AWS SDK for SQS and SNS. Both providers share a single `MessagingOptions` and can be registered independently or together as keyed services.

| Provider | Queue/Topic | Publish | Batch | Receive | Delete | Subscribe |
|---|---|---|---|---|---|---|
| `SqsMessagingProvider` | SQS Queue | Yes | Yes | Yes | Yes | No |
| `SnsMessagingProvider` | SNS Topic | Yes | Yes | No | No | Platform provisioning only |

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
builder.Services.AddGenesisSnsMessaging(
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

Tenant providers and shared consumers are **Scoped**. Keyed AWS clients are container-owned **Singletons**, shared across scopes. Resolve providers inside a DI scope; scope disposal does not dispose AWS clients.

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

### Receiving and deleting for the current tenant

Resolve SqsMessagingProvider in a tenant DI scope. Receive metadata and acknowledge
with the same instance and logical queue:

    var messages = await messaging.ReceiveWithMetadataAsync<Order>("orders", cancellationToken: ct);
    foreach (var item in messages)
    {
        await ProcessOrderAsync(item.Message, ct);
        await messaging.DeleteAsync("orders", item.ReceiptHandle, ct);
    }

For shared background processing across tenants, use ISharedQueueConsumer below.

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

### Shared-topic subscriptions

Subscription management is infrastructure provisioning. SubscribeAsync rejects tenant
calls with NotSupportedException: it cannot authorize ownership of an arbitrary endpoint
or safely modify a subscription shared with other tenants.

Provision shared SNS-to-SQS subscriptions with RawMessageDelivery=true so the reserved
pervaxis_tenant_id attribute reaches SQS. Shared worker subscriptions must accept all
required tenant traffic. Tenant-specific endpoints require infrastructure to authorize
endpoint ownership and apply an exact tenant subscription filter. Tenant application
roles should not have subscription-management permissions.

## Tenant isolation and shared consumers

All tenants use the same configured physical queue/topic per logical destination.
Single and batch publish stamp pervaxis_tenant_id from ITenantContext, never payload
properties. Missing, invalid, unresolved or changed tenant contexts are rejected,
including empty batches. EnableTenantIsolation=false is rejected in every environment.

### Tenant-facing receive and acknowledgement

ReceiveAsync and ReceiveWithMetadataAsync require a resolved tenant and return only
matching messages. Other tenants' valid messages are released with zero visibility
delay without deserializing or exposing their payloads, quarantining or acknowledging
them. DeleteAsync requires a receipt received for that tenant and queue by the same
provider instance. Use ReceiveWithMetadataAsync and acknowledge in the same DI scope;
raw SDK receipts and receipts from another provider scope are rejected.

SQS cannot select by tenant attribute. Use the shared worker for normal production
consumption: tenant-specific polling adds receives for other tenants and cannot
guarantee fair dispatch.

### Trusted shared-queue worker

Register this separately in the worker host, with Core's ambient ITenantContext and
ITenantScopeFactory configured. Never expose ISharedQueueConsumer through tenant
endpoints. Registration accepts configuration or an options action:

    builder.Services.AddGenesisSharedQueueConsumer(
        builder.Configuration.GetSection("Messaging"));

A hosted service creates a DI scope for each poll and resolves the consumer outside
any active tenant scope:

    await using var pollScope = scopeFactory.CreateAsyncScope();
    var consumer = pollScope.ServiceProvider.GetRequiredService<ISharedQueueConsumer>();
    await consumer.ProcessAsync<OrderEvent>("orders", async (message, services, ct) =>
    {
        var handler = services.GetRequiredService<OrderEventHandler>();
        await handler.HandleAsync(message, ct);
    }, cancellationToken: stoppingToken);

Each validated message gets its own tenant scope and async DI scope. Resolve
tenant-sensitive dependencies from the supplied services, not from the hosted service
or polling scope. Overlapping handlers have independent contexts. The worker checks
the scoped tenant context before and after the handler, disposes handler dependencies
and the tenant scope, then acknowledges success. Tenant IDs must also satisfy the
registered Core scope factory's rules, for example GUID-only IDs.

Failed handlers, mismatched contexts, scope disposal failures and canceled work are
not acknowledged. Cancellation propagates and scopes are always cleaned up. Handlers
must tolerate SQS redelivery. Configure visibility timeout for handler duration;
automatic visibility renewal is not implemented.

Missing/invalid tenant attributes and malformed/null payloads are sent to the configured
DLQ before acknowledging the source. An absent/failing DLQ leaves the source
unacknowledged. Provision the DLQ separately from the source. Only an exact String
tenant attribute is accepted; reserved platform identities, alternate data types and
conflicting casing variants are rejected.

### Trust boundary and compatibility

Queue/topic policies must restrict writers to trusted tenant-stamping producers.
A message attribute is not authentication: direct SDK writers can forge it. The worker
trusts authenticated infrastructure ingress, not tenant IDs inside business payloads.

Breaking changes: providers are Scoped; isolation cannot be disabled; receives return
only matching tenants; acknowledgement requires a receipt from the same provider scope;
cross-tenant processing requires explicit worker registration; tenant subscription
management is disabled. Existing physical mappings and pervaxis_tenant_id wire
attributes are unchanged.

This change does not provision resources, implement per-tenant resources or
platform-message APIs, or add FIFO-specific grouping/deduplication controls.

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
    "sqs:ChangeMessageVisibility",
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
    "sns:PublishBatch"
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
| SubscribeAsync rejected | Tenant messaging cannot manage shared subscriptions | Provision through platform infrastructure |
| 403 / credentials error | Missing IAM permissions or no credentials | Check IAM policy and AWS credential chain |
