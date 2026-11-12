# Implementation Plan: Genesis Transactional Logging

## Overview

Implement the `Pervaxis.Genesis.TransactionalLogging.AWS` module that provides audit-grade, per-request operation logging across all Genesis providers. The module automatically captures provider calls via interceptors (implicit), exposes `ITransactionLog` for business-level entries (explicit), and persists to DynamoDB (hot) with S3 overflow (large records). Fully configurable per service, per endpoint, per provider.

## Prerequisites

- [x] `ITransactionLog` interface published in `Pervaxis.Core.Abstractions` NuGet (v1.5.0) ✅
- [x] `TransactionLogEntry` record published in `Pervaxis.Core.Abstractions` NuGet (v1.5.0) ✅
- [x] `TransactionLogStatus` enum published in `Pervaxis.Core.Abstractions` NuGet (v1.5.0) ✅
- [x] `TransactionLogQuery` and `TransactionLogQueryResult` records published in `Pervaxis.Core.Abstractions` NuGet (v1.5.0) ✅
- [ ] Update `Pervaxis.Core.Abstractions` from v1.4.0 → v1.5.0 in `src/Pervaxis.Genesis.Base/Pervaxis.Genesis.Base.csproj` (do this when starting task 1.1)

**Release:** https://github.com/clarivex-tech/pervaxis-core/releases/tag/v1.5.0

**Interface methods available:**
- `BeginTransactionAsync(tenantId, correlationId?, idempotencyKey?)` → returns transaction ID
- `RecordEntryAsync(transactionId, entry)` → logs a provider operation
- `CompleteTransactionAsync(transactionId, status, summary?)` → finalizes the log
- `GetByTransactionIdAsync(transactionId)` → single lookup
- `QueryAsync(tenantId, correlationId?, rangeStart?, rangeEnd?, maxResults)` → filtered query

## Tasks

- [ ] 1. Set up project structure
  - [ ] 1.1 Create the `Pervaxis.Genesis.TransactionalLogging.AWS` project with folder structure and .csproj
    - Create `src/Pervaxis.Genesis.TransactionalLogging.AWS/` with subdirectories: `Context/`, `Extensions/`, `Interceptors/`, `Middleware/`, `Attributes/`, `Options/`, `Sanitization/`, `Services/`, `Storage/`, `Diagnostics/`
    - Create `.csproj` targeting `net10.0` with package references: `AWSSDK.DynamoDBv2`, `AWSSDK.S3`, `Scrutor 5.0.1`, and project reference to `Pervaxis.Genesis.Base`
    - Add `InternalsVisibleTo` for `Pervaxis.Genesis.TransactionalLogging.AWS.Tests`
    - Add the project to the solution file `Pervaxis.Genesis.slnx`
    - _Requirements: 1.1, 1.2_

  - [ ] 1.2 Create the test project `Pervaxis.Genesis.TransactionalLogging.AWS.Tests`
    - Create `tests/Pervaxis.Genesis.TransactionalLogging.AWS.Tests/` with subdirectories: `Unit/Options/`, `Unit/Context/`, `Unit/Interceptors/`, `Unit/Middleware/`, `Unit/Sanitization/`, `Unit/Services/`, `Unit/Extensions/`, `Properties/`, `Integration/`
    - Create `.csproj` targeting `net10.0` with references to xUnit, NSubstitute, FsCheck.Xunit, and the main project
    - Add the test project to the solution file
    - _Requirements: All (testing infrastructure)_

- [ ] 2. Implement Options and Validation
  - [ ] 2.1 Create `TransactionalLoggingOptions` class in `Options/TransactionalLoggingOptions.cs`
    - Extend `GenesisOptionsBase`
    - Include all properties: `Enabled`, `ImplicitCapture`, `CaptureProviders`, `ExcludeProviders`, `ExcludeOperations`, `MinimumDurationMs`, `TableName`, `BucketName`, `HotRetentionDays`, `ColdRetentionDays`, `EnableTenantIsolation`, `SanitizeParameters`, `SensitiveKeys`, `SuppressRoutes`, `EnableObjectLock`, `Resilience`
    - Implement `Validate()` with all validation rules
    - Add full XML documentation
    - _Requirements: 2.1–2.19_

  - [ ]* 2.2 Write property tests for options validation
    - **Property 1: Options validation rejects invalid retention ranges**
    - **Property 2: Options validation rejects empty TableName when enabled**
    - **Validates: Requirements 2.15, 2.16, 2.17, 2.18, 2.19**

  - [ ]* 2.3 Write unit tests for options validation
    - Test all boundary values for HotRetentionDays (0, 1, 365, 366)
    - Test all boundary values for ColdRetentionDays (29, 30, 3650, 3651)
    - Test ColdRetentionDays < HotRetentionDays fails
    - Test TableName null/empty with Enabled=true, UseLocalEmulator=false
    - Test Resilience sub-validation propagation
    - _Requirements: 2.15–2.19_

- [ ] 3. Implement TransactionContext and Accessor
  - [ ] 3.1 Create `TransactionContext` class in `Context/TransactionContext.cs`
    - Thread-safe entry accumulation via `ConcurrentBag<TransactionLogEntry>`
    - Business key support via `ConcurrentDictionary<string, string>`
    - `Finalize()` method for setting end state
    - All properties as documented in design
    - _Requirements: 6.1, 6.2, 6.3, 6.5, 6.6, 6.7, 6.8_

  - [ ] 3.2 Create `TransactionContextAccessor` class in `Context/TransactionContextAccessor.cs`
    - AsyncLocal-based scoped accessor for TransactionContext
    - `Current` property (get/set) for middleware and interceptors to share context
    - _Requirements: 6.1, 3.7_

  - [ ]* 3.3 Write unit tests for TransactionContext
    - Test entry accumulation (thread-safety with concurrent additions)
    - Test Finalize sets all fields correctly
    - Test business key addition
    - Test TransactionId format matches `txn_{Guid:N}`
    - _Requirements: 6.1–6.8_

- [ ] 4. Implement Parameter Sanitization
  - [ ] 4.1 Create `ParameterSanitizer` class in `Sanitization/ParameterSanitizer.cs`
    - Default sensitive patterns: password, secret, token, key, credential, auth, connectionstring, apikey, private
    - Custom patterns from options
    - Case-insensitive contains matching
    - Replace values with "[REDACTED]"
    - Handle null/empty parameters gracefully
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_

  - [ ]* 4.2 Write property tests for sanitization
    - **Property 5: Parameter sanitization correctness**
    - **Validates: Requirements 10.1, 10.2, 10.3**

  - [ ]* 4.3 Write unit tests for sanitization
    - Test default patterns are redacted
    - Test custom patterns from options
    - Test case-insensitive matching
    - Test non-sensitive keys remain unchanged
    - Test SanitizeParameters=false returns raw values
    - Test null/empty parameters
    - _Requirements: 10.1–10.5_

- [ ] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. Implement Middleware and Attributes
  - [ ] 6.1 Create `SuppressTransactionLogAttribute` in `Attributes/SuppressTransactionLogAttribute.cs`
    - `[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]`
    - Marker attribute only (no logic)
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [ ] 6.2 Create `TransactionLoggingMiddleware` in `Middleware/TransactionLoggingMiddleware.cs`
    - Create TransactionContext per request (when not suppressed)
    - Check `[SuppressTransactionLog]` attribute on endpoint metadata
    - Check `SuppressRoutes` configuration
    - Set context in `TransactionContextAccessor`
    - Finalize on response completion
    - Fire-and-forget persistence
    - Capture IdempotencyKey and CorrelationId from headers
    - Handle exceptions (status=Failed)
    - _Requirements: 1.7, 5.2, 5.5, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 14.1, 14.2_

  - [ ]* 6.3 Write unit tests for middleware
    - Test context created for normal requests
    - Test context NOT created when Enabled=false
    - Test context NOT created when endpoint has [SuppressTransactionLog]
    - Test context NOT created when path matches SuppressRoutes
    - Test Finalize called with correct status code
    - Test exception handling sets Failed status
    - Test IdempotencyKey and CorrelationId captured from headers
    - Test persistence failure doesn't affect response
    - _Requirements: 5.2, 5.5, 6.1–6.7, 14.1, 14.2_

  - [ ]* 6.4 Write property tests for suppression
    - **Property 8: Suppression prevents context creation**
    - **Validates: Requirements 5.2, 5.5**

- [ ] 7. Implement Provider Interceptors
  - [ ] 7.1 Create `CacheTransactionInterceptor` in `Interceptors/CacheTransactionInterceptor.cs`
    - Implement all `ICache` methods as decorator
    - Record entries with provider="Caching", operation names matching existing tracing conventions
    - Respect capture rules (ImplicitCapture, CaptureProviders, ExcludeProviders, ExcludeOperations, MinimumDurationMs)
    - Sanitize parameters before recording
    - Skip recording when no TransactionContext is active
    - Never affect the original operation result
    - _Requirements: 3.1–3.9_

  - [ ] 7.2 Create `MessagingTransactionInterceptor` in `Interceptors/MessagingTransactionInterceptor.cs`
    - Same pattern as 7.1 for IMessaging
    - _Requirements: 3.1–3.9_

  - [ ] 7.3 Create `FileStorageTransactionInterceptor` in `Interceptors/FileStorageTransactionInterceptor.cs`
    - Same pattern as 7.1 for IFileStorage
    - _Requirements: 3.1–3.9_

  - [ ] 7.4 Create `SearchTransactionInterceptor` in `Interceptors/SearchTransactionInterceptor.cs`
    - Same pattern as 7.1 for ISearch
    - _Requirements: 3.1–3.9_

  - [ ] 7.5 Create `NotificationsTransactionInterceptor` in `Interceptors/NotificationsTransactionInterceptor.cs`
    - Same pattern as 7.1 for INotifications
    - _Requirements: 3.1–3.9_

  - [ ] 7.6 Create `WorkflowTransactionInterceptor` in `Interceptors/WorkflowTransactionInterceptor.cs`
    - Same pattern as 7.1 for IWorkflow
    - _Requirements: 3.1–3.9_

  - [ ] 7.7 Create `AIAssistantTransactionInterceptor` in `Interceptors/AIAssistantTransactionInterceptor.cs`
    - Same pattern as 7.1 for IAIAssistant
    - _Requirements: 3.1–3.9_

  - [ ] 7.8 Create `ReportingTransactionInterceptor` in `Interceptors/ReportingTransactionInterceptor.cs`
    - Same pattern as 7.1 for IReporting
    - _Requirements: 3.1–3.9_

  - [ ]* 7.9 Write property tests for capture rules
    - **Property 3: Implicit capture respects provider inclusion/exclusion rules**
    - **Property 4: Minimum duration threshold filtering**
    - **Validates: Requirements 3.3, 3.4, 3.5, 3.6**

  - [ ]* 7.10 Write unit tests for CacheTransactionInterceptor (representative)
    - Test entry recorded on success
    - Test entry recorded on error (exception still propagated)
    - Test no recording when ImplicitCapture=false
    - Test no recording when provider is excluded
    - Test no recording when operation is excluded
    - Test no recording when duration < MinimumDurationMs
    - Test no recording when no TransactionContext
    - Test recording failure doesn't affect cache operation
    - _Requirements: 3.1–3.9_

- [ ] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. Implement TransactionLogService (explicit API)
  - [ ] 9.1 Create `TransactionLogService` implementing `ITransactionLog` in `Services/TransactionLogService.cs`
    - `RecordEntryAsync` — adds entry to current TransactionContext with captureType="explicit"
    - `BeginScopeAsync` — creates new TransactionContext for non-HTTP scenarios
    - `CompleteScopeAsync` — finalizes explicit scope and persists
    - `SuppressCapture` — returns IDisposable that temporarily disables implicit capture
    - `QueryAsync` — delegates to ITransactionLogStore
    - No-op behavior when `Enabled=false` (null object pattern)
    - Handle missing TransactionContext gracefully (create ad-hoc for explicit entries)
    - _Requirements: 4.1–4.8, 9.1–9.7_

  - [ ]* 9.2 Write unit tests for TransactionLogService
    - Test RecordEntryAsync adds to current context
    - Test RecordEntryAsync creates ad-hoc context when none exists
    - Test BeginScopeAsync creates new context
    - Test CompleteScopeAsync finalizes and persists
    - Test SuppressCapture disables implicit recording within scope
    - Test all methods no-op when Enabled=false
    - _Requirements: 4.1–4.8_

- [ ] 10. Implement DynamoDB Storage
  - [ ] 10.1 Create `ITransactionLogStore` interface in `Storage/ITransactionLogStore.cs`
    - `PersistAsync(TransactionContext, CancellationToken)`
    - `QueryAsync(TransactionLogQuery, CancellationToken)`
    - _Requirements: 7.1, 9.1_

  - [ ] 10.2 Create `DynamoDbTransactionLogStore` in `Storage/DynamoDbTransactionLogStore.cs`
    - Serialize TransactionContext to DynamoDB item
    - Partition key: `{TenantId}#{YYYY-MM-DD}` (or `__global__#...`)
    - Sort key: `{TransactionId}`
    - Set TTL attribute: CreatedAt + HotRetentionDays
    - Handle item size > 400KB → split to S3
    - Query by TransactionId (GSI-1), by tenant+date, by business key (GSI-2)
    - Use GenesisResiliencePipelineBuilder for all DDB operations
    - _Requirements: 7.1–7.8, 9.1–9.7_

  - [ ] 10.3 Create `S3OverflowStore` in `Storage/S3OverflowStore.cs`
    - Write full transaction log as GZIP JSON to S3
    - Key format: `{TenantId}/{YYYY}/{MM}/{DD}/{TransactionId}.json.gz`
    - Support Object Lock when configured
    - _Requirements: 8.1–8.7_

  - [ ] 10.4 Create `InMemoryTransactionLogStore` in `Storage/InMemoryTransactionLogStore.cs`
    - In-memory store for unit testing and local dev fallback
    - Thread-safe concurrent dictionary
    - _Requirements: 18.3_

  - [ ]* 10.5 Write property tests for tenant isolation in storage
    - **Property 7: Tenant isolation in storage keys**
    - **Validates: Requirements 15.1, 15.2, 15.4**

  - [ ]* 10.6 Write unit tests for DynamoDbTransactionLogStore
    - Test partition key construction with tenant
    - Test partition key construction without tenant
    - Test TTL calculation
    - Test S3 overflow triggered when > 400KB
    - Test query by TransactionId
    - Test query by tenant + date range
    - Test query enforces tenant isolation
    - _Requirements: 7.1–7.8, 9.1–9.7, 15.1–15.4_

- [ ] 11. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 12. Implement DI Registration
  - [ ] 12.1 Create `TransactionalLoggingServiceCollectionExtensions` in `Extensions/`
    - `AddGenesisTransactionalLogging(IServiceCollection, IConfiguration)` — bind from `Genesis:TransactionalLogging`
    - `AddGenesisTransactionalLogging(IServiceCollection, Action<TransactionalLoggingOptions>)` — action-based config
    - `RegisterCoreServices` — register scoped accessor, scoped ITransactionLog, singleton store, singleton sanitizer
    - Decorate all 8 provider interfaces with transaction interceptors via Scrutor
    - Null guards on all parameters
    - TryAdd pattern for idempotent registration
    - _Requirements: 1.1–1.8_

  - [ ]* 12.2 Write unit tests for DI registration
    - Test null IServiceCollection throws ArgumentNullException
    - Test null IConfiguration throws ArgumentNullException
    - Test null Action throws ArgumentNullException
    - Test ITransactionLog registered as scoped
    - Test all interceptors registered (when providers present)
    - Test idempotent registration (calling twice doesn't duplicate)
    - _Requirements: 1.1–1.8_

- [ ] 13. Implement Observability
  - [ ] 13.1 Create `TransactionLogMetrics` in `Diagnostics/TransactionLogMetrics.cs`
    - Static readonly counters: `genesis.txlog.transactions`, `genesis.txlog.entries`, `genesis.txlog.persist.failures`
    - Static readonly histogram: `genesis.txlog.persist.duration`
    - Tags: status, tenant_id, capture_mode, provider, operation, result, capture_type, reason
    - _Requirements: 11.1–11.6_

  - [ ] 13.2 Create `TransactionLogTracing` in `Diagnostics/TransactionLogTracing.cs`
    - Activity: `txlog.scope` (created by middleware)
    - Child activity: `txlog.persist` (created by store)
    - Tags: txlog.transaction_id, txlog.entry_count, txlog.duration_ms, txlog.status, txlog.capture_mode, tenant.id, tenant.name
    - _Requirements: 12.1–12.5_

  - [ ] 13.3 Create `TransactionLogLogMessages` in `Diagnostics/TransactionLogLogMessages.cs`
    - Source-generated LoggerMessage attributes
    - Debug: context created, context finalized
    - Warning: persistence failed, circuit breaker opened
    - Information: S3 overflow, circuit breaker recovered
    - _Requirements: 13.1–13.5_

  - [ ]* 13.4 Write unit tests for metrics and tracing
    - Test transactions counter incremented on finalize
    - Test entries counter incremented per entry
    - Test persist.duration histogram recorded
    - Test persist.failures counter on store error
    - Test trace activity created with correct tags
    - _Requirements: 11.1–11.6, 12.1–12.5_

- [ ] 14. Implement Resilience and Fail-Open
  - [ ] 14.1 Integrate `GenesisResiliencePipelineBuilder` in `DynamoDbTransactionLogStore`
    - Configure pipeline from options.Resilience
    - Wrap all DDB calls in pipeline
    - On retries exhausted: log warning, discard transaction (fail-open)
    - Circuit breaker: when open, skip persistence entirely
    - Log state transitions (opened, half-open, closed)
    - _Requirements: 14.1–14.5_

  - [ ]* 14.2 Write property tests for fail-open
    - **Property 9: Fail-open on persistence failure**
    - **Validates: Requirements 14.1, 14.2**

  - [ ]* 14.3 Write unit tests for resilience
    - Test retry on transient DDB errors
    - Test circuit breaker opens after threshold
    - Test fail-open: exception from store doesn't propagate
    - Test recovery logging when circuit closes
    - _Requirements: 14.1–14.5_

- [ ] 15. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 16. Write integration tests
  - [ ]* 16.1 Create `Integration/EndToEndTransactionLogTests.cs`
    - Full DI setup with WebApplicationFactory
    - Test implicit capture: call cache → verify entry in transaction log
    - Test explicit capture: call RecordEntryAsync → verify entry
    - Test mixed: implicit + explicit entries in same transaction
    - Test [SuppressTransactionLog] endpoint not logged
    - Test SuppressRoutes config
    - Test Enabled=false → no logging
    - _Requirements: 3.1, 4.1, 5.2, 5.5_

  - [ ]* 16.2 Create `Integration/DynamoDbPersistenceTests.cs`
    - LocalStack DynamoDB integration
    - Test persist and query by TransactionId
    - Test query by tenant + date range
    - Test query by business key
    - Test TTL attribute correctly set
    - Test tenant isolation on query
    - _Requirements: 7.1–7.8, 9.1–9.7, 15.1–15.4_

  - [ ]* 16.3 Create `Integration/S3OverflowTests.cs`
    - LocalStack S3 integration
    - Test overflow triggered when > 400KB
    - Test S3 key structure correct
    - Test GZIP compression applied
    - _Requirements: 8.1–8.6_

  - [ ]* 16.4 Create `Integration/ResilienceTests.cs`
    - Test DDB failure → fail-open (response unaffected)
    - Test circuit breaker → skip persistence
    - Test recovery after DDB restored
    - _Requirements: 14.1–14.5_

- [ ] 17. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- **BLOCKER**: Tasks 7.1–7.8 (interceptors) require the provider interfaces from Core.Abstractions to be available. If `ITransactionLog` NuGet is not yet published, start with tasks 1–6 (everything except interceptor decoration)
- The 8 interceptors follow an identical pattern — consider a code generation approach or base class to reduce boilerplate
- Scrutor's `Decorate<TInterface, TDecorator>` only works if the interface is already registered in the container. The extensions should gracefully skip decoration for providers not registered in the current service.
- S3 archival auto-tiering (DynamoDB Streams → Lambda) is infrastructure, documented in design but NOT implemented in the library. Add as a separate IaC task.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1", "3.1", "3.2"] },
    { "id": 2, "tasks": ["2.2", "2.3", "3.3", "4.1"] },
    { "id": 3, "tasks": ["4.2", "4.3", "6.1"] },
    { "id": 4, "tasks": ["6.2", "6.3", "6.4"] },
    { "id": 5, "tasks": ["7.1", "7.2", "7.3", "7.4", "7.5", "7.6", "7.7", "7.8"] },
    { "id": 6, "tasks": ["7.9", "7.10", "9.1"] },
    { "id": 7, "tasks": ["9.2", "10.1", "10.2", "10.3", "10.4"] },
    { "id": 8, "tasks": ["10.5", "10.6", "12.1"] },
    { "id": 9, "tasks": ["12.2", "13.1", "13.2", "13.3"] },
    { "id": 10, "tasks": ["13.4", "14.1"] },
    { "id": 11, "tasks": ["14.2", "14.3"] },
    { "id": 12, "tasks": ["16.1", "16.2", "16.3", "16.4"] }
  ]
}
```
