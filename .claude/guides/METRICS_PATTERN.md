# Metrics Pattern Guide

**Last Updated:** 2026-04-27  
**Status:** Production-Ready ✅  
**Coverage:** All 8 Genesis providers (390/390 tests passing)

---

## Overview

This guide documents the OpenTelemetry metrics instrumentation pattern used across all Genesis providers. Metrics provide quantitative measurements of system behavior, complementing distributed tracing and logging to form complete observability coverage.

**Observability Stack:**
- ✅ **Logging** - What happened (events, errors, state changes)
- ✅ **Tracing** - How it happened (request flow, latency, dependencies)
- ✅ **Metrics** - How much happened (counts, rates, distributions)

---

## Core Components

### PervaxisMeter (from Core.Observability v1.3.0)

All metrics are created using `PervaxisMeter` from `Pervaxis.Core.Observability.Metrics`:

```csharp
using System.Diagnostics.Metrics;
using Pervaxis.Core.Observability.Metrics;

// Counter - monotonically increasing value
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.caching.operations",
    "1",
    "Total number of caching operations");

// Histogram - distribution of values
private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.caching.operation.duration",
    "ms",
    "Duration of caching operations in milliseconds");
```

### Metric Types Used

| Type | Purpose | Example |
|------|---------|---------|
| **Counter** | Cumulative count of events | Operations executed, messages sent, files uploaded |
| **Histogram** | Distribution of values | Operation duration, file size, query latency |

**Note:** We use `static readonly` fields for metrics to ensure they're created once and shared across all provider instances.

---

## Implementation Pattern

### Step 1: Add Required Usings

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pervaxis.Core.Observability.Metrics;
```

### Step 2: Define Static Metrics Fields

Place after existing fields (after `_logger`, `_tenantContext`, etc.):

```csharp
// Metrics
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.{module}.operations",
    "1",
    "Total number of operations");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.{module}.operation.duration",
    "ms",
    "Duration of operations in milliseconds");

// Add module-specific metrics (see provider-specific patterns below)
```

### Step 3: Add GetMetricTags Helper Method

Place before the closing brace of the class:

```csharp
private TagList GetMetricTags(string operation, string result)
{
    var tags = new TagList
    {
        { "operation", operation },
        { "result", result }
    };
    
    if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
    {
        tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
    }
    
    return tags;
}
```

**Tag Dimensions:**
- `operation` - Method name (e.g., "get", "set", "publish", "upload")
- `result` - Outcome ("success", "error", "not_found", "miss")
- `tenant_id` - Tenant identifier (when multi-tenancy is enabled)

### Step 4: Instrument Each Method

**Pattern:**

```csharp
public async Task<string> MethodAsync(/* parameters */)
{
    var stopwatch = Stopwatch.StartNew();
    using var activity = PervaxisActivitySource.StartActivity("...");
    
    // ... existing validation and tracing code ...
    
    try
    {
        // ... existing business logic ...
        
        var tags = GetMetricTags("operation_name", "success");
        _operationsCounter.Add(1, tags);
        _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        
        return result;
    }
    catch (Exception ex)
    {
        // ... existing error handling ...
        
        var tags = GetMetricTags("operation_name", "error");
        _operationsCounter.Add(1, tags);
        _operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        
        throw new GenesisException(...);
    }
}
```

**Key Points:**
- ✅ Add `Stopwatch.StartNew()` at method entry
- ✅ Record metrics **before** returning or throwing
- ✅ Use consistent operation names (lowercase, underscores)
- ✅ Record both success and error metrics
- ✅ Record duration for all code paths

---

## Provider-Specific Patterns

### 1. Caching (ElastiCacheProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.caching.operations", "1", "Total number of caching operations");

private static readonly Counter<long> _cacheHits = PervaxisMeter.CreateCounter<long>(
    "genesis.caching.hits", "1", "Total number of cache hits");

private static readonly Counter<long> _cacheMisses = PervaxisMeter.CreateCounter<long>(
    "genesis.caching.misses", "1", "Total number of cache misses");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.caching.operation.duration", "ms", "Duration of caching operations in milliseconds");
```

**Operations:** `get`, `set`, `remove`, `exists`, `get_many`, `set_many`, `refresh`  
**Results:** `success`, `error`, `hit`, `miss`

**Example:**
```csharp
// Cache hit
var tags = GetMetricTags("get", "hit");
_operationsCounter.Add(1, tags);
_cacheHits.Add(1, tags);

// Cache miss
var tags = GetMetricTags("get", "miss");
_operationsCounter.Add(1, tags);
_cacheMisses.Add(1, tags);
```

---

### 2. Messaging (SQS + SNS)

**Metrics (shared across SQS and SNS):**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.messaging.operations", "1", "Total number of messaging operations");

private static readonly Counter<long> _messagesSent = PervaxisMeter.CreateCounter<long>(
    "genesis.messaging.messages.sent", "1", "Total number of messages sent");

private static readonly Counter<long> _messagesReceived = PervaxisMeter.CreateCounter<long>(
    "genesis.messaging.messages.received", "1", "Total number of messages received");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.messaging.operation.duration", "ms", "Duration of messaging operations in milliseconds");
```

**Helper Method (3-parameter version for provider distinction):**
```csharp
private TagList GetMetricTags(string operation, string result, string provider)
{
    var tags = new TagList
    {
        { "operation", operation },
        { "result", result },
        { "provider", provider } // "sqs" or "sns"
    };
    
    if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
    {
        tags.Add("tenant_id", _tenantContext.TenantId.Value.ToString());
    }
    
    return tags;
}
```

**Operations (SQS):** `publish`, `publish_batch`, `receive`, `delete`  
**Operations (SNS):** `publish`, `publish_batch`, `subscribe`  
**Results:** `success`, `error`

---

### 3. File Storage (S3FileStorageProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.filestorage.operations", "1", "Total number of file storage operations");

private static readonly Counter<long> _filesUploaded = PervaxisMeter.CreateCounter<long>(
    "genesis.filestorage.files.uploaded", "1", "Total number of files uploaded");

private static readonly Histogram<long> _uploadSize = PervaxisMeter.CreateHistogram<long>(
    "genesis.filestorage.upload.size", "bytes", "Size of uploaded files in bytes");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.filestorage.operation.duration", "ms", "Duration of file storage operations in milliseconds");
```

**Operations:** `upload`, `download`, `delete`, `exists`, `get_presigned_url`, `get_metadata`, `list`  
**Results:** `success`, `error`, `not_found`

**Example:**
```csharp
// Upload success
var tags = GetMetricTags("upload", "success");
_operationsCounter.Add(1, tags);
_filesUploaded.Add(1, tags);
_uploadSize.Record(contentLength, tags);
_operationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
```

---

### 4. Search (OpenSearchProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.search.operations", "1", "Total number of search operations");

private static readonly Counter<long> _queriesExecuted = PervaxisMeter.CreateCounter<long>(
    "genesis.search.queries.executed", "1", "Total number of search queries executed");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.search.operation.duration", "ms", "Duration of search operations in milliseconds");
```

**Operations:** `index`, `search`, `delete`, `bulk_index`  
**Results:** `success`, `error`

---

### 5. Notifications (AwsNotificationProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.notifications.operations", "1", "Total number of notification operations");

private static readonly Counter<long> _notificationsSent = PervaxisMeter.CreateCounter<long>(
    "genesis.notifications.sent", "1", "Total number of notifications sent");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.notifications.operation.duration", "ms", "Duration of notification operations in milliseconds");
```

**Operations:** `send_email`, `send_templated_email`, `send_sms`, `send_push`  
**Results:** `success`, `error`

---

### 6. Workflow (StepFunctionsWorkflowProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.workflow.operations", "1", "Total number of workflow operations");

private static readonly Counter<long> _executionsStarted = PervaxisMeter.CreateCounter<long>(
    "genesis.workflow.executions.started", "1", "Total number of workflow executions started");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.workflow.operation.duration", "ms", "Duration of workflow operations in milliseconds");
```

**Operations:** `start_execution`, `get_execution_status`, `get_execution_output`, `stop_execution`  
**Results:** `success`, `error`

---

### 7. AI Assistance (BedrockAIAssistantProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.aiassistance.operations", "1", "Total number of AI operations");

private static readonly Counter<long> _tokensGenerated = PervaxisMeter.CreateCounter<long>(
    "genesis.aiassistance.tokens.generated", "1", "Total number of tokens generated (estimated)");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.aiassistance.operation.duration", "ms", "Duration of AI operations in milliseconds");
```

**Operations:** `generate_text`, `generate_embedding`, `generate_image`  
**Results:** `success`, `error`

**Note:** Token counting is estimated based on response content length (approximate).

---

### 8. Reporting (MetabaseReportingProvider)

**Metrics:**
```csharp
private static readonly Counter<long> _operationsCounter = PervaxisMeter.CreateCounter<long>(
    "genesis.reporting.operations", "1", "Total number of reporting operations");

private static readonly Counter<long> _queriesExecuted = PervaxisMeter.CreateCounter<long>(
    "genesis.reporting.queries.executed", "1", "Total number of queries executed");

private static readonly Histogram<double> _operationDuration = PervaxisMeter.CreateHistogram<double>(
    "genesis.reporting.operation.duration", "ms", "Duration of reporting operations in milliseconds");
```

**Operations:** `execute_query`, `get_dashboard`, `create_dashboard`, `export_report`  
**Results:** `success`, `error`

---

## Metric Naming Conventions

### Structure

```
genesis.{module}.{metric_type}.{detail}
```

**Examples:**
- `genesis.caching.operations` - Counter for all caching operations
- `genesis.caching.hits` - Counter for cache hits
- `genesis.messaging.messages.sent` - Counter for messages sent
- `genesis.filestorage.upload.size` - Histogram for upload sizes
- `genesis.search.operation.duration` - Histogram for search operation latency

### Rules

1. **Lowercase with dots** - Use lowercase letters and dots as separators
2. **Module prefix** - Always start with `genesis.{module}`
3. **Descriptive names** - Name should indicate what is being measured
4. **Consistent units** - Use standard units (ms, bytes, 1 for counts)

---

## Configuration & Consumption

### OpenTelemetry Setup

Genesis providers emit metrics through `PervaxisMeter`, which integrates with OpenTelemetry:

```csharp
// Program.cs or Startup.cs
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Pervaxis.Genesis.*") // All Genesis providers
        .AddPrometheusExporter()        // Prometheus endpoint
        .AddOtlpExporter());            // OTLP for collectors
```

### Prometheus Export

Metrics are exposed at `/metrics` endpoint:

```
# HELP genesis_caching_operations Total number of caching operations
# TYPE genesis_caching_operations counter
genesis_caching_operations{operation="get",result="hit",tenant_id="tenant-123"} 1523

# HELP genesis_caching_operation_duration Duration of caching operations in milliseconds
# TYPE genesis_caching_operation_duration histogram
genesis_caching_operation_duration_bucket{operation="get",result="hit",le="10"} 1200
genesis_caching_operation_duration_bucket{operation="get",result="hit",le="50"} 1480
genesis_caching_operation_duration_bucket{operation="get",result="hit",le="+Inf"} 1523
genesis_caching_operation_duration_sum{operation="get",result="hit"} 18745.3
genesis_caching_operation_duration_count{operation="get",result="hit"} 1523
```

### Grafana Dashboards

**Example PromQL Queries:**

```promql
# Operation rate (operations per second)
rate(genesis_caching_operations[5m])

# Cache hit rate
rate(genesis_caching_hits[5m]) / rate(genesis_caching_operations[5m]) * 100

# P95 latency
histogram_quantile(0.95, rate(genesis_caching_operation_duration_bucket[5m]))

# Error rate
rate(genesis_caching_operations{result="error"}[5m])

# Operations by tenant
sum by (tenant_id) (rate(genesis_caching_operations[5m]))
```

---

## Multi-Tenancy Support

Metrics automatically include `tenant_id` tag when:
1. `EnableTenantIsolation = true` in provider options
2. `ITenantContext` is resolved (tenant is identified)

**Query by tenant:**
```promql
genesis_caching_operations{tenant_id="tenant-123"}
```

**Aggregate across tenants:**
```promql
sum(genesis_caching_operations)
```

---

## Testing Considerations

### Metrics Don't Affect Test Outcomes

Metrics recording does **not** impact test results:
- Metrics are fire-and-forget (no exceptions on recording failure)
- Tests validate business logic, not metric recording
- All 390 tests pass with metrics instrumentation in place

### Verifying Metrics in Tests (Optional)

If you need to verify metrics are recorded correctly:

```csharp
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;

[Fact]
public async Task GetAsync_RecordsMetrics()
{
    // Arrange
    var meterProvider = Sdk.CreateMeterProviderBuilder()
        .AddMeter("Pervaxis.Genesis.Caching")
        .AddInMemoryExporter(exportedItems)
        .Build();
    
    // Act
    await provider.GetAsync("test-key");
    
    // Assert
    var metric = exportedItems
        .First(m => m.Name == "genesis.caching.operations");
    Assert.Equal(1, metric.LongSum);
}
```

---

## Best Practices

### ✅ DO

- ✅ Use `static readonly` fields for metric instruments
- ✅ Record metrics for **all** code paths (success and error)
- ✅ Use consistent operation names across methods
- ✅ Add `tenant_id` tag when multi-tenancy is enabled
- ✅ Record duration for all operations using `Stopwatch`
- ✅ Use lowercase with underscores for operation names
- ✅ Use descriptive metric names with proper units

### ❌ DON'T

- ❌ Don't create new metric instruments per operation (use static fields)
- ❌ Don't forget to record metrics in catch blocks
- ❌ Don't use high-cardinality tags (e.g., user IDs, message IDs)
- ❌ Don't record sensitive data in metric tags
- ❌ Don't throw exceptions from metric recording
- ❌ Don't skip duration recording for fast operations

---

## Troubleshooting

### Metrics Not Appearing

**Check OpenTelemetry configuration:**
```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Pervaxis.Genesis.*"));
```

**Verify PervaxisMeter is initialized** (should be automatic via Core.Observability).

### High Cardinality Issues

**Problem:** Too many unique tag combinations causing performance issues.

**Solution:**
- Avoid user IDs, message IDs, or timestamps in tags
- Only use `tenant_id`, `operation`, and `result`
- Consider aggregating before recording

### Missing Tenant Tags

**Check tenant context:**
```csharp
if (_tenantContext?.IsResolved == true)
{
    // Tenant ID available: _tenantContext.TenantId.Value
}
```

Ensure `ITenantContext` is registered in DI and tenant is resolved.

---

## Summary

**Metrics Coverage:**
- ✅ **8 providers** fully instrumented
- ✅ **39 methods** with metrics recording
- ✅ **390/390 tests** passing
- ✅ **Consistent pattern** across all providers

**Key Metrics by Provider:**

| Provider | Operations | Specific Metrics |
|----------|------------|------------------|
| Caching | 7 | Cache hits/misses |
| Messaging (SQS/SNS) | 7 | Messages sent/received |
| File Storage | 7 | Files uploaded, upload size |
| Search | 4 | Queries executed |
| Notifications | 4 | Notifications sent |
| Workflow | 4 | Executions started |
| AI Assistance | 3 | Tokens generated |
| Reporting | 4 | Queries executed |

**Complete Observability:**
- ✅ **Logging** - Structured logs via ILogger
- ✅ **Tracing** - Distributed traces via PervaxisActivitySource
- ✅ **Metrics** - Quantitative metrics via PervaxisMeter

---

*Pervaxis Platform · Clarivex Technologies · Genesis Edition*  
*Metrics Pattern Guide · v1.0 · 2026-04-27*
