# SNS-to-SQS Subscription Requirements for Genesis Messaging Phase 1

## Overview

Genesis Messaging Phase 1 uses **shared queues/topics with message-attribute based tenant isolation**. For SNS-to-SQS subscriptions to work correctly, the subscription **MUST** have `RawMessageDelivery` enabled. This ensures that message attributes (including `pervaxis_tenant_id`) are passed through from SNS to SQS.

## Why RawMessageDelivery is Required

By default, SNS wraps messages in a JSON envelope when delivering to SQS. This envelope **discards message attributes**, which breaks tenant isolation. Setting `RawMessageDelivery=true` tells SNS to deliver the raw message body and preserve all attributes.

### Without RawMessageDelivery (Default Behavior - ❌ BROKEN)
```json
{
  "Type": "Notification",
  "MessageId": "abc123",
  "TopicArn": "arn:aws:sns:us-east-1:123:topic",
  "Message": "{\"orderId\": 42}",
  "Timestamp": "2026-11-11T10:00:00Z"
  // ❌ Message attributes are LOST
}
```

### With RawMessageDelivery (Phase 1 Requirement - ✅ CORRECT)
```json
{
  "orderId": 42
}
// ✅ Message attributes preserved (pervaxis_tenant_id, etc.)
```

## Infrastructure as Code Examples

### Terraform

```hcl
resource "aws_sns_topic_subscription" "orders_shared_subscription" {
  topic_arn = aws_sns_topic.orders_shared.arn
  protocol  = "sqs"
  endpoint  = aws_sqs_queue.orders_shared.arn

  # ✅ REQUIRED for Phase 1 tenant isolation
  raw_message_delivery = true
}
```

### AWS CloudFormation

```yaml
OrdersSharedSubscription:
  Type: AWS::SNS::Subscription
  Properties:
    TopicArn: !Ref OrdersSharedTopic
    Protocol: sqs
    Endpoint: !GetAtt OrdersSharedQueue.Arn
    # ✅ REQUIRED for Phase 1 tenant isolation
    RawMessageDelivery: true
```

### AWS CDK (TypeScript)

```typescript
const subscription = topic.addSubscription(
  new sqs.SqsSubscription(queue, {
    // ✅ REQUIRED for Phase 1 tenant isolation
    rawMessageDelivery: true
  })
);
```

### AWS CDK (Python)

```python
from aws_cdk import aws_sns_subscriptions as subs

subscription = topic.add_subscription(
    subs.SqsSubscription(
        queue,
        # ✅ REQUIRED for Phase 1 tenant isolation
        raw_message_delivery=True
    )
)
```

### Pulumi (TypeScript)

```typescript
const subscription = new aws.sns.TopicSubscription("orders-subscription", {
    topic: topic.arn,
    protocol: "sqs",
    endpoint: queue.arn,
    // ✅ REQUIRED for Phase 1 tenant isolation
    rawMessageDelivery: true,
});
```

## Verification

### Check Existing Subscription (AWS CLI)

```bash
# List subscriptions for a topic
aws sns list-subscriptions-by-topic --topic-arn arn:aws:sns:us-east-1:123:orders-shared

# Check RawMessageDelivery attribute
aws sns get-subscription-attributes \
  --subscription-arn arn:aws:sns:us-east-1:123:orders-shared:abc123 \
  --query 'Attributes.RawMessageDelivery'
```

Expected output: `"true"`

### Update Existing Subscription

```bash
aws sns set-subscription-attributes \
  --subscription-arn arn:aws:sns:us-east-1:123:orders-shared:abc123 \
  --attribute-name RawMessageDelivery \
  --attribute-value true
```

## Application Code

The `SnsMessagingProvider.SubscribeAsync()` method **automatically sets `RawMessageDelivery=true`** for SQS subscriptions:

```csharp
// ✅ Genesis automatically enables raw delivery for SQS
var subscriptionArn = await snsProvider.SubscribeAsync(
    "notifications",
    "arn:aws:sqs:us-east-1:123:orders-shared"
);
```

For **infrastructure-provisioned subscriptions** (Terraform, CloudFormation, etc.), you **MUST manually set** `RawMessageDelivery=true` as shown above.

## Phase 1 vs Phase 2

| Aspect | Phase 1 (Current) | Phase 2 (Future) |
|--------|------------------|------------------|
| **Queue Model** | Shared queues (`orders-shared`) | Per-tenant queues (`orders-tenant-acme`) |
| **Tenant Isolation** | App-enforced via `pervaxis_tenant_id` attribute | IAM-enforced + app-enforced |
| **RawMessageDelivery** | ✅ **REQUIRED** | ✅ Still required |

## Troubleshooting

### Symptom: Consumer receives messages without tenant ID
**Cause:** Subscription has `RawMessageDelivery=false` (default)  
**Fix:** Update subscription to `RawMessageDelivery=true` (see examples above)

### Symptom: Consumer dead-letters all messages with `missing_tenant_id`
**Cause:** Same as above - attributes not passing through  
**Fix:** Same as above

### Symptom: Publisher succeeds but consumer never receives messages
**Cause:** May be unrelated to RawMessageDelivery - check SQS queue permissions  
**Fix:** Ensure SNS topic has permission to send to SQS queue:

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": {"Service": "sns.amazonaws.com"},
    "Action": "sqs:SendMessage",
    "Resource": "arn:aws:sqs:us-east-1:123:orders-shared",
    "Condition": {
      "ArnEquals": {
        "aws:SourceArn": "arn:aws:sqs:us-east-1:123:orders-shared"
      }
    }
  }]
}
```

## Security Note

**Tenant isolation in Phase 1 is app-enforced, not IAM-enforced.**  
Applications MUST validate the `pervaxis_tenant_id` attribute and reject invalid messages. Phase 2 will add IAM-based enforcement using per-tenant resources.

## References

- [AWS SNS RawMessageDelivery Documentation](https://docs.aws.amazon.com/sns/latest/dg/sns-large-payload-raw-message-delivery.html)
- Genesis Messaging Phase 1 Spec: `MULTI_TENANT_MESSAGING_STEP5_FINAL.md`
- Phase 1B Consumer Implementation: TBD (in progress)
