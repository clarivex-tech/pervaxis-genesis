# Resilience Implementation Pattern for Genesis Providers

## Task 4.1.4: Resilience Integration

This document describes the standard pattern for adding resilience policies (retry, circuit breaker, timeout) to all Genesis AWS providers using Pervaxis.Core.Resilience (Polly-based).

## Pattern Overview

Each provider's AWS SDK calls get wrapped with:
1. **Retry Policy** - handle transient AWS failures (throttling, network timeouts)
2. **Circuit Breaker** - prevent cascade failures when AWS is down
3. **Timeout Policy** - prevent hung operations
4. **Configurable** - each provider gets resilience options

## AWS Transient Errors to Handle

### Common AWS Transient Errors:
- **Throttling**: `RequestLimitExceeded`, `ThrottlingException`, `TooManyRequestsException`
- **Network**: `RequestTimeout`, `ServiceUnavailable`, `InternalServerError` (500, 503)
- **Connection**: `IOException`, `SocketException`, `HttpRequestException`

### Provider-Specific Errors:
- **S3**: `SlowDown`, `ServiceUnavailable`
- **SQS/SNS**: `ThrottlingException`, `InternalErrorException`
- **DynamoDB**: `ProvisionedThroughputExceededException`
- **All**: HTTP 429, 500, 502, 503, 504

## Implementation Strategy

### Option A: Core.Resilience Provides Ready-Made Policies (Recommended)
If `Pervaxis.Core.Resilience` already has AWS resilience policies built-in:
- Use `services.AddGenesisResilience()` from Core
- Configure per-provider via options
- No Polly code needed in Genesis

### Option B: Genesis Implements Custom Policies
If Core.Resilience only provides base Polly infrastructure:
- Create `Pervaxis.Genesis.Base/Resilience/` folder
- Implement AWS-specific resilience policies
- Wire into each provider's DI registration

## Resilience Options Pattern

Each provider gets resilience configuration:

```csharp
public class CachingOptions : GenesisOptionsBase
{
    // ... existing properties ...
    
    /// <summary>
    /// Resilience policy configuration for caching operations.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();
}

public class ResilienceOptions
{
    /// <summary>
    /// Enable resilience policies (retry, circuit breaker, timeout).
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Number of retry attempts for transient failures.
    /// Default: 3
    /// </summary>
    public int RetryCount { get; set; } = 3;
    
    /// <summary>
    /// Delay between retries in milliseconds (exponential backoff).
    /// Default: 1000ms (1s base, then 2s, 4s, 8s...)
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// Circuit breaker: number of consecutive failures before opening circuit.
    /// Default: 5
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;
    
    /// <summary>
    /// Circuit breaker: duration in seconds to keep circuit open.
    /// Default: 30 seconds
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
    
    /// <summary>
    /// Operation timeout in seconds.
    /// Default: 30 seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
```

## DI Registration Pattern

```csharp
public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddGenesisCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options
        services.Configure<CachingOptions>(configuration.GetSection("Genesis:Caching"));
        
        // Register provider
        services.AddSingleton<ICache, ElastiCacheProvider>();
        
        // Add resilience policies (if enabled in options)
        services.AddGenesisResiliencePolicies<ICache, ElastiCacheProvider>(
            policyBuilder => policyBuilder
                .WithRetry()
                .WithCircuitBreaker()
                .WithTimeout()
        );
        
        return services;
    }
}
```

## Provider Implementation Pattern

### Approach 1: Resilience via DI Interceptor (Preferred)
Use Core.Resilience's policy interceptor - no provider code changes needed:
- Policies applied via DI registration
- No business logic changes
- Centralized resilience configuration

### Approach 2: Explicit Policy Wrapping (If No DI Support)
Wrap each provider method:

```csharp
public class ElastiCacheProvider : ICache
{
    private readonly ResiliencePipeline _pipeline;
    
    public ElastiCacheProvider(
        IOptions<CachingOptions> options,
        IResiliencePipelineProvider pipelineProvider)
    {
        _pipeline = pipelineProvider.GetPipeline("caching");
    }
    
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(async token => 
        {
            // Existing implementation
            var db = GetDatabase();
            var value = await db.StringGetAsync(GetFullKey(key));
            return value.HasValue ? Deserialize<T>(value!) : default;
        }, ct);
    }
}
```

## Configuration Example

```json
{
  "Genesis": {
    "Caching": {
      "ConnectionString": "localhost:6379",
      "Database": 0,
      "Resilience": {
        "Enabled": true,
        "RetryCount": 3,
        "RetryDelayMs": 1000,
        "CircuitBreakerThreshold": 5,
        "CircuitBreakerDurationSeconds": 30,
        "TimeoutSeconds": 30
      }
    }
  }
}
```

## Testing Resilience

### Unit Tests:
- Test retry on transient errors (simulate throttling)
- Test circuit breaker opening after threshold
- Test circuit breaker closing after duration
- Test timeout enforcement
- Test resilience disabled (Enabled = false)

### Integration Tests:
- Test with real AWS throttling
- Test with network failures
- Test concurrent operations with circuit breaker

## Implementation Checklist

### Phase 1: Research Core.Resilience Capabilities
- [ ] Examine `Pervaxis.Core.Resilience` v1.1.1 API
- [ ] Determine if policies are pre-built or need custom implementation
- [ ] Check for AWS-specific error handling

### Phase 2: Add Resilience Options
- [ ] Add `ResilienceOptions` to each provider's Options class
- [ ] Update Options validation to include resilience settings
- [ ] Update README with resilience configuration examples

### Phase 3: Implement Resilience Policies
- [ ] Choose implementation approach (DI interceptor vs explicit wrapping)
- [ ] Add resilience to Caching.AWS (reference implementation)
- [ ] Apply pattern to remaining 7 providers

### Phase 4: Testing
- [ ] Write unit tests for resilience scenarios
- [ ] Verify all 390+ existing tests still pass
- [ ] Add integration tests (optional)

### Phase 5: Documentation
- [ ] Update each provider's README with resilience section
- [ ] Update TASKS.md to mark Task 4.1.4 complete
- [ ] Create migration guide if breaking changes

## Providers to Instrument (8 total)

1. **Caching.AWS (ElastiCache)** - Redis throttling, connection failures
2. **Messaging.AWS (SQS/SNS)** - Throttling, service unavailable
3. **FileStorage.AWS (S3)** - SlowDown, throttling, large file timeouts
4. **Search.AWS (OpenSearch)** - Query timeouts, throttling
5. **Notifications.AWS (SES/SNS)** - Rate limiting, bounces
6. **Workflow.AWS (Step Functions)** - Throttling, execution delays
7. **AIAssistance.AWS (Bedrock)** - Model throttling, long generation times
8. **Reporting.AWS (Metabase)** - Query timeouts, API rate limits

## Expected Outcome

After implementation:
- All Genesis providers handle transient AWS failures gracefully
- Configurable retry, circuit breaker, and timeout policies
- Forge-generated microservices get resilience out-of-the-box
- Zero code changes needed for resilience (configured via appsettings.json)

## Status

- ⏳ **Research Phase**: Pending
- ⏳ **Implementation**: Not started
- ⏳ **Testing**: Not started
- ⏳ **Documentation**: Not started
