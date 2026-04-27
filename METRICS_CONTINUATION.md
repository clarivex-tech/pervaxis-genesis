# Task 4.2 Metrics Integration - Continuation Guide

**Date:** 2026-04-26  
**Status:** 40% Complete (13/42 methods instrumented)  
**Branch:** `feature/metrics-integration` (not created yet - working on main)

---

## ✅ What's Complete

### 1. Core.Observability v1.3.0 Integration
- ✅ `Pervaxis.Genesis.Base.csproj` updated to Core.Observability v1.3.0
- ✅ `Pervaxis.Genesis.Base.csproj` updated to Core.Abstractions v1.3.0
- ✅ `nuget.config` updated with GitHub PAT (from environment variable)
- ✅ Solution builds: 0 warnings, 0 errors
- ✅ All 390 tests passing

### 2. Providers with Metrics Instrumentation

#### ✅ Caching.AWS (100% complete)
**File:** `src/Pervaxis.Genesis.Caching.AWS/Providers/ElastiCache/ElastiCacheProvider.cs`

**Metrics added:**
- `genesis.cache.operations` (Counter<long>) - all operations with tags
- `genesis.cache.hits` (Counter<long>) - cache hits
- `genesis.cache.misses` (Counter<long>) - cache misses  
- `genesis.cache.operation.duration` (Histogram<double>) - operation latency in ms

**Methods instrumented:** 7/7
1. ✅ GetAsync - records hits/misses
2. ✅ SetAsync - records success/failure
3. ✅ RemoveAsync - records success/not_found
4. ✅ ExistsAsync - records exists/not_found
5. ✅ GetManyAsync - records batch operations
6. ✅ SetManyAsync - records batch operations
7. ✅ RefreshAsync - records success/not_found

**Helper method:** `GetMetricTags(string operation, string result)` - adds tenant_id when available

**Tests:** ✅ 40/40 passing

---

#### 🟡 Messaging.AWS - SQS (100% complete, needs testing)
**File:** `src/Pervaxis.Genesis.Messaging.AWS/Providers/Sqs/SqsMessagingProvider.cs`

**Metrics added:**
- `genesis.messaging.operations` (Counter<long>)
- `genesis.messaging.messages.sent` (Counter<long>)
- `genesis.messaging.messages.received` (Counter<long>)
- `genesis.messaging.operation.duration` (Histogram<double>)

**Methods instrumented:** 4/4
1. ✅ PublishAsync - records sent messages
2. ✅ PublishBatchAsync - records batch sent count
3. ✅ ReceiveAsync - records received count
4. ✅ DeleteAsync - records delete operations

**Helper method:** `GetMetricTags(string operation, string result, string provider)` - includes "sqs" tag

**Tests:** ⏳ Need to run: 50 tests total

---

#### 🔴 Messaging.AWS - SNS (30% complete - NEEDS COMPLETION)
**File:** `src/Pervaxis.Genesis.Messaging.AWS/Providers/Sns/SnsMessagingProvider.cs`

**✅ What's done:**
- Added usings: `System.Diagnostics.Metrics`, `Pervaxis.Core.Observability.Metrics`
- Added metrics fields (same as SQS - shared static fields):
  - `genesis.messaging.operations`
  - `genesis.messaging.messages.sent`
  - `genesis.messaging.operation.duration`

**❌ What's missing:**
- [ ] Add `var stopwatch = Stopwatch.StartNew();` to 3 methods
- [ ] Add metrics recording to PublishAsync (line ~138)
- [ ] Add metrics recording to PublishBatchAsync (line ~185)
- [ ] Add metrics recording to SubscribeAsync (line ~291)
- [ ] Add `GetMetricTags(string operation, string result, string provider)` helper method (before closing brace)

**Pattern to follow (from SQS PublishAsync):**
```csharp
public async Task<string> PublishAsync<T>(...) {
    var stopwatch = Stopwatch.StartNew();  // ADD THIS
    using var activity = ...;
    // ... existing code ...
    try {
        // ... existing code ...
        // ADD BEFORE RETURN:
        var tags = GetMetricTags("publish", "success", "sns");
        _operationsCounter.Add(1, tags);
        _messagesSent.Add(1, tags);
        _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        return response.MessageId;
    }
    catch (Exception ex) {
        // ... existing logging ...
        // ADD BEFORE THROW:
        var tags = GetMetricTags("publish", "error", "sns");
        _operationsCounter.Add(1, tags);
        _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        throw;
    }
}
```

**Helper method to add at end of file (before closing brace):**
```csharp
private TagList GetMetricTags(string operation, string result, string provider)
{
    var tags = new TagList
    {
        { "operation", operation },
        { "result", result },
        { "provider", provider }
    };
    if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
    {
        tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
    }
    return tags;
}
```

---

## 🔴 Remaining Work (6 Providers, 29 Methods)

### FileStorage.AWS (7 methods)
**File:** `src/Pervaxis.Genesis.FileStorage.AWS/Providers/S3/S3FileStorageProvider.cs`

**Metrics to add:**
- `genesis.filestorage.operations` (Counter<long>)
- `genesis.filestorage.files.uploaded` (Counter<long>)
- `genesis.filestorage.upload.size` (Histogram<long>) - bytes
- `genesis.filestorage.operation.duration` (Histogram<double>)

**Methods:** UploadAsync, DownloadAsync, DeleteAsync, ExistsAsync, GetPresignedUrlAsync, GetMetadataAsync, ListAsync

---

### Search.AWS (4 methods)
**File:** `src/Pervaxis.Genesis.Search.AWS/Providers/OpenSearch/OpenSearchProvider.cs`

**Metrics to add:**
- `genesis.search.operations` (Counter<long>)
- `genesis.search.queries.executed` (Counter<long>)
- `genesis.search.operation.duration` (Histogram<double>)

**Methods:** IndexAsync, SearchAsync, DeleteAsync, BulkIndexAsync

---

### Notifications.AWS (4 methods)
**File:** `src/Pervaxis.Genesis.Notifications.AWS/Providers/AwsNotificationProvider.cs`

**Metrics to add:**
- `genesis.notifications.operations` (Counter<long>)
- `genesis.notifications.sent` (Counter<long>)
- `genesis.notifications.operation.duration` (Histogram<double>)

**Methods:** SendEmailAsync, SendTemplatedEmailAsync, SendSmsAsync, SendPushAsync

---

### Workflow.AWS (4 methods)
**File:** `src/Pervaxis.Genesis.Workflow.AWS/Providers/StepFunctionsWorkflowProvider.cs`

**Metrics to add:**
- `genesis.workflow.operations` (Counter<long>)
- `genesis.workflow.executions.started` (Counter<long>)
- `genesis.workflow.operation.duration` (Histogram<double>)

**Methods:** StartExecutionAsync, GetExecutionStatusAsync, GetExecutionOutputAsync, StopExecutionAsync

---

### AIAssistance.AWS (3 methods)
**File:** `src/Pervaxis.Genesis.AIAssistance.AWS/Providers/BedrockAIAssistantProvider.cs`

**Metrics to add:**
- `genesis.ai.operations` (Counter<long>)
- `genesis.ai.tokens.generated` (Counter<long>) - estimate from response
- `genesis.ai.operation.duration` (Histogram<double>)

**Methods:** GenerateTextAsync, GenerateEmbeddingAsync, GenerateImageAsync

---

### Reporting.AWS (4 methods)
**File:** `src/Pervaxis.Genesis.Reporting.AWS/Providers/MetabaseReportingProvider.cs`

**Metrics to add:**
- `genesis.reporting.operations` (Counter<long>)
- `genesis.reporting.queries.executed` (Counter<long>)
- `genesis.reporting.operation.duration` (Histogram<double>)

**Methods:** ExecuteQueryAsync, GetDashboardAsync, CreateDashboardAsync, ExportReportAsync

---

## 📋 Quick Start Commands for Next Session

### 1. Check Current Status
```bash
cd C:\Anand\Clarivex\Pervaxis\Code\Genesis

# Verify Core v1.3.0 is installed
grep -A 2 "Pervaxis.Core.Observability" src/Pervaxis.Genesis.Base/Pervaxis.Genesis.Base.csproj

# Check build status
dotnet build Pervaxis.Genesis.slnx --configuration Release

# Check test status
dotnet test Pervaxis.Genesis.slnx --configuration Release --no-build --verbosity minimal
```

### 2. Resume from SNS
```bash
# Open SNS file
code src/Pervaxis.Genesis.Messaging.AWS/Providers/Sns/SnsMessagingProvider.cs

# Find methods to instrument (lines ~138, ~185, ~291)
grep -n "public async Task" src/Pervaxis.Genesis.Messaging.AWS/Providers/Sns/SnsMessagingProvider.cs
```

### 3. Test After Each Provider
```bash
# Build specific provider
dotnet build src/Pervaxis.Genesis.Messaging.AWS/Pervaxis.Genesis.Messaging.AWS.csproj --configuration Release

# Run tests
dotnet test tests/Pervaxis.Genesis.Messaging.AWS.Tests/Pervaxis.Genesis.Messaging.AWS.Tests.csproj --configuration Release --verbosity minimal
```

### 4. Verify All Tests at End
```bash
dotnet test Pervaxis.Genesis.slnx --configuration Release --verbosity minimal | grep "Passed\|Failed"
```

---

## 🎯 Implementation Workflow (Per Provider)

1. **Open provider file**
2. **Add usings** (if not present):
   - `using System.Diagnostics.Metrics;`
   - `using Pervaxis.Core.Observability.Metrics;`
3. **Add static metrics fields** (after existing fields)
4. **Add stopwatch to each method** (`var stopwatch = Stopwatch.StartNew();`)
5. **Add metrics recording** before returns and in catch blocks
6. **Add GetMetricTags helper method** (before closing brace)
7. **Build & test**
8. **Move to next provider**

---

## 🚀 Completion Checklist

- [x] Caching.AWS - 7/7 methods ✅
- [x] Messaging.AWS (SQS) - 4/4 methods ✅  
- [ ] Messaging.AWS (SNS) - 3/3 methods (70% done)
- [ ] FileStorage.AWS - 7/7 methods
- [ ] Search.AWS - 4/4 methods
- [ ] Notifications.AWS - 4/4 methods
- [ ] Workflow.AWS - 4/4 methods
- [ ] AIAssistance.AWS - 3/3 methods
- [ ] Reporting.AWS - 4/4 methods
- [ ] All 390 tests passing
- [ ] Create `.claude/guides/METRICS_PATTERN.md`
- [ ] Update TASKS.md Task 4.2 to complete
- [ ] Create PR

---

**Estimated Time Remaining:** 2-3 hours

**Next Step:** Complete SNS (3 methods, 15 minutes), then move to FileStorage.AWS
