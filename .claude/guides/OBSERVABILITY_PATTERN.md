# Observability Implementation Pattern for Genesis Providers

## Task 4.1.3: Observability Integration

This document describes the standard pattern for adding distributed tracing to all Genesis AWS providers.

## Pattern Overview

Each provider's public interface methods get instrumented with:
1. **Activity/Span** - using `PervaxisActivitySource.StartActivity()`
2. **Standard Tags** - operation name, resource identifiers
3. **Tenant Tags** - when tenant isolation is enabled
4. **Status Tracking** - success/error state

## Implementation Steps Per Provider

### 1. Add Using Statement
```csharp
using System.Diagnostics;
using Pervaxis.Core.Observability.Tracing;
```

### 2. Add Helper Method
Add this method before the closing brace of each provider class:

```csharp
private void AddTenantTags(Activity? activity)
{
    if (activity == null || !_options.EnableTenantIsolation || _tenantContext?.IsResolved != true)
    {
        return;
    }

    activity.SetTag("tenant.id", _tenantContext.TenantId.Value);
    activity.SetTag("tenant.name", _tenantContext.TenantName);
}
```

### 3. Instrument Public Methods

**Pattern:**
```csharp
public async Task<TResult> MethodAsync(...)
{
    using var activity = PervaxisActivitySource.StartActivity("{provider}.{operation}", ActivityKind.{kind});
    activity?.SetTag("{provider}.key_tag", value);
    activity?.SetTag("{provider}.operation", "operation_name");
    AddTenantTags(activity);

    // ... existing validation ...

    try
    {
        // ... existing implementation ...
        
        activity?.SetTag("{provider}.success", result);  // On success
        return result;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        // ... existing error handling ...
    }
}
```

## Provider-Specific Patterns

### Caching.AWS (ElastiCacheProvider)
- **Span Names**: `cache.get`, `cache.set`, `cache.remove`, `cache.exists`, `cache.get_many`, `cache.set_many`, `cache.refresh`
- **ActivityKind**: `Client`
- **Tags**:
  - `cache.key` - the cache key
  - `cache.operation` - operation name
  - `cache.hit` - true/false (for get operations)
  - `cache.success` - true/false
  - `cache.key_count` - for batch operations

### Messaging.AWS (SQS/SNS)
- **Span Names**: `messaging.publish`, `messaging.publish_batch`, `messaging.receive`, `messaging.delete`, `messaging.subscribe`
- **ActivityKind**: `Producer` (publish), `Consumer` (receive), `Client` (others)
- **Tags**:
  - `messaging.system` - "sqs" or "sns"
  - `messaging.destination` - queue/topic name
  - `messaging.operation` - operation name
  - `messaging.message_id` - returned message ID
  - `messaging.message_count` - for batch operations

### FileStorage.AWS (S3)
- **Span Names**: `storage.upload`, `storage.download`, `storage.delete`, `storage.exists`, `storage.list`, `storage.get_metadata`, `storage.get_presigned_url`
- **ActivityKind**: `Client`
- **Tags**:
  - `storage.system` - "s3"
  - `storage.bucket` - bucket name
  - `storage.key` - object key
  - `storage.operation` - operation name
  - `storage.size` - file size (when available)

### Search.AWS (OpenSearch)
- **Span Names**: `search.index`, `search.search`, `search.delete`, `search.bulk_index`
- **ActivityKind**: `Client`
- **Tags**:
  - `search.system` - "opensearch"
  - `search.index` - index name
  - `search.operation` - operation name
  - `search.document_count` - for bulk operations
  - `search.result_count` - for search results

### Notifications.AWS (SES/SNS)
- **Span Names**: `notification.send_email`, `notification.send_templated_email`, `notification.send_sms`, `notification.send_push`
- **ActivityKind**: `Producer`
- **Tags**:
  - `notification.type` - "email", "sms", "push"
  - `notification.destination` - recipient
  - `notification.operation` - operation name
  - `notification.message_id` - returned message ID

### Workflow.AWS (Step Functions)
- **Span Names**: `workflow.start`, `workflow.get_status`, `workflow.get_output`, `workflow.stop`
- **ActivityKind**: `Client`
- **Tags**:
  - `workflow.system` - "stepfunctions"
  - `workflow.name` - workflow name
  - `workflow.execution_id` - execution ARN
  - `workflow.operation` - operation name
  - `workflow.status` - execution status

### AIAssistance.AWS (Bedrock)
- **Span Names**: `ai.generate_text`, `ai.generate_embedding`, `ai.generate_image`
- **ActivityKind**: `Client`
- **Tags**:
  - `ai.system` - "bedrock"
  - `ai.model` - model ID
  - `ai.operation` - operation name
  - `ai.token_count` - when available

### Reporting.AWS (Metabase)
- **Span Names**: `reporting.execute_query`, `reporting.get_dashboard`, `reporting.create_dashboard`, `reporting.export_report`
- **ActivityKind**: `Client`
- **Tags**:
  - `reporting.system` - "metabase"
  - `reporting.operation` - operation name
  - `reporting.dashboard_id` - for dashboard operations
  - `reporting.format` - for export operations

## Status Completed

- ✅ **Caching.AWS**: Fully implemented (7 methods instrumented)
- 🔄 **Messaging.AWS**: Partially implemented (SQS PublishAsync done)
- ⏳ **FileStorage.AWS**: Pending
- ⏳ **Search.AWS**: Pending
- ⏳ **Notifications.AWS**: Pending
- ⏳ **Workflow.AWS**: Pending
- ⏳ **AIAssistance.AWS**: Pending
- ⏳ **Reporting.AWS**: Pending

## Testing Observability

Once implemented, observability can be tested by:
1. Configure OpenTelemetry in consuming application
2. Operations automatically create spans in distributed traces
3. Spans include tenant context for multi-tenant scenarios
4. Error states properly propagated to tracing backend

## Notes

- Activity creation is safe (returns null when no listener configured)
- All tagging operations are null-safe (activity?.SetTag)
- No performance impact when tracing is disabled
- Tenant tags only added when tenant context is resolved
