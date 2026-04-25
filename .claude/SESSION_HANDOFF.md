# Session Handoff - Task 4.1.3-CONTINUE

## 📋 Next Task: Complete Observability Integration

**Task Number**: **4.1.3-CONTINUE**  
**Branch**: `feature/observability-integration`  
**Status**: 1.5/8 providers complete (Caching ✅, SQS partial 🔄)

---

## Quick Start

```bash
git checkout feature/observability-integration
# Read the pattern guide first
cat .claude/guides/OBSERVABILITY_PATTERN.md
```

---

## What's Done ✅

### Caching.AWS - 100% Complete
All 7 methods instrumented:
- GetAsync, SetAsync, RemoveAsync, ExistsAsync
- GetManyAsync, SetManyAsync, RefreshAsync
- Helper method: `AddTenantTags(Activity?)`

### Messaging.AWS (SQS) - 25% Complete
- ✅ PublishAsync
- ⏳ PublishBatchAsync, ReceiveAsync, DeleteAsync

---

## What Remains ⏳

**32 methods across 7.5 providers:**

1. **SQS (3 methods)** - 75% remaining
   - PublishBatchAsync (line ~155)
   - ReceiveAsync (line ~214)
   - DeleteAsync (line ~256)

2. **SNS (3 methods)** - 100% remaining
   - PublishAsync (line ~106)
   - PublishBatchAsync (line ~145)
   - SubscribeAsync (line ~242)

3. **FileStorage.AWS/S3 (7 methods)** - 100% remaining
   - UploadAsync, DownloadAsync, DeleteAsync, ExistsAsync
   - GetPresignedUrlAsync, GetMetadataAsync, ListAsync

4. **Search.AWS/OpenSearch (4 methods)** - 100% remaining
   - IndexAsync, SearchAsync, DeleteAsync, BulkIndexAsync

5. **Notifications.AWS (4 methods)** - 100% remaining
   - SendEmailAsync, SendTemplatedEmailAsync, SendSmsAsync, SendPushAsync

6. **Workflow.AWS/StepFunctions (4 methods)** - 100% remaining
   - StartExecutionAsync, GetExecutionStatusAsync, GetExecutionOutputAsync, StopExecutionAsync

7. **AIAssistance.AWS/Bedrock (3 methods)** - 100% remaining
   - GenerateTextAsync, GenerateEmbeddingAsync, GenerateImageAsync

8. **Reporting.AWS/Metabase (4 methods)** - 100% remaining
   - ExecuteQueryAsync, GetDashboardAsync, CreateDashboardAsync, ExportReportAsync

---

## Implementation Pattern (3 Steps)

### Step 1: Add Using Statements (if not present)
```csharp
using System.Diagnostics;
using Pervaxis.Core.Observability.Tracing;
```

### Step 2: Add Helper Method (before closing brace)
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

### Step 3: Wrap Each Public Method
```csharp
public async Task<TResult> MethodAsync(...)
{
    using var activity = PervaxisActivitySource.StartActivity("{provider}.{operation}", ActivityKind.{kind});
    activity?.SetTag("{provider}.tag", value);
    activity?.SetTag("{provider}.operation", "operation_name");
    AddTenantTags(activity);

    // ... existing validation ...

    try
    {
        // ... existing implementation ...
        
        activity?.SetTag("{provider}.success", result);  // Mark success
        return result;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);  // Mark error
        throw;  // Re-throw existing exception
    }
}
```

---

## Provider-Specific Tags

See `.claude/guides/OBSERVABILITY_PATTERN.md` for complete tag specifications.

**Quick Reference:**
- **Caching**: `cache.key`, `cache.hit`, `cache.operation`
- **Messaging**: `messaging.system` (sqs/sns), `messaging.destination`, `messaging.message_id`
- **Storage**: `storage.bucket`, `storage.key`, `storage.size`
- **Search**: `search.index`, `search.document_count`
- **Notifications**: `notification.type`, `notification.destination`
- **Workflow**: `workflow.name`, `workflow.execution_id`, `workflow.status`
- **AI**: `ai.model`, `ai.token_count`
- **Reporting**: `reporting.dashboard_id`, `reporting.format`

**ActivityKind:**
- `Client` - most operations
- `Producer` - messaging publish, notifications
- `Consumer` - messaging receive

---

## Files to Modify

```
src/Pervaxis.Genesis.Messaging.AWS/Providers/Sqs/SqsMessagingProvider.cs
src/Pervaxis.Genesis.Messaging.AWS/Providers/Sns/SnsMessagingProvider.cs
src/Pervaxis.Genesis.FileStorage.AWS/Providers/S3/S3FileStorageProvider.cs
src/Pervaxis.Genesis.Search.AWS/Providers/OpenSearch/OpenSearchProvider.cs
src/Pervaxis.Genesis.Notifications.AWS/Providers/AwsNotificationProvider.cs
src/Pervaxis.Genesis.Workflow.AWS/Providers/StepFunctionsWorkflowProvider.cs
src/Pervaxis.Genesis.AIAssistance.AWS/Providers/BedrockAIAssistantProvider.cs
src/Pervaxis.Genesis.Reporting.AWS/Providers/MetabaseReportingProvider.cs
```

---

## Reference Implementation

**Complete Example:**
- `src/Pervaxis.Genesis.Caching.AWS/Providers/ElastiCache/ElastiCacheProvider.cs`
- Shows all patterns: activity creation, tagging, error handling, tenant enrichment

---

## Testing

```bash
# Build check
dotnet build Pervaxis.Genesis.slnx --configuration Release

# Verify all tests pass
dotnet test Pervaxis.Genesis.slnx

# Expected: 384/384 tests passing
```

---

## Completion Criteria

- [ ] All 40 public interface methods across 8 providers traced
- [ ] Build: 0 warnings, 0 errors
- [ ] Tests: 384/384 passing
- [ ] Consistent span naming (see pattern guide)
- [ ] All activities include tenant tags when applicable

---

## Estimated Time

**2-3 hours** for remaining 32 methods

**Per Provider Average:**
- Simple (3-4 methods): 20-30 minutes
- Complex (7 methods): 40-50 minutes

---

## Commit Message Template

```
feat(observability): complete tracing for all 8 Genesis providers

Completed Task 4.1.3: Observability Integration

## Summary
- Implemented distributed tracing for all 40 public methods across 8 Genesis AWS providers
- All providers use PervaxisActivitySource for consistent span creation
- Tenant context automatically enriched in all spans

## Providers Complete (8/8)
- ✅ Caching.AWS (7 methods)
- ✅ Messaging.AWS SQS (4 methods)
- ✅ Messaging.AWS SNS (3 methods)
- ✅ FileStorage.AWS (7 methods)
- ✅ Search.AWS (4 methods)
- ✅ Notifications.AWS (4 methods)
- ✅ Workflow.AWS (4 methods)
- ✅ AIAssistance.AWS (3 methods)
- ✅ Reporting.AWS (4 methods)

## Verification
- ✅ Build: 0 warnings, 0 errors
- ✅ Tests: 384/384 passing
- ✅ Pattern: Consistent across all providers

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

---

## Notes

- All activity operations are null-safe (`activity?.SetTag`)
- No performance impact when tracing disabled (activity returns null)
- Tenant tags only added when tenant context is resolved
- Error status propagated to tracing backend via `ActivityStatusCode.Error`

---

**Good luck! The pattern is straightforward - just apply it consistently to each method.** 🚀
