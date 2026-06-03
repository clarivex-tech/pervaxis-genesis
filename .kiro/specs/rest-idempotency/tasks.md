# Implementation Plan: REST Idempotency

## Overview

This plan implements the Genesis REST Idempotency module across two projects: `Pervaxis.Genesis.Idempotency` (abstraction contracts, options, middleware, filter, services, diagnostics) and `Pervaxis.Genesis.Idempotency.AWS` (DynamoDB store implementation, in-memory fallback). Tasks are ordered to build foundational types first, then layer services, middleware, observability, and resilience on top, finishing with integration wiring and tests.

## Tasks

- [ ] 1. Set up project structure and core abstractions
  - [ ] 1.1 Create project files and directory structure
    - Create `src/Pervaxis.Genesis.Idempotency/Pervaxis.Genesis.Idempotency.csproj` targeting net8.0 with references to `Pervaxis.Core.Abstractions` and `Microsoft.AspNetCore.Http.Abstractions`
    - Create `src/Pervaxis.Genesis.Idempotency.AWS/Pervaxis.Genesis.Idempotency.AWS.csproj` targeting net8.0 with references to `Pervaxis.Genesis.Idempotency` and `AWSSDK.DynamoDBv2`
    - Create `tests/Pervaxis.Genesis.Idempotency.Tests/Pervaxis.Genesis.Idempotency.Tests.csproj` with references to xUnit, FsCheck.Xunit, NSubstitute, and the source projects
    - Create `tests/Pervaxis.Genesis.Idempotency.AWS.Tests/Pervaxis.Genesis.Idempotency.AWS.Tests.csproj` with references to xUnit, NSubstitute, and the AWS project
    - Create all subdirectory folders per the design project structure
    - Add project references to the solution file `Pervaxis.Genesis.slnx`
    - _Requirements: 1.1, 1.2, 10.1_

  - [ ] 1.2 Define `IIdempotencyStore` interface and `IdempotencyRecord` data model
    - Create `src/Pervaxis.Genesis.Idempotency/Abstractions/IIdempotencyStore.cs` with methods: `TryGetRecordAsync`, `CreateInFlightRecordAsync`, `CompleteRecordAsync`, `DeleteRecordAsync`
    - Create `src/Pervaxis.Genesis.Idempotency/Abstractions/IdempotencyRecord.cs` as a sealed class with all properties defined in the design (IdempotencyKey, CompositeKey, Fingerprint, IsCompleted, StatusCode, ResponseHeaders, ResponseBody, CreatedAt, ExpiresAtEpoch)
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_

  - [ ] 1.3 Define `IIdempotencyKeyValidator` and `IRequestFingerprintComputer` interfaces
    - Create `src/Pervaxis.Genesis.Idempotency/Services/IIdempotencyKeyValidator.cs` with `Validate(string? keyValue, bool hasMultipleValues)` method returning `IdempotencyKeyValidationResult`
    - Create `src/Pervaxis.Genesis.Idempotency/Services/IdempotencyKeyValidationResult.cs` as a readonly record struct
    - Create `src/Pervaxis.Genesis.Idempotency/Services/IRequestFingerprintComputer.cs` with `ComputeAsync(HttpContext, CancellationToken)` returning `Task<string>`
    - _Requirements: 3.1, 5.1_

- [ ] 2. Implement options and configuration
  - [ ] 2.1 Implement `IdempotencyOptions` with validation
    - Create `src/Pervaxis.Genesis.Idempotency/Options/IdempotencyOptions.cs` extending `GenesisOptionsBase`
    - Include properties: TableName (default "genesis-idempotency", max 255), TtlMinutes (default 1440, range 1-10080), HeaderName (default "Idempotency-Key", max 128), EnableTenantIsolation (default true), ValidateRequestFingerprint (default true), Resilience (ResilienceOptions)
    - Implement `Validate()` with all constraint checks per design
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9, 2.10, 2.11, 2.12_

  - [ ]* 2.2 Write property tests for `IdempotencyOptions` validation
    - **Property 2: Options Validation Correctness**
    - **Validates: Requirements 2.8, 2.9, 2.10, 2.11, 2.12**
    - Use FsCheck to generate arbitrary options instances and verify `Validate()` returns true iff all constraints hold

  - [ ] 2.3 Implement `IdempotencyMiddlewareOptions`
    - Create `src/Pervaxis.Genesis.Idempotency/Options/IdempotencyMiddlewareOptions.cs` with `RoutePatterns` (List<string>) and `HttpMethods` (HashSet<string> defaulting to POST, PATCH)
    - _Requirements: 9.2, 9.6_

- [ ] 3. Implement key validation and fingerprint services
  - [ ] 3.1 Implement `IdempotencyKeyValidator`
    - Create `src/Pervaxis.Genesis.Idempotency/Services/IdempotencyKeyValidator.cs` implementing `IIdempotencyKeyValidator`
    - Validate: reject null/empty/whitespace, reject > 256 chars, reject chars outside `[a-zA-Z0-9\-_.]`, reject multiple header values
    - Return appropriate error codes: `IDEMPOTENCY_KEY_MISSING`, `IDEMPOTENCY_KEY_INVALID`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [ ]* 3.2 Write property tests for `IdempotencyKeyValidator`
    - **Property 3: Idempotency Key Validation**
    - **Validates: Requirements 3.1, 3.3, 3.4, 3.5**
    - Use FsCheck to generate arbitrary strings and verify the validator accepts iff 1-256 chars of `[a-zA-Z0-9\-_.]` with exactly one value

  - [ ] 3.3 Implement `RequestFingerprintComputer`
    - Create `src/Pervaxis.Genesis.Idempotency/Services/RequestFingerprintComputer.cs` implementing `IRequestFingerprintComputer`
    - Compute fingerprint as `"{METHOD}|{routeTemplate}|{SHA256(body)}"` using SHA-256 for body hashing
    - Handle null/empty body with empty byte array SHA-256
    - Enable request body rewinding for subsequent reads
    - _Requirements: 5.1, 5.4_

  - [ ]* 3.4 Write property tests for `RequestFingerprintComputer`
    - **Property 6: Fingerprint Determinism**
    - **Validates: Requirements 5.1**
    - Use FsCheck to verify same inputs produce same output, and different inputs produce different outputs

- [ ] 4. Implement composite key construction and tenant isolation logic
  - [ ] 4.1 Implement composite key builder utility
    - Create a static helper or service method for constructing composite keys: `"{tenantId}#{idempotencyKey}"` when isolation enabled + tenant resolved, `"__global__#{idempotencyKey}"` otherwise
    - Validate tenant ID does not contain `#` character, reject with `IDEMPOTENCY_TENANT_INVALID` error
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_

  - [ ]* 4.2 Write property tests for composite key construction
    - **Property 9: Composite Key Construction**
    - **Validates: Requirements 7.1, 7.2, 7.3, 7.5**
    - Use FsCheck to verify key format for all combinations of tenant isolation enabled/disabled and tenant resolved/not-resolved

- [ ] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. Implement the idempotency action filter (core request lifecycle)
  - [ ] 6.1 Implement `IdempotencyActionFilter`
    - Create `src/Pervaxis.Genesis.Idempotency/Filters/IdempotencyActionFilter.cs` implementing `IAsyncActionFilter`
    - Inject `IIdempotencyStore`, `IIdempotencyKeyValidator`, `IRequestFingerprintComputer`, `IOptions<IdempotencyOptions>`, `ITenantContext`, `ILogger<IdempotencyActionFilter>`
    - Implement full request lifecycle: extract key → validate → resolve tenant → build composite key → check store → handle existing record (replay/conflict/fingerprint mismatch) → create in-flight → execute action → store response → handle exceptions (delete in-flight + propagate)
    - Set `Idempotency-Replayed: true` header on cached responses
    - Support per-endpoint TTL and ValidateFingerprint overrides from attribute
    - Implement fail-open on store failures (catch exceptions, log, proceed)
    - Handle expired records as nonexistent
    - Handle abandoned in-flight records older than TTL
    - _Requirements: 3.1, 3.2, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.1, 5.2, 5.3, 5.4, 5.5, 6.3, 6.4, 7.5, 15.3, 15.4, 15.6, 15.7_

  - [ ]* 6.2 Write property tests for record lifecycle state machine
    - **Property 5: Record Lifecycle State Machine**
    - **Validates: Requirements 4.3, 4.4, 4.6, 10.3, 10.4, 10.5**
    - Use FsCheck with mocked IIdempotencyStore to verify state transitions: nonexistent → in-flight → completed

  - [ ]* 6.3 Write property tests for cached response fidelity
    - **Property 4: Cached Response Fidelity (Round-Trip)**
    - **Validates: Requirements 4.1, 4.2**
    - Use FsCheck to generate arbitrary response data and verify byte-for-byte cached response fidelity with `Idempotency-Replayed: true`

  - [ ]* 6.4 Write property tests for fingerprint mismatch detection
    - **Property 7: Fingerprint Mismatch Detection**
    - **Validates: Requirements 5.2, 5.3, 5.5**
    - Use FsCheck to verify HTTP 422 with `IDEMPOTENCY_KEY_REUSE` when fingerprints differ and ValidateRequestFingerprint is true

  - [ ]* 6.5 Write property tests for record expiration correctness
    - **Property 8: Record Expiration Correctness**
    - **Validates: Requirements 6.1, 6.3, 6.4, 4.7**
    - Use FsCheck to verify expired records are treated as nonexistent and ExpiresAtEpoch equals CreatedAt + (TtlMinutes × 60)

  - [ ]* 6.6 Write property tests for fail-open resilience
    - **Property 12: Fail-Open Resilience**
    - **Validates: Requirements 15.3, 15.4, 15.6**
    - Use FsCheck with mocked IIdempotencyStore that throws exceptions to verify requests proceed to endpoint without error

- [ ] 7. Implement the `[Idempotent]` attribute
  - [ ] 7.1 Implement `IdempotentAttribute`
    - Create `src/Pervaxis.Genesis.Idempotency/Filters/IdempotentAttribute.cs` with `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]`
    - Implement `IFilterFactory` returning `IdempotencyActionFilter` from DI
    - Include `TtlMinutes` property (int, default 0) and `ValidateFingerprint` property (bool?, default null)
    - Set `IsReusable => true`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_

- [ ] 8. Implement the idempotency middleware
  - [ ] 8.1 Implement `IdempotencyMiddleware`
    - Create `src/Pervaxis.Genesis.Idempotency/Middleware/IdempotencyMiddleware.cs`
    - Implement route pattern matching (case-insensitive) against configured `IdempotencyMiddlewareOptions.RoutePatterns`
    - Apply HTTP method filtering (default POST, PATCH)
    - Delegate to `IdempotencyActionFilter` when matched, pass through when not
    - Defer to attribute when both middleware and attribute target the same endpoint
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_

  - [ ]* 8.2 Write property tests for route pattern matching
    - **Property 10: Route Pattern Matching**
    - **Validates: Requirements 9.2, 9.3, 9.4, 9.6**
    - Use FsCheck to generate route patterns and request paths, verify matching applies only when path matches AND method is in configured set

- [ ] 9. Implement observability (metrics, tracing, logging)
  - [ ] 9.1 Implement `IdempotencyMetrics`
    - Create `src/Pervaxis.Genesis.Idempotency/Diagnostics/IdempotencyMetrics.cs`
    - Create `genesis.idempotency.requests` counter (tagged: outcome, http_method, endpoint, optional tenant_id)
    - Create `genesis.idempotency.store.duration` histogram in ms (tagged: operation, outcome, optional tenant_id)
    - Use `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>` as static readonly fields
    - Suppress metric emission failures silently
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [ ] 9.2 Implement `IdempotencyTracing`
    - Create `src/Pervaxis.Genesis.Idempotency/Diagnostics/IdempotencyTracing.cs`
    - Create `idempotency.process` activity (Internal kind) with tags: idempotency.key, idempotency.outcome, http.method, http.route, tenant.id, tenant.name
    - Create child `idempotency.store.{operation}` activities (Client kind) for store operations
    - Set error status on failed store activities
    - Use null-safe activity pattern (`activity?.SetTag`)
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6_

  - [ ] 9.3 Implement `IdempotencyLogMessages`
    - Create `src/Pervaxis.Genesis.Idempotency/Diagnostics/IdempotencyLogMessages.cs` using source-generated `[LoggerMessage]` attributes
    - Define log messages: duplicate detected (Info), record created (Debug), fingerprint mismatch (Warning), conflict (Warning), store error (Error), key invalid (Warning)
    - Include structured properties: idempotency key, endpoint, method, tenant_id, operation, exception details
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7_

- [ ] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Implement DynamoDB store (AWS project)
  - [ ] 11.1 Implement `DynamoDbIdempotencyStore`
    - Create `src/Pervaxis.Genesis.Idempotency.AWS/Providers/DynamoDb/DynamoDbIdempotencyStore.cs` implementing `IIdempotencyStore`
    - Use `IAmazonDynamoDB` for all operations
    - Implement `TryGetRecordAsync`: GetItem, check expiration, return null if expired/missing
    - Implement `CreateInFlightRecordAsync`: PutItem with `ConditionExpression: attribute_not_exists(PK) OR ExpiresAt <= :now`, handle `ConditionalCheckFailedException` → return false
    - Implement `CompleteRecordAsync`: UpdateItem with condition that record exists and is in-flight
    - Implement `DeleteRecordAsync`: DeleteItem (no-op if missing)
    - Handle item size limit (400 KB) on CompleteRecordAsync — return error result, don't cache
    - Wrap all calls with `GenesisResiliencePipelineBuilder` pipeline
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.7, 15.1, 15.2_

  - [ ]* 11.2 Write property tests for store atomicity (create mutual exclusion)
    - **Property 11: Store Atomicity (Create Mutual Exclusion)**
    - **Validates: Requirements 10.3, 11.2**
    - Use FsCheck with concurrent test scenarios to verify only one CreateInFlightRecordAsync succeeds for a given key

  - [ ] 11.3 Implement `DynamoDbTableInitializer`
    - Create `src/Pervaxis.Genesis.Idempotency.AWS/Providers/DynamoDb/DynamoDbTableInitializer.cs`
    - Auto-create table when `UseLocalEmulator` is true and table doesn't exist
    - Configure table with PK (String) partition key and TTL attribute on `ExpiresAt`
    - _Requirements: 11.6, 17.2_

  - [ ] 11.4 Implement `InMemoryIdempotencyStore`
    - Create `src/Pervaxis.Genesis.Idempotency.AWS/Fallback/InMemoryIdempotencyStore.cs` implementing `IIdempotencyStore`
    - Use `ConcurrentDictionary` as backing store
    - Honor TTL by checking expiration on retrieval
    - Guarantee atomicity on `CreateInFlightRecordAsync` using `ConcurrentDictionary.TryAdd`
    - Evict expired entries when store exceeds 10000 records
    - _Requirements: 17.3, 17.4, 17.5_

- [ ] 12. Implement DI registration extensions
  - [ ] 12.1 Implement `IdempotencyServiceCollectionExtensions`
    - Create `src/Pervaxis.Genesis.Idempotency/Extensions/IdempotencyServiceCollectionExtensions.cs`
    - Provide `AddGenesisIdempotency(IServiceCollection, IConfiguration)` overload binding "Genesis:Idempotency" section
    - Provide `AddGenesisIdempotency(IServiceCollection, Action<IdempotencyOptions>)` overload
    - Register `IIdempotencyStore` as singleton, `IdempotencyActionFilter`, `IIdempotencyKeyValidator`, `IRequestFingerprintComputer`
    - Use TryAdd pattern for idempotent registration
    - Throw `ArgumentNullException` for null parameters
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [ ]* 12.2 Write property tests for options configuration round-trip
    - **Property 1: Options Configuration Round-Trip**
    - **Validates: Requirements 1.5, 1.6**
    - Use FsCheck to generate valid configuration values and verify binding produces matching options instance

  - [ ] 12.3 Implement `IdempotencyApplicationBuilderExtensions`
    - Create `src/Pervaxis.Genesis.Idempotency/Extensions/IdempotencyApplicationBuilderExtensions.cs`
    - Provide `UseGenesisIdempotency(IApplicationBuilder, Action<IdempotencyMiddlewareOptions>)` extension method
    - Throw `ArgumentNullException` for null parameters
    - _Requirements: 9.1, 9.7_

  - [ ] 12.4 Implement `IdempotencyAwsServiceCollectionExtensions`
    - Create `src/Pervaxis.Genesis.Idempotency.AWS/Extensions/IdempotencyAwsServiceCollectionExtensions.cs`
    - Register `DynamoDbIdempotencyStore` as the `IIdempotencyStore` implementation
    - Handle `UseLocalEmulator` logic: attempt DynamoDB connection, fallback to `InMemoryIdempotencyStore` on timeout (5s) or permission error
    - Register table initializer for local emulator mode
    - _Requirements: 17.1, 17.2, 17.3, 17.5_

- [ ] 13. Implement resilience pipeline integration
  - [ ] 13.1 Configure resilience pipeline for DynamoDB store
    - Wire `GenesisResiliencePipelineBuilder` in the DynamoDB store with configured retry (exponential backoff + full jitter), circuit breaker, and timeout policies
    - Handle transient errors: HTTP 429, HTTP 500-599, `ProvisionedThroughputExceededException`, `InternalServerErrorException`, `HttpRequestException`, `SocketException`, `IOException`, `TimeoutException`
    - Support `Resilience.Enabled = false` to bypass wrapping
    - _Requirements: 15.1, 15.2, 15.5_

- [ ] 14. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 15. Integration tests and end-to-end wiring
  - [ ] 15.1 Write unit tests for DI registration
    - Test `AddGenesisIdempotency` with IConfiguration binding
    - Test `AddGenesisIdempotency` with Action<IdempotencyOptions>
    - Test null parameter ArgumentNullException
    - Test TryAdd idempotent registration
    - Test `UseGenesisIdempotency` registration and null checks
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.7, 1.8, 9.1, 9.7_

  - [ ]* 15.2 Write integration tests for DynamoDB store with LocalStack
    - Test conditional write atomicity (concurrent CreateInFlightRecordAsync)
    - Test TTL-based expiration behavior
    - Test item size limit handling (> 400 KB)
    - Test table auto-creation on UseLocalEmulator
    - Test in-memory fallback when LocalStack unreachable
    - _Requirements: 11.2, 11.3, 11.4, 11.5, 11.6, 17.2, 17.3_

  - [ ]* 15.3 Write integration tests for end-to-end request flow
    - Test full flow: new request → store → replay with Idempotency-Replayed header
    - Test conflict (409) scenario
    - Test fingerprint mismatch (422) scenario
    - Test key validation error scenarios (400)
    - Test fail-open behavior when store is unavailable
    - Test attribute + middleware overlap (attribute precedence)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 5.2, 5.3, 8.7, 15.3_

- [ ] 16. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The module follows existing Genesis patterns (abstraction + AWS split, GenesisOptionsBase, PervaxisMeter, PervaxisActivitySource, GenesisResiliencePipelineBuilder)
- All implementations use C# targeting .NET 8.0
- FsCheck.Xunit is used for property-based testing with minimum 100 iterations per property

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2", "1.3"] },
    { "id": 2, "tasks": ["2.1", "2.3"] },
    { "id": 3, "tasks": ["2.2", "3.1", "3.3", "4.1"] },
    { "id": 4, "tasks": ["3.2", "3.4", "4.2"] },
    { "id": 5, "tasks": ["6.1", "7.1"] },
    { "id": 6, "tasks": ["6.2", "6.3", "6.4", "6.5", "6.6", "8.1"] },
    { "id": 7, "tasks": ["8.2", "9.1", "9.2", "9.3"] },
    { "id": 8, "tasks": ["11.1", "11.3", "11.4"] },
    { "id": 9, "tasks": ["11.2", "12.1", "12.3"] },
    { "id": 10, "tasks": ["12.2", "12.4", "13.1"] },
    { "id": 11, "tasks": ["15.1", "15.2", "15.3"] }
  ]
}
```
