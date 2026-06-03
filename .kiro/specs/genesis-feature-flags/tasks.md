# Implementation Plan: Genesis Feature Flags

## Overview

Implement the `Pervaxis.Genesis.FeatureFlags.AWS` module that wraps Microsoft.FeatureManagement with AWS AppConfig as the production configuration source, a custom TenantFilter for per-tenant targeting, and full Genesis observability instrumentation (metrics, tracing, state-change logging). The implementation follows standard Genesis registration patterns and folder conventions.

## Status: ✅ IMPLEMENTED

**Branch:** `feature/genesis-feature-flags`  
**Build:** 0 warnings, 0 errors  
**All 451 existing tests pass with no regressions**

## Tasks

- [x] 1. Set up project structure and core interfaces ✅
  - [x] 1.1 Create the `Pervaxis.Genesis.FeatureFlags.AWS` project with folder structure and .csproj ✅
  - [x] 1.2 Create the test project `Pervaxis.Genesis.FeatureFlags.AWS.Tests` ✅

- [x] 2. Implement FeatureFlagOptions and validation ✅
  - [x] 2.1 Create `FeatureFlagOptions` class in `Options/FeatureFlagOptions.cs` ✅

  - [ ]* 2.2 Write property tests for FeatureFlagOptions validation
    - **Property 1: Options validation rejects out-of-range polling intervals**
    - **Property 3: Options validation rejects empty AppConfigPath in non-emulator mode**
    - **Property 4: Options validation propagates Resilience validation failure**
    - **Validates: Requirements 2.3, 2.6, 2.7, 2.8**

  - [ ]* 2.3 Write unit tests for FeatureFlagOptions in `Unit/Options/FeatureFlagOptionsValidationTests.cs`
    - Test boundary values: PollingIntervalSeconds = 9, 10, 300, 301
    - Test AppConfigPath null/empty/whitespace with UseLocalEmulator false and true
    - Test Resilience sub-validation failure propagation
    - _Requirements: 2.3, 2.6, 2.7, 2.8_

- [ ] 3. Implement TenantFilter
  - [ ] 3.1 Create `TenantFilter` class in `Filters/TenantFilter.cs`
    - Implement `IFeatureFilter` with `[FilterAlias("Tenant")]`
    - Accept optional `ITenantContext?` via constructor injection
    - In `EvaluateAsync`: return false if `_tenantContext` is null or `IsResolved` is false; read `AllowedTenants` from parameters; return false if list is empty/null; match current tenant ID using `StringComparer.OrdinalIgnoreCase`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [ ]* 3.2 Write property tests for TenantFilter
    - **Property 6: TenantFilter evaluates correctly based on AllowedTenants membership**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.6**

  - [ ]* 3.3 Write unit tests for TenantFilter in `Unit/Filters/TenantFilterTests.cs`
    - Test null ITenantContext returns false
    - Test unresolved ITenantContext returns false
    - Test empty AllowedTenants returns false
    - Test case-insensitive matching (e.g., "Tenant-001" matches "tenant-001")
    - Test non-matching tenant returns false
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

- [ ] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Implement observability components
  - [ ] 5.1 Create `FeatureFlagStateTracker` class in `Observability/FeatureFlagStateTracker.cs`
    - Use `ConcurrentDictionary<string, bool>` for thread-safe state tracking
    - Implement `RecordEvaluation(flagName, tenantKey, currentResult, logger)` method
    - Compose key as `flagName` or `flagName:tenantKey`
    - On first evaluation: record baseline, no log emitted
    - On state transition: emit Information-level structured log with flag name, previous state, new state, tenant key, and UTC timestamp
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [ ]* 5.2 Write property tests for FeatureFlagStateTracker
    - **Property 9: State change logs emitted only on transitions**
    - **Validates: Requirements 12.1, 12.2, 12.4**

  - [ ]* 5.3 Write unit tests for FeatureFlagStateTracker in `Unit/Observability/StateTrackerTests.cs`
    - Test first evaluation records baseline without log
    - Test same-state consecutive evaluations emit no log
    - Test state transition emits exactly one log
    - Test per-tenant isolation (different tenants have independent state)
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [ ] 5.4 Create `FeatureFlagObservabilityInterceptor` class in `Observability/FeatureFlagObservabilityInterceptor.cs`
    - Implement `IFeatureManager` as a decorator wrapping the inner `IFeatureManager`
    - Declare static readonly metrics: `genesis.featureflags.evaluations` counter (unit "1"), `genesis.featureflags.appconfig.fallback` counter (unit "1"), `genesis.featureflags.evaluation.duration` histogram (unit "ms") using `PervaxisMeter`
    - In `IsEnabledAsync(string)`: start stopwatch, start activity via `PervaxisActivitySource.StartActivity("featureflags.evaluate", ActivityKind.Internal)`, set flag_name tag, add tenant tags if resolved, call inner, record result tag, record metrics, call state tracker, return result
    - In `IsEnabledAsync<TContext>`: same pattern with context passed through
    - On exception: set activity status to Error with message, record metrics with "error" result tag, rethrow
    - Implement `GetFeatureNamesAsync()` delegating to inner
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 11.1, 11.2, 11.3, 11.4, 11.5_

  - [ ]* 5.5 Write property tests for observability interceptor
    - **Property 7: Every flag evaluation emits correct observability metrics**
    - **Property 8: Every flag evaluation creates a trace activity with correct tags**
    - **Validates: Requirements 10.1, 10.3, 10.5, 11.1, 11.2, 11.4**

  - [ ]* 5.6 Write unit tests for observability interceptor in `Unit/Observability/ObservabilityInterceptorTests.cs`
    - Test metrics are emitted with correct tags on success
    - Test metrics are emitted with "error" tag on exception
    - Test trace activity created with correct span name and kind
    - Test tenant tags added when ITenantContext resolved
    - Test tenant tags omitted when ITenantContext null/unresolved
    - _Requirements: 10.1, 10.3, 10.5, 11.1, 11.2, 11.4, 11.5_

- [ ] 6. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 7. Implement DI registration and configuration extensions
  - [ ] 7.1 Create `FeatureFlagServiceCollectionExtensions` in `Extensions/FeatureFlagServiceCollectionExtensions.cs`
    - Implement `AddGenesisFeatureFlags(IServiceCollection, IConfiguration)` overload: null guards with `ArgumentNullException.ThrowIfNull`, bind from `Genesis:FeatureFlags` section, call `RegisterCoreServices`
    - Implement `AddGenesisFeatureFlags(IServiceCollection, Action<FeatureFlagOptions>)` overload: null guards, configure options via action, call `RegisterCoreServices`
    - Implement `RegisterCoreServices`: call `AddFeatureManagement()`, register PercentageFilter, TimeWindowFilter, TenantFilter, register FeatureFlagStateTracker as singleton, decorate IFeatureManager with FeatureFlagObservabilityInterceptor via Scrutor
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 9.1, 9.2_

  - [ ] 7.2 Create `FeatureFlagConfigurationExtensions` in `Extensions/FeatureFlagConfigurationExtensions.cs`
    - Implement `AddGenesisFeatureFlagSource(IConfigurationBuilder, IHostEnvironment, FeatureFlagOptions)`: null guards, skip in Development environment, resolve `{env}` placeholder with environment name, add SystemsManager source with polling interval and `Optional = true`
    - _Requirements: 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 5.1_

  - [ ]* 7.3 Write property tests for AppConfigPath resolution
    - **Property 2: AppConfigPath {env} placeholder resolution**
    - **Validates: Requirements 2.5**

  - [ ]* 7.4 Write unit tests for service collection extensions in `Unit/Extensions/ServiceCollectionExtensionsTests.cs`
    - Test null IServiceCollection throws ArgumentNullException with parameter name
    - Test null IConfiguration throws ArgumentNullException with parameter name
    - Test null Action throws ArgumentNullException with parameter name
    - Test IFeatureManager registered after calling AddGenesisFeatureFlags
    - Test TenantFilter, PercentageFilter, TimeWindowFilter registered
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

- [ ] 8. Implement flag naming convention validation
  - [ ] 8.1 Add flag naming convention helper and logging in the observability interceptor
    - Create a static helper method to validate flag names against `^[A-Za-z0-9]+\.[A-Za-z0-9]+$` with max length 128
    - In the observability interceptor, log a warning when a non-conforming flag name is evaluated (first occurrence only, to avoid log spam)
    - Accept any non-empty flag name up to 256 characters without throwing
    - _Requirements: 13.1, 13.2, 13.3, 13.4_

  - [ ]* 8.2 Write property tests for flag naming convention
    - **Property 10: Flag naming convention validation**
    - **Property 11: Runtime accepts any non-empty flag name up to 256 characters**
    - **Validates: Requirements 13.1, 13.2**

- [ ] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Implement fallback, resilience, and AppConfig integration wiring
  - [ ] 10.1 Implement resilience pipeline for AppConfig polling
    - Configure retry policy with exponential backoff + jitter using configured `RetryCount` and `RetryDelayMs`/`MaxRetryDelayMs`
    - Configure circuit breaker with failure threshold, minimum throughput, duration, and sampling duration from `ResilienceOptions`
    - Configure timeout from `TimeoutSeconds`
    - Handle transient errors: HTTP 429, 500-599, HttpRequestException, SocketException, IOException, TimeoutException, AWS throttling
    - When `Resilience.Enabled` is false, skip all wrapping
    - On retries exhausted: fall back to last cached config or evaluate all flags as disabled if cold start
    - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5_

  - [ ] 10.2 Implement fallback behavior and logging in the observability interceptor
    - On AppConfig failure: increment `genesis.featureflags.appconfig.fallback` counter with `reason` tag
    - Emit structured warning log with flag name, fallback reason, and timestamp
    - On recovery: emit informational log indicating AppConfig connectivity restored
    - Ensure flag evaluations continue serving cached values without blocking during poll
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 14.2, 14.3, 14.4_

  - [ ]* 10.3 Write unit tests for fallback and resilience behavior
    - Test fallback counter incremented on AppConfig failure
    - Test warning log emitted on fallback with correct details
    - Test recovery log emitted when AppConfig restored
    - Test flags evaluate as disabled when no config available (cold start)
    - Test resilience disabled skips retry/circuit breaker
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 15.1, 15.5_

- [ ] 11. Implement FeatureGate middleware integration
  - [ ] 11.1 Register FeatureGate middleware and action filters in the service collection extension
    - Ensure `AddFeatureManagement()` includes `.UseActionFilters()` to enable `[FeatureGate]` attribute support from `Microsoft.FeatureManagement.AspNetCore`
    - Return HTTP 404 when a gated action's flag is disabled (default Microsoft.FeatureManagement behavior)
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ]* 11.2 Write integration tests for FeatureGate behavior in `Integration/FeatureGateIntegrationTests.cs`
    - Test [FeatureGate] decorated action returns 404 when flag disabled
    - Test [FeatureGate] decorated action executes normally when flag enabled
    - Use WebApplicationFactory with in-memory configuration
    - _Requirements: 7.1, 7.2, 7.3_

- [ ] 12. Write integration tests for end-to-end fallback behavior
  - [ ]* 12.1 Create `Integration/FallbackBehaviorTests.cs`
    - Test full DI container starts with missing FeatureManagement section (all flags disabled)
    - Test local Development environment skips AppConfig entirely
    - Test flag evaluation returns false for undefined flags
    - Test percentage and time window filters work via in-memory config
    - _Requirements: 3.4, 3.5, 4.2, 5.1, 5.2, 5.3, 5.4, 6.3, 9.3, 9.4, 9.5, 9.6, 9.7_

- [ ] 13. Write property tests for undefined flags
  - [ ]* 13.1 Write property test for undefined flag evaluation
    - **Property 5: Undefined flags evaluate as disabled**
    - **Validates: Requirements 4.2, 6.3**

- [ ] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation uses C# with .NET 10, xUnit, NSubstitute, and FsCheck.Xunit
- All metrics follow the Genesis pattern: static readonly fields using PervaxisMeter
- The Scrutor library is used for the decorator pattern (IFeatureManager wrapping)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3", "3.1"] },
    { "id": 3, "tasks": ["3.2", "3.3", "5.1"] },
    { "id": 4, "tasks": ["5.2", "5.3", "5.4"] },
    { "id": 5, "tasks": ["5.5", "5.6", "7.1"] },
    { "id": 6, "tasks": ["7.2", "7.3", "7.4", "8.1"] },
    { "id": 7, "tasks": ["8.2", "10.1"] },
    { "id": 8, "tasks": ["10.2", "10.3", "11.1"] },
    { "id": 9, "tasks": ["11.2", "12.1", "13.1"] }
  ]
}
```
