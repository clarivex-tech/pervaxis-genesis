# Phase 4 Implementation Plan: Cross-Cutting Concerns

## Overview

Phase 4 adds enterprise-grade cross-cutting concerns to all Genesis providers:
- Multi-tenancy support
- Observability (structured logging, distributed tracing, metrics)
- Resilience (retry policies, circuit breakers, timeouts)

## Task 4.1: Pervaxis.Core Integration

### Prerequisites Check

Before starting, verify Pervaxis.Core NuGet packages are available:
- [ ] Pervaxis.Core.MultiTenancy
- [ ] Pervaxis.Core.Observability (Serilog + OpenTelemetry)
- [ ] Pervaxis.Core.Resilience (Polly integration)

**If packages don't exist yet:** Defer Phase 4 until Pervaxis.Core is published, OR implement inline and refactor later.

### Decision: Keep Genesis Exceptions

✅ **Keep** `GenesisException` and `GenesisConfigurationException` in Genesis.Base
- They are provider-specific and add `ProviderName` context
- Not generic enough for Pervaxis.Core
- Follow exception hierarchy: `GenesisException : Exception`

### 4.1.1: Add Multi-Tenancy Support

**Goal:** All providers support tenant isolation via `ITenantContext`

#### Changes Required:

1. **Add Pervaxis.Core.MultiTenancy reference**
   ```xml
   <PackageReference Include="Pervaxis.Core.MultiTenancy" Version="1.0.0" />
   ```

2. **Update all provider constructors** to accept `ITenantContext`
   ```csharp
   public ElastiCacheProvider(
       IOptions<CachingOptions> options,
       ILogger<ElastiCacheProvider> logger,
       ITenantContext tenantContext)  // NEW
   ```

3. **Add tenant isolation to operations**
   - Caching: Prefix keys with `{TenantId}:`
   - Messaging: Add tenant ID to message metadata
   - FileStorage: Prefix S3 keys with `{TenantId}/`
   - Search: Use tenant-specific index names
   - Notifications: Tag tenant ID in SES/SNS
   - Workflow: Pass tenant ID in Step Functions input
   - AIAssistance: Add tenant context to Bedrock calls
   - Reporting: Filter queries by tenant ID

4. **Update Options classes** with tenant isolation settings
   ```csharp
   public bool EnableTenantIsolation { get; set; } = true;
   public string TenantIdPrefix { get; set; } = "tenant";
   ```

#### Testing:
- [ ] Unit tests with mock ITenantContext
- [ ] Verify tenant isolation in keys/identifiers
- [ ] Test multi-tenant scenarios

---

### 4.1.2: Add Observability (Serilog + OpenTelemetry)

**Goal:** Structured logging, distributed tracing, and metrics for all operations

#### Changes Required:

1. **Add Pervaxis.Core.Observability reference**
   ```xml
   <PackageReference Include="Pervaxis.Core.Observability" Version="1.0.0" />
   ```

2. **Create ActivitySource for each provider**
   ```csharp
   private static readonly ActivitySource ActivitySource = new(
       "Pervaxis.Genesis.Caching.AWS",
       "1.0.0");
   ```

3. **Add tracing to all operations**
   ```csharp
   public async Task<string?> GetAsync(string key, CancellationToken ct)
   {
       using var activity = ActivitySource.StartActivity("Cache.Get");
       activity?.SetTag("cache.key", key);
       activity?.SetTag("tenant.id", _tenantContext.TenantId);
       
       try
       {
           var value = await _cache.StringGetAsync(key);
           activity?.SetTag("cache.hit", value.HasValue);
           return value;
       }
       catch (Exception ex)
       {
           activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
           throw;
       }
   }
   ```

4. **Add structured logging with enrichment**
   ```csharp
   _logger.LogInformation(
       "Cache operation completed: {Operation} {Key} {TenantId} {Duration}ms",
       "Get",
       key,
       _tenantContext.TenantId,
       activity?.Duration.TotalMilliseconds);
   ```

5. **Add metrics**
   ```csharp
   private static readonly Counter<long> CacheOperations = Meter.CreateCounter<long>(
       "genesis.cache.operations",
       "operations",
       "Number of cache operations");
   
   private static readonly Histogram<double> CacheLatency = Meter.CreateHistogram<double>(
       "genesis.cache.latency",
       "ms",
       "Cache operation latency");
   ```

#### Metrics to Track:

- **Caching:** Hit rate, miss rate, operation latency
- **Messaging:** Messages published, messages received, queue depth
- **FileStorage:** Upload size, download size, operation latency
- **Search:** Query latency, index size, document count
- **Notifications:** Emails sent, SMS sent, push notifications sent
- **Workflow:** Executions started, execution duration, failures
- **AIAssistance:** Tokens used, model latency, error rate
- **Reporting:** Query execution time, result size

#### Testing:
- [ ] Verify Activity propagation in tests
- [ ] Check log enrichment with tenant ID
- [ ] Validate metrics are emitted

---

### 4.1.3: Add Resilience (Polly Policies)

**Goal:** Retry transient failures, circuit breakers for external dependencies

#### Changes Required:

1. **Add Pervaxis.Core.Resilience reference**
   ```xml
   <PackageReference Include="Pervaxis.Core.Resilience" Version="1.0.0" />
   ```

2. **Update Options with resilience settings**
   ```csharp
   public class CachingOptions : GenesisOptionsBase
   {
       // Existing properties...
       
       public ResilienceOptions Resilience { get; set; } = new();
   }
   
   public class ResilienceOptions
   {
       public int MaxRetries { get; set; } = 3;
       public int RetryDelayMs { get; set; } = 100;
       public int CircuitBreakerThreshold { get; set; } = 5;
       public int CircuitBreakerDurationSeconds { get; set; } = 30;
       public int TimeoutSeconds { get; set; } = 30;
   }
   ```

3. **Wrap AWS SDK calls with Polly policies**
   ```csharp
   private readonly ResiliencePipeline _pipeline;
   
   public ElastiCacheProvider(...)
   {
       _pipeline = new ResiliencePipelineBuilder()
           .AddRetry(new RetryStrategyOptions
           {
               MaxRetryAttempts = options.Value.Resilience.MaxRetries,
               Delay = TimeSpan.FromMilliseconds(options.Value.Resilience.RetryDelayMs),
               BackoffType = DelayBackoffType.Exponential,
               ShouldHandle = new PredicateBuilder()
                   .Handle<RedisConnectionException>()
                   .Handle<RedisTimeoutException>()
           })
           .AddCircuitBreaker(new CircuitBreakerStrategyOptions
           {
               FailureRatio = 0.5,
               SamplingDuration = TimeSpan.FromSeconds(10),
               BreakDuration = TimeSpan.FromSeconds(options.Value.Resilience.CircuitBreakerDurationSeconds)
           })
           .AddTimeout(TimeSpan.FromSeconds(options.Value.Resilience.TimeoutSeconds))
           .Build();
   }
   
   public async Task<string?> GetAsync(string key, CancellationToken ct)
   {
       return await _pipeline.ExecuteAsync(async ct =>
       {
           return await _cache.StringGetAsync(key);
       }, ct);
   }
   ```

#### AWS SDK Transient Errors to Retry:

- **All services:** 
  - `RequestLimitExceeded` (throttling)
  - `ServiceUnavailableException`
  - `InternalServerError`
  - Network errors (connection timeout, socket exceptions)

- **ElastiCache/Redis:**
  - `RedisConnectionException`
  - `RedisTimeoutException`

- **S3:**
  - `SlowDown` (too many requests)
  - `RequestTimeout`

- **SQS/SNS:**
  - `OverLimit`

- **OpenSearch:**
  - `429 Too Many Requests`

#### Testing:
- [ ] Test retry on transient failures
- [ ] Test circuit breaker opening after threshold
- [ ] Test timeout enforcement
- [ ] Verify exponential backoff

---

## Task 4.2: Observability Integration

See 4.1.2 above - implemented as part of Pervaxis.Core.Observability integration.

## Task 4.3: Resilience Policies

See 4.1.3 above - implemented as part of Pervaxis.Core.Resilience integration.

## Task 4.4: Multi-Tenancy Support

See 4.1.1 above - implemented as part of Pervaxis.Core.MultiTenancy integration.

---

## Implementation Order

### Step 1: Check Pervaxis.Core Package Availability
```bash
# Check if packages exist on NuGet/GitHub Packages
dotnet nuget list source
dotnet search Pervaxis.Core.MultiTenancy
```

### Step 2: Multi-Tenancy (Easiest, No External Dependencies)
1. Define ITenantContext interface in Genesis.Base (if Core doesn't exist)
2. Update all 8 providers with tenant context
3. Add tenant isolation to operations
4. Write tests

### Step 3: Observability (Moderate Complexity)
1. Add ActivitySource to each provider
2. Add tracing to all operations
3. Add structured logging with enrichment
4. Define and emit metrics
5. Write tests

### Step 4: Resilience (Most Complex)
1. Add Polly packages
2. Define resilience options
3. Create resilience pipelines for each provider
4. Wrap AWS SDK calls
5. Handle provider-specific transient errors
6. Write tests

---

## Testing Strategy

### Unit Tests:
- Mock ITenantContext, Activity, ILogger
- Verify tenant isolation logic
- Verify resilience policy invocation
- Verify metrics emission

### Integration Tests:
- Test against LocalStack with resilience enabled
- Inject faults to test retry/circuit breaker
- Verify traces in test output

---

## Breaking Changes

⚠️ **This will be a BREAKING CHANGE** - all providers will require ITenantContext in constructors.

**Migration Path:**
1. Release v1.1.0 with opt-in tenant context (nullable parameter)
2. Release v2.0.0 with required tenant context

**For v1.1.0 (Non-Breaking):**
```csharp
public ElastiCacheProvider(
    IOptions<CachingOptions> options,
    ILogger<ElastiCacheProvider> logger,
    ITenantContext? tenantContext = null)  // Optional
{
    _tenantContext = tenantContext ?? new DefaultTenantContext();
}
```

---

## Completion Criteria

- [ ] All 8 providers support ITenantContext
- [ ] All 8 providers have ActivitySource and tracing
- [ ] All 8 providers emit metrics
- [ ] All 8 providers have resilience policies
- [ ] Tests verify tenant isolation
- [ ] Tests verify observability (logs, traces, metrics)
- [ ] Tests verify resilience (retry, circuit breaker, timeout)
- [ ] Documentation updated with new features
- [ ] Migration guide created for v2.0.0

---

*Last Updated: 2026-04-24*
