# Requirements Document

## Introduction

The Genesis Feature Flags module provides feature flag infrastructure to all vertical domain services within the Pervaxis platform. It uses Microsoft.FeatureManagement backed by AWS AppConfig in production and appsettings.json locally. This module follows existing Genesis patterns for DI registration, options, observability, and multi-tenancy. Domain services consume flags exclusively through `IFeatureManager` — never interacting with flag storage directly.

## Glossary

- **Feature_Flag_Module**: The Genesis library module (`Pervaxis.Genesis.FeatureFlags.AWS`) that registers and configures Microsoft.FeatureManagement with AWS AppConfig and built-in filters.
- **Feature_Manager**: The `IFeatureManager` interface from Microsoft.FeatureManagement that vertical domain services use to evaluate flag state.
- **AppConfig_Provider**: The AWS AppConfig configuration source that supplies feature flag values in staging and production environments via polling.
- **TenantFilter**: A custom `IFeatureFilter` implementation that evaluates flag state based on the current tenant identity from `ITenantContext`.
- **FeatureFlagOptions**: The options class extending `GenesisOptionsBase` that configures the Feature Flag module (AppConfig path, polling interval, resilience settings).
- **Flag_Name**: A string identifier following the `{Domain}.{Feature}` convention (e.g., `Billing.NewInvoiceFlow`).
- **Forge**: The code generation engine that auto-wires Genesis module registration into every generated service.
- **ITenantContext**: The tenant resolution abstraction from `Pervaxis.Core.Abstractions.MultiTenancy` providing current tenant identity.
- **PervaxisMeter**: The static metrics factory from `Pervaxis.Core.Observability.Metrics` used to create counters and histograms.
- **PervaxisActivitySource**: The static tracing source from `Pervaxis.Core.Observability.Tracing` used to create distributed trace activities.

## Requirements

### Requirement 1: Module Registration

**User Story:** As a platform engineer, I want to register the Feature Flag module using a standard Genesis extension method, so that it integrates consistently with other Genesis modules.

#### Acceptance Criteria

1. THE Feature_Flag_Module SHALL provide an `AddGenesisFeatureFlags` extension method on `IServiceCollection` that accepts an `IConfiguration` parameter and returns `IServiceCollection` for method chaining.
2. THE Feature_Flag_Module SHALL provide an `AddGenesisFeatureFlags` extension method on `IServiceCollection` that accepts an `Action<FeatureFlagOptions>` parameter and returns `IServiceCollection` for method chaining.
3. WHEN `AddGenesisFeatureFlags` is called, THE Feature_Flag_Module SHALL register `IFeatureManager` in the dependency injection container as a singleton.
4. WHEN `AddGenesisFeatureFlags` is called, THE Feature_Flag_Module SHALL register the PercentageFilter, TimeWindowFilter, and TenantFilter as feature filters.
5. WHEN `AddGenesisFeatureFlags` is called with an `IConfiguration` parameter, THE Feature_Flag_Module SHALL bind options from the `Genesis:FeatureFlags` configuration section.
6. IF `AddGenesisFeatureFlags` is called with a null `IServiceCollection`, null `IConfiguration`, or null `Action<FeatureFlagOptions>` parameter, THEN THE Feature_Flag_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.

### Requirement 2: Options Configuration

**User Story:** As a platform engineer, I want to configure the Feature Flag module through a validated options class, so that misconfiguration is caught early at startup.

#### Acceptance Criteria

1. THE FeatureFlagOptions SHALL extend `GenesisOptionsBase`.
2. THE FeatureFlagOptions SHALL include an `AppConfigPath` property of type string for specifying the AWS AppConfig parameter path.
3. THE FeatureFlagOptions SHALL include a `PollingIntervalSeconds` property of type integer with a default value of 30 and a valid range of 10 to 300.
4. THE FeatureFlagOptions SHALL include a `Resilience` property of type `ResilienceOptions` initialized with default values.
5. WHEN `AppConfigPath` contains the `{env}` placeholder, THE Feature_Flag_Module SHALL replace the placeholder with the value from the current hosting environment name (e.g., `Development`, `Staging`, `Production`).
6. THE FeatureFlagOptions SHALL implement a `Validate()` method that returns false when `AppConfigPath` is null or empty and `UseLocalEmulator` is false.
7. THE FeatureFlagOptions `Validate()` method SHALL return false when `PollingIntervalSeconds` is less than 10 or greater than 300.
8. THE FeatureFlagOptions `Validate()` method SHALL return false when the `Resilience` property fails its own validation.

### Requirement 3: AWS AppConfig Integration

**User Story:** As a platform engineer, I want feature flags sourced from AWS AppConfig in production, so that flag values can change without redeployment.

#### Acceptance Criteria

1. WHEN the application starts in a non-local environment (staging or production), THE Feature_Flag_Module SHALL add AWS AppConfig as a configuration source using the configured `AppConfigPath`.
2. THE AppConfig_Provider SHALL poll for configuration changes at the interval specified by `PollingIntervalSeconds`, which SHALL default to 30 seconds and accept values between 10 and 300 seconds.
3. WHEN a flag value changes in AppConfig, THE Feature_Flag_Module SHALL reflect the updated value within one polling interval (maximum of the configured `PollingIntervalSeconds`) without requiring a restart.
4. IF AppConfig is unreachable during application startup or during polling, THEN THE AppConfig_Provider SHALL fall back to the values in appsettings configuration and the application SHALL continue to start and operate without error.
5. IF the `AppConfigPath` configuration value is missing or empty in a non-local environment, THEN THE Feature_Flag_Module SHALL log a warning and fall back to appsettings configuration without preventing application startup.

### Requirement 4: Fallback Behavior

**User Story:** As a platform engineer, I want predictable fallback behavior when AppConfig is unavailable, so that services degrade gracefully.

#### Acceptance Criteria

1. IF AppConfig fails to respond within the configured polling interval (default 30 seconds) or returns a connection error, THEN THE Feature_Flag_Module SHALL fall back to reading flag values from `appsettings.json` for all subsequent flag evaluations until AppConfig connectivity is restored.
2. IF a flag is not defined in AppConfig or `appsettings.json`, THEN THE Feature_Flag_Module SHALL evaluate the flag as disabled (false).
3. WHEN fallback to `appsettings.json` occurs, THE Feature_Flag_Module SHALL emit a structured warning log entry containing at minimum the flag name requested, the fallback reason, and a timestamp.
4. WHEN fallback to `appsettings.json` occurs, THE Feature_Flag_Module SHALL increment the `genesis.featureflags.appconfig.fallback` counter metric with a `reason` tag indicating the failure type.
5. WHEN AppConfig connectivity is restored after a fallback period, THE Feature_Flag_Module SHALL resume reading flag values from AppConfig within the next polling interval and emit a structured informational log indicating recovery.

### Requirement 5: Local Development Support

**User Story:** As a developer, I want to use feature flags locally via appsettings.json without any AWS dependency, so that I can develop and test flag-gated behavior offline.

#### Acceptance Criteria

1. WHEN the application starts with the host environment name set to "Development", THE Feature_Flag_Module SHALL read flag definitions exclusively from `appsettings.json` and `appsettings.Development.json` without contacting AWS AppConfig.
2. IF AWS credentials are absent or AWS AppConfig is unreachable during application startup, THEN THE Feature_Flag_Module SHALL start successfully within 5 seconds and resolve flags from local appsettings configuration only.
3. THE Feature_Flag_Module SHALL support the `FeatureManagement` JSON section structure including simple boolean flags and filter-based flags (Percentage, TimeWindow, Tenant) for local flag configuration.
4. IF the `FeatureManagement` section is missing or empty in the local appsettings files, THEN THE Feature_Flag_Module SHALL start without error and return disabled (false) for all flag evaluations via `IFeatureManager.IsEnabledAsync()`.

### Requirement 6: Feature Flag Consumption — Imperative

**User Story:** As a domain developer, I want to check flag state programmatically, so that I can branch logic based on feature availability.

#### Acceptance Criteria

1. THE Feature_Manager SHALL be resolvable via dependency injection in any vertical domain service that accepts constructor-injected dependencies.
2. WHEN `IsEnabledAsync` is called with a Flag_Name that is defined in the flag configuration, THE Feature_Manager SHALL return true if the flag is enabled for the current evaluation context (including tenant identity and any registered filter parameters), and false otherwise.
3. IF `IsEnabledAsync` is called with a Flag_Name that is not defined in the flag configuration, THEN THE Feature_Manager SHALL return false.
4. THE Feature_Manager SHALL support async evaluation to allow filter logic that depends on external context (e.g., tenant resolution), completing evaluation within 5 seconds.
5. IF a feature filter evaluation fails due to an error (e.g., unavailable external context or misconfigured filter parameters), THEN THE Feature_Manager SHALL return false and log the failure.

### Requirement 7: Feature Flag Consumption — Declarative

**User Story:** As a domain developer, I want to gate entire controller actions with an attribute, so that I can enable or disable endpoints without modifying business logic.

#### Acceptance Criteria

1. THE Feature_Flag_Module SHALL register the middleware and action filters required to process the `[FeatureGate]` attribute from Microsoft.FeatureManagement.AspNetCore on controller actions and controller classes.
2. WHEN a request reaches a `[FeatureGate]`-decorated action and the flag is disabled, THE Feature_Flag_Module SHALL return HTTP 404 to the caller without executing the action body.
3. WHEN a request reaches a `[FeatureGate]`-decorated action and the flag is enabled, THE Feature_Flag_Module SHALL allow the request to proceed to the action for normal execution.

### Requirement 8: TenantFilter — Per-Tenant Targeting

**User Story:** As a platform engineer, I want to enable flags for specific tenants, so that I can progressively roll out features to individual customers.

#### Acceptance Criteria

1. THE TenantFilter SHALL resolve the current tenant identity by reading the `TenantId.Value` string from `ITenantContext`.
2. IF `ITenantContext` is null or its `IsResolved` property is false, THEN THE TenantFilter SHALL evaluate the flag as disabled (false).
3. WHEN the current tenant ID matches an entry in the filter's `AllowedTenants` configuration list using case-insensitive ordinal comparison, THE TenantFilter SHALL evaluate to true.
4. WHEN the current tenant ID does not match any entry in the `AllowedTenants` list using case-insensitive ordinal comparison, THE TenantFilter SHALL evaluate to false.
5. THE TenantFilter SHALL accept `ITenantContext` as an optional nullable dependency injected via constructor.
6. IF the `AllowedTenants` configuration list is empty or not present in the filter parameters, THEN THE TenantFilter SHALL evaluate to false.

### Requirement 9: Built-in Filters — Percentage and TimeWindow

**User Story:** As a platform engineer, I want percentage-based and time-window rollout strategies available out of the box, so that I can control gradual rollouts and scheduled availability.

#### Acceptance Criteria

1. THE Feature_Flag_Module SHALL register `PercentageFilter` from Microsoft.FeatureManagement so that it is resolvable during feature evaluation at runtime.
2. THE Feature_Flag_Module SHALL register `TimeWindowFilter` from Microsoft.FeatureManagement so that it is resolvable during feature evaluation at runtime.
3. WHEN a flag is configured with a Percentage filter specifying a `Value` between 0 and 100 inclusive, THE Feature_Flag_Module SHALL enable the flag for approximately that percentage of evaluations, where evaluation consistency is determined by the Microsoft.FeatureManagement library's default behavior.
4. WHEN a flag is configured with a TimeWindow filter specifying both `Start` and `End` timestamps, THE Feature_Flag_Module SHALL enable the flag only when the current UTC time is at or after `Start` and before `End`.
5. WHEN a flag is configured with a TimeWindow filter specifying only a `Start` timestamp and no `End`, THE Feature_Flag_Module SHALL enable the flag from `Start` onward with no automatic expiry.
6. WHEN a flag is configured with a TimeWindow filter specifying only an `End` timestamp and no `Start`, THE Feature_Flag_Module SHALL enable the flag from application start until `End` is reached.
7. IF a Percentage filter is configured with a `Value` outside the range 0–100 or with a missing `Value` parameter, THEN THE Feature_Flag_Module SHALL treat the flag as disabled for all evaluations.

### Requirement 10: Observability — Metrics

**User Story:** As an SRE, I want metrics on feature flag evaluations and provider health, so that I can monitor flag usage and detect configuration issues.

#### Acceptance Criteria

1. THE Feature_Flag_Module SHALL emit a `genesis.featureflags.evaluations` counter metric for each flag evaluation, tagged with the flag name (tag: `flag_name`), result (tag: `result` with values `enabled` or `disabled`), and `tenant_id` when `EnableTenantIsolation` is true and `ITenantContext` is resolved.
2. WHEN AppConfig is unreachable and fallback to `appsettings.json` occurs, THE Feature_Flag_Module SHALL increment the `genesis.featureflags.appconfig.fallback` counter metric.
3. THE Feature_Flag_Module SHALL emit a `genesis.featureflags.evaluation.duration` histogram metric measuring evaluation latency in milliseconds, tagged with `flag_name` and `result` matching criterion 1.
4. THE Feature_Flag_Module SHALL create all metrics as `static readonly` fields using `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>` with the unit parameter set to `"1"` for counters and `"ms"` for histograms.
5. IF an evaluation throws an exception, THEN THE Feature_Flag_Module SHALL record the `genesis.featureflags.evaluations` counter and `genesis.featureflags.evaluation.duration` histogram with a `result` tag value of `error` before propagating the exception.

### Requirement 11: Observability — Tracing

**User Story:** As an SRE, I want distributed trace spans for feature flag evaluations, so that I can understand flag evaluation latency in the context of a request.

#### Acceptance Criteria

1. WHEN a flag is evaluated, THE Feature_Flag_Module SHALL create a trace activity using `PervaxisActivitySource` with span name `featureflags.evaluate` and ActivityKind `Internal`.
2. WHEN a trace activity is created for a flag evaluation, THE Feature_Flag_Module SHALL set the tags `featureflags.flag_name` (the evaluated flag name) and `featureflags.result` (`enabled` or `disabled`).
3. WHEN a trace activity is created and the flag evaluation used a named filter (Tenant, Percentage, or TimeWindow), THE Feature_Flag_Module SHALL set the tag `featureflags.filter_type` to the filter alias name.
4. WHEN `ITenantContext` is resolved, THE trace activity SHALL include `tenant.id` and `tenant.name` tags consistent with existing Genesis provider tracing.
5. IF an evaluation error occurs, THEN THE trace activity SHALL set status to Error with the exception message and retain any tags already set prior to the error.

### Requirement 12: Observability — State Change Logging

**User Story:** As an SRE, I want to see when a flag's effective state changes, so that I can correlate behavior changes with flag transitions.

#### Acceptance Criteria

1. WHEN a flag's evaluated state transitions from disabled to enabled (or enabled to disabled), THE Feature_Flag_Module SHALL emit a structured log at Information level containing the flag name, previous state, new state, and a UTC timestamp.
2. THE Feature_Flag_Module SHALL track the last-known evaluated state per flag per tenant (when `ITenantContext` is resolved) or per flag per process (when `ITenantContext` is not resolved), and SHALL emit a state change log only when the evaluated result differs from the last-known state — not on every evaluation.
3. IF `ITenantContext` is resolved, THEN THE Feature_Flag_Module SHALL include the tenant ID in the state change log entry.
4. WHEN a flag is evaluated for the first time after process startup (no prior state is recorded), THE Feature_Flag_Module SHALL record the initial state without emitting a transition log, so that subsequent evaluations can detect changes from that baseline.

### Requirement 13: Flag Naming Convention

**User Story:** As a platform engineer, I want a consistent flag naming convention, so that flags are discoverable and organized by domain.

#### Acceptance Criteria

1. THE Feature_Flag_Module SHALL define the naming convention as `{Domain}.{Feature}` where each segment contains only alphanumeric characters with no spaces, the dot character (`.`) serves as the sole separator between exactly two segments, and the total flag name length does not exceed 128 characters.
2. THE Feature_Flag_Module SHALL accept any non-empty flag name string up to 256 characters without rejecting it at runtime, regardless of whether it conforms to the `{Domain}.{Feature}` convention.
3. WHEN a flag name that does not conform to the `{Domain}.{Feature}` convention is registered, THE Feature_Flag_Module SHALL log a warning message indicating the non-conforming flag name and the expected format.
4. THE Feature_Flag_Module SHALL include the naming convention definition and at least one usage example in the XML documentation comments on the public flag registration API.

### Requirement 14: Eventual Consistency

**User Story:** As a domain developer, I want to understand the consistency model for flag changes, so that I can design my feature gating with appropriate expectations.

#### Acceptance Criteria

1. THE Feature_Flag_Module SHALL document that flag changes from AppConfig are eventually consistent with a maximum propagation delay equal to the configured polling interval, where the polling interval is configurable between 10 seconds and 300 seconds with a default of 30 seconds.
2. WHILE AppConfig is being polled, THE Feature_Flag_Module SHALL continue serving the previously cached flag values and respond to flag evaluation requests within 5 milliseconds without blocking on the poll operation.
3. IF AppConfig is unreachable during a poll cycle, THEN THE Feature_Flag_Module SHALL continue serving the last successfully retrieved flag values and log a warning indicating the failed poll attempt.
4. WHEN the Feature_Flag_Module starts and no prior cached values exist, THE Feature_Flag_Module SHALL serve flag values from the local configuration source (appsettings) until the first successful AppConfig poll completes.

### Requirement 15: Resilience

**User Story:** As a platform engineer, I want the Feature Flag module to handle transient AWS failures gracefully, so that flag evaluations remain available during network issues.

#### Acceptance Criteria

1. THE FeatureFlagOptions SHALL include a `Resilience` property of type `ResilienceOptions` for configuring retry, circuit breaker, and timeout policies with the following defaults: `Enabled` = true, `RetryCount` = 3, `RetryDelayMs` = 1000 (exponential backoff with jitter), `MaxRetryDelayMs` = 30000, `CircuitBreakerFailureThreshold` = 0.5, `CircuitBreakerMinimumThroughput` = 10, `CircuitBreakerDurationSeconds` = 60, `CircuitBreakerSamplingDurationSeconds` = 30, and `TimeoutSeconds` = 30.
2. WHEN AppConfig polling encounters a transient error (HTTP 429, HTTP 500-599, `HttpRequestException`, `SocketException`, `IOException`, `TimeoutException`, or AWS throttling errors), THE Feature_Flag_Module SHALL retry with exponential backoff and jitter up to the configured `RetryCount` (default 3 attempts) before treating the operation as failed.
3. IF resilience retries are exhausted, THEN THE Feature_Flag_Module SHALL fall back to the last successfully retrieved configuration values and continue serving flag evaluations without throwing an exception.
4. IF resilience retries are exhausted and no prior configuration has been successfully retrieved (cold start), THEN THE Feature_Flag_Module SHALL evaluate all flags as disabled and log a warning indicating AppConfig is unreachable.
5. IF `Resilience.Enabled` is set to false, THEN THE Feature_Flag_Module SHALL execute AppConfig polling directly without retry, circuit breaker, or timeout wrapping.
