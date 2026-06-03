# Requirements Document

## Introduction

The Genesis Transactional Logging module (`Pervaxis.Genesis.TransactionalLogging.AWS`) provides audit-grade, per-request operation logging across all Genesis providers within the Pervaxis platform. It automatically captures every Genesis provider call (caching, messaging, file storage, search, notifications, workflow, AI assistance, reporting) into a structured, persistent transaction log — giving platform teams full visibility into what happened during each request without any developer effort.

The module follows a dual-mode consumption model:
- **Implicit mode** (default): A middleware + provider interceptors automatically capture all Genesis provider operations per request scope. Zero code from vertical developers.
- **Explicit mode** (on-demand): Developers inject `ITransactionLog` to record custom business-level entries (e.g., "refund initiated", "approval granted") into the same transaction log.

Both modes produce a unified, chronological audit trail per request — queryable by tenant, business keys, date range, and transaction ID. Storage uses DynamoDB for hot/recent queries and S3 for long-term compliance archival with auto-tiering.

This module is audit-only in v1. No saga orchestration, compensating transactions, or event replay capability is included.

## Glossary

- **TransactionalLogging_Module**: The Genesis library (`Pervaxis.Genesis.TransactionalLogging.AWS`) that provides implicit and explicit transaction logging infrastructure.
- **ITransactionLog**: The interface from `Pervaxis.Core.Abstractions.Genesis.Modules` consumed by vertical services for explicit logging. Implemented by this module.
- **Transaction_Context**: A scoped context created per HTTP request (or per explicit scope) that accumulates operation entries and metadata for one logical transaction.
- **Transaction_Entry**: A single operation record within a transaction log — contains provider name, operation, timestamp, duration, result, and sanitized parameters.
- **TransactionalLoggingOptions**: The options class extending `GenesisOptionsBase` that configures the module (storage, capture rules, retention, tiering).
- **Implicit_Capture**: Automatic recording of Genesis provider calls via interceptors/decorators without developer code.
- **Explicit_Capture**: Developer-initiated recording of business-level events via `ITransactionLog.RecordEntryAsync()`.
- **Hot_Store**: DynamoDB table for recent transaction logs (fast queries, configurable retention).
- **Cold_Store**: S3 bucket for long-term archival (compliance, cost-effective, queryable via Athena).
- **Auto_Tiering**: Automatic movement of transaction logs from Hot_Store to Cold_Store after configurable retention period.
- **ITenantContext**: The tenant resolution abstraction from `Pervaxis.Core.Abstractions.MultiTenancy` providing current tenant identity.
- **PervaxisMeter**: The static metrics factory from `Pervaxis.Core.Observability.Metrics` used to create counters and histograms.
- **PervaxisActivitySource**: The static tracing source from `Pervaxis.Core.Observability.Tracing` used to create distributed trace activities.
- **Forge**: The code generation engine that auto-wires Genesis module registration into every generated service.

## Requirements

### Requirement 1: Module Registration

**User Story:** As a platform engineer, I want to register the Transactional Logging module using a standard Genesis extension method, so that it integrates consistently with other Genesis modules.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL provide an `AddGenesisTransactionalLogging` extension method on `IServiceCollection` that accepts an `IConfiguration` parameter and returns `IServiceCollection` for method chaining.
2. THE TransactionalLogging_Module SHALL provide an `AddGenesisTransactionalLogging` extension method on `IServiceCollection` that accepts an `Action<TransactionalLoggingOptions>` parameter and returns `IServiceCollection` for method chaining.
3. WHEN `AddGenesisTransactionalLogging` is called, THE TransactionalLogging_Module SHALL register `ITransactionLog` in the dependency injection container as a scoped service (one instance per HTTP request scope).
4. WHEN `AddGenesisTransactionalLogging` is called, THE TransactionalLogging_Module SHALL register the provider interceptors that enable implicit capture for all Genesis providers present in the service collection.
5. WHEN `AddGenesisTransactionalLogging` is called with an `IConfiguration` parameter, THE TransactionalLogging_Module SHALL bind options from the `Genesis:TransactionalLogging` configuration section.
6. IF `AddGenesisTransactionalLogging` is called with a null `IServiceCollection`, null `IConfiguration`, or null `Action<TransactionalLoggingOptions>` parameter, THEN THE TransactionalLogging_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.
7. WHEN `AddGenesisTransactionalLogging` is called, THE TransactionalLogging_Module SHALL register the `TransactionLoggingMiddleware` for automatic scope creation per HTTP request.
8. IF `AddGenesisTransactionalLogging` is called multiple times on the same `IServiceCollection`, THEN THE TransactionalLogging_Module SHALL register services using the TryAdd pattern, ensuring idempotent registration without duplicate service descriptors.

### Requirement 2: Options Configuration

**User Story:** As a platform engineer, I want to configure the Transactional Logging module through a validated options class, so that I can control capture behavior, storage, and retention per service.

#### Acceptance Criteria

1. THE TransactionalLoggingOptions SHALL extend `GenesisOptionsBase`.
2. THE TransactionalLoggingOptions SHALL include an `Enabled` property of type boolean with a default value of true, controlling whether the module is active at runtime.
3. THE TransactionalLoggingOptions SHALL include an `ImplicitCapture` property of type boolean with a default value of true, controlling whether provider interceptors automatically record operations.
4. THE TransactionalLoggingOptions SHALL include a `CaptureProviders` property of type `List<string>` with a default value of an empty list (meaning all providers), specifying which Genesis providers to capture implicitly.
5. THE TransactionalLoggingOptions SHALL include an `ExcludeProviders` property of type `List<string>` with a default value of an empty list, specifying which Genesis providers to exclude from implicit capture.
6. THE TransactionalLoggingOptions SHALL include an `ExcludeOperations` property of type `List<string>` with a default value of an empty list, specifying provider operations to exclude (e.g., "cache.get").
7. THE TransactionalLoggingOptions SHALL include a `MinimumDurationMs` property of type integer with a default value of 0, specifying the minimum operation duration in milliseconds before an entry is captured implicitly.
8. THE TransactionalLoggingOptions SHALL include a `TableName` property of type string with a default value of "genesis-transaction-logs" for the DynamoDB hot store table name.
9. THE TransactionalLoggingOptions SHALL include a `BucketName` property of type string with a default value of "genesis-transaction-logs-archive" for the S3 cold store bucket name.
10. THE TransactionalLoggingOptions SHALL include a `HotRetentionDays` property of type integer with a default value of 30 and a valid range of 1 to 365, specifying how long records stay in DynamoDB before archival.
11. THE TransactionalLoggingOptions SHALL include a `ColdRetentionDays` property of type integer with a default value of 2555 (7 years) and a valid range of 30 to 3650, specifying how long records stay in S3.
12. THE TransactionalLoggingOptions SHALL include an `EnableTenantIsolation` property of type boolean with a default value of true.
13. THE TransactionalLoggingOptions SHALL include a `Resilience` property of type `ResilienceOptions` initialized with default values.
14. THE TransactionalLoggingOptions SHALL include a `SanitizeParameters` property of type boolean with a default value of true, controlling whether operation parameters are sanitized (sensitive values redacted) before storage.
15. THE TransactionalLoggingOptions `Validate()` method SHALL return false when `Enabled` is true and `TableName` is null or empty and `UseLocalEmulator` is false.
16. THE TransactionalLoggingOptions `Validate()` method SHALL return false when `HotRetentionDays` is less than 1 or greater than 365.
17. THE TransactionalLoggingOptions `Validate()` method SHALL return false when `ColdRetentionDays` is less than 30 or greater than 3650.
18. THE TransactionalLoggingOptions `Validate()` method SHALL return false when `ColdRetentionDays` is less than `HotRetentionDays`.
19. THE TransactionalLoggingOptions `Validate()` method SHALL return false when the `Resilience` property fails its own validation.

### Requirement 3: Implicit Capture — Provider Interception

**User Story:** As a platform engineer, I want all Genesis provider calls automatically captured in the transaction log without any developer effort, so that every service gets audit logging for free.

#### Acceptance Criteria

1. WHEN `ImplicitCapture` is true and a Genesis provider method is invoked within an active Transaction_Context, THE TransactionalLogging_Module SHALL automatically record a Transaction_Entry containing the provider name, operation name, start timestamp, duration in milliseconds, result status (success/error), and sanitized input parameters.
2. THE TransactionalLogging_Module SHALL intercept operations on all registered Genesis providers: ICache, IMessaging, IFileStorage, ISearch, INotifications, IWorkflow, IAIAssistant, and IReporting.
3. WHEN `CaptureProviders` is non-empty, THE TransactionalLogging_Module SHALL only intercept providers whose names appear in the list (case-insensitive matching).
4. WHEN `ExcludeProviders` is non-empty, THE TransactionalLogging_Module SHALL skip interception for providers whose names appear in the list (case-insensitive matching).
5. WHEN `ExcludeOperations` is non-empty, THE TransactionalLogging_Module SHALL skip recording for operations matching entries in the list (format: "provider.operation", case-insensitive).
6. WHEN `MinimumDurationMs` is greater than 0, THE TransactionalLogging_Module SHALL only record entries for operations whose duration meets or exceeds the threshold.
7. IF no active Transaction_Context exists when a provider operation executes (e.g., background job without middleware), THE TransactionalLogging_Module SHALL skip recording without throwing an exception.
8. THE provider interceptors SHALL NOT materially affect the performance of the intercepted operation — recording SHALL occur asynchronously after the operation completes, adding no more than 2ms of synchronous overhead per operation.
9. IF recording a Transaction_Entry fails (store unavailable, serialization error), THE TransactionalLogging_Module SHALL log a warning and continue without affecting the original provider operation result.

### Requirement 4: Explicit Capture — Developer API

**User Story:** As a domain developer, I want to record custom business-level events into the transaction log, so that I can audit domain-specific operations alongside infrastructure operations.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL make `ITransactionLog` available for constructor injection in any service within a request scope.
2. WHEN `RecordEntryAsync` is called with a Transaction_Entry, THE TransactionalLogging_Module SHALL append the entry to the current Transaction_Context with the caller-provided operation name, parameters, and an auto-generated timestamp.
3. WHEN `RecordEntryAsync` is called outside of an active Transaction_Context, THE TransactionalLogging_Module SHALL create an ad-hoc transaction context for that entry and persist it independently.
4. THE `ITransactionLog` interface SHALL provide a `RecordEntryAsync` method accepting a `TransactionLogEntry` record and a `CancellationToken`.
5. THE `ITransactionLog` interface SHALL provide a `BeginScopeAsync` method for explicitly starting a named transaction scope (for background jobs or non-HTTP contexts).
6. THE `ITransactionLog` interface SHALL provide a `CompleteScopeAsync` method for finalizing an explicit transaction scope.
7. THE `ITransactionLog` interface SHALL provide a `SuppressCapture` method that returns an `IDisposable` scope within which implicit capture is temporarily disabled.
8. WHEN `TransactionalLoggingOptions.Enabled` is false, ALL `ITransactionLog` methods SHALL no-op without throwing exceptions (null object pattern).

### Requirement 5: Endpoint-Level Control

**User Story:** As a domain developer, I want to suppress transaction logging on specific endpoints (health checks, metrics endpoints), so that I can reduce noise and storage costs.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL provide a `[SuppressTransactionLog]` attribute decorated with `[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]` that suppresses transaction logging for decorated actions or controllers.
2. WHEN a request targets an action or controller decorated with `[SuppressTransactionLog]`, THE TransactionalLogging_Module SHALL not create a Transaction_Context and SHALL skip all implicit capture for that request.
3. WHEN `[SuppressTransactionLog]` is applied to a controller class, THE TransactionalLogging_Module SHALL suppress logging for all actions in that controller.
4. WHEN `[SuppressTransactionLog]` is applied to a specific action within a controller that is NOT class-level suppressed, THE TransactionalLogging_Module SHALL suppress logging only for that specific action.
5. THE TransactionalLogging_Module SHALL support a `SuppressRoutes` property in `TransactionalLoggingOptions` of type `List<string>` for configuring route patterns to suppress via configuration (e.g., "/health", "/metrics").

### Requirement 6: Transaction Context and Scope

**User Story:** As a platform engineer, I want each HTTP request to automatically create a transaction context, so that all operations within the request are grouped into one audit record.

#### Acceptance Criteria

1. WHEN an HTTP request enters the ASP.NET Core pipeline and `Enabled` is true and the endpoint is not suppressed, THE TransactionLoggingMiddleware SHALL create a new Transaction_Context with a unique `TransactionId` (format: `txn_{Guid:N}`).
2. THE Transaction_Context SHALL capture and store: `TransactionId`, `TraceId` (from `Activity.Current`), `TenantId` (from `ITenantContext` when resolved), `HttpMethod`, `RequestPath`, `StartTimestamp` (UTC), and a list of Transaction_Entry records.
3. WHEN the HTTP response is completed, THE TransactionLoggingMiddleware SHALL finalize the Transaction_Context with `EndTimestamp`, `DurationMs`, `HttpStatusCode`, and `Status` (Completed or Failed based on status code).
4. WHEN the Transaction_Context is finalized, THE TransactionalLogging_Module SHALL persist the complete transaction log to the Hot_Store asynchronously (fire-and-forget with error logging on failure).
5. IF an unhandled exception occurs during the request, THE Transaction_Context SHALL be finalized with status `Failed` and the exception type and message recorded (but not the full stack trace, for storage efficiency).
6. THE Transaction_Context SHALL include an `IdempotencyKey` property that is populated from the request's `Idempotency-Key` header (when present) for correlation with the idempotency module.
7. THE Transaction_Context SHALL include a `CorrelationId` property populated from the `X-Correlation-Id` header (when present) for cross-service correlation.
8. THE Transaction_Context SHALL support attaching arbitrary business keys via `AddBusinessKey(string key, string value)` for query support (e.g., OrderId, CustomerId).

### Requirement 7: Storage — Hot Store (DynamoDB)

**User Story:** As an SRE, I want recent transaction logs stored in DynamoDB for fast querying, so that I can investigate issues within minutes of occurrence.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL persist finalized transaction logs to a DynamoDB table using the configured `TableName`.
2. THE DynamoDB table SHALL use a composite key: Partition Key = `{TenantId}#{YYYY-MM-DD}` (or `__global__#{YYYY-MM-DD}` when no tenant), Sort Key = `{TransactionId}`.
3. THE DynamoDB table SHALL include a Global Secondary Index (GSI) with Partition Key = `TransactionId` for direct lookup by transaction ID.
4. THE DynamoDB table SHALL include a Global Secondary Index (GSI) with Partition Key = `{TenantId}#{BusinessKey}`, Sort Key = `Timestamp` for querying by business keys.
5. THE DynamoDB table SHALL include a TTL attribute set to `CreatedAt + HotRetentionDays` (as epoch seconds) for automatic record expiration.
6. WHEN writing a transaction log, THE TransactionalLogging_Module SHALL serialize the list of Transaction_Entry records as a JSON array stored in a single DynamoDB attribute.
7. THE TransactionalLogging_Module SHALL use the `GenesisResiliencePipelineBuilder` for DynamoDB operations with the configured `Resilience` options.
8. IF the transaction log exceeds the DynamoDB item size limit (400KB), THE TransactionalLogging_Module SHALL split the entries into a summary record (in DynamoDB) and a full record (in S3), with the DynamoDB record containing a reference to the S3 object key.

### Requirement 8: Storage — Cold Store (S3)

**User Story:** As a compliance officer, I want transaction logs archived to S3 for long-term retention, so that we can satisfy regulatory audit requirements.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL archive transaction logs from DynamoDB to S3 after the configured `HotRetentionDays` period expires.
2. THE S3 archival SHALL use a key structure of `{TenantId}/{YYYY}/{MM}/{DD}/{TransactionId}.json.gz` for efficient partitioning and Athena queryability.
3. THE S3 objects SHALL be stored with GZIP compression to reduce storage costs.
4. THE S3 bucket SHALL be configured with Intelligent-Tiering or a lifecycle policy transitioning objects to S3 Glacier after 90 days and S3 Glacier Deep Archive after 365 days.
5. THE TransactionalLogging_Module SHALL support S3 Object Lock (Governance mode) for compliance-critical deployments, configurable via `EnableObjectLock` option (default: false).
6. WHEN a transaction log is archived to S3, THE TransactionalLogging_Module SHALL write the complete transaction record including all entries, metadata, and business keys as a single JSON document.
7. THE auto-tiering mechanism SHALL be implemented via DynamoDB Streams triggering a Lambda function (or equivalent) — this is an infrastructure concern documented but not implemented in the Genesis library itself.

### Requirement 9: Querying Transaction Logs

**User Story:** As a developer or SRE, I want to query transaction logs by various keys, so that I can investigate issues and audit specific operations.

#### Acceptance Criteria

1. THE `ITransactionLog` interface SHALL provide a `QueryAsync` method accepting a `TransactionLogQuery` record with optional filters: `TenantId`, `TransactionId`, `TraceId`, `CorrelationId`, `IdempotencyKey`, `BusinessKeys` (dictionary), `FromTimestamp`, `ToTimestamp`, `Status`, `PageSize`, and `ContinuationToken`.
2. WHEN `TransactionId` is provided, THE TransactionalLogging_Module SHALL query the DynamoDB GSI for direct lookup, returning a single transaction log.
3. WHEN `TenantId` and date range are provided, THE TransactionalLogging_Module SHALL query the DynamoDB partition for the matching date range.
4. WHEN a business key query is provided, THE TransactionalLogging_Module SHALL query the business key GSI.
5. THE `QueryAsync` method SHALL return a `TransactionLogQueryResult` containing a list of transaction log summaries, total count, and a continuation token for pagination.
6. THE TransactionalLogging_Module SHALL only query the Hot_Store (DynamoDB) via the `QueryAsync` method. Cold store queries are handled via Athena or direct S3 access (outside the scope of this library).
7. THE `QueryAsync` method SHALL enforce tenant isolation when `EnableTenantIsolation` is true — a query SHALL only return records matching the current tenant from `ITenantContext`.

### Requirement 10: Parameter Sanitization

**User Story:** As a security engineer, I want sensitive parameters automatically redacted from transaction logs, so that we don't persist credentials, tokens, or PII in audit records.

#### Acceptance Criteria

1. WHEN `SanitizeParameters` is true, THE TransactionalLogging_Module SHALL redact parameter values for keys matching common sensitive patterns: any key containing "password", "secret", "token", "key", "credential", "auth", "connectionstring" (case-insensitive matching).
2. WHEN a parameter value is redacted, THE TransactionalLogging_Module SHALL replace the value with the string `"[REDACTED]"` in the stored Transaction_Entry.
3. THE TransactionalLogging_Module SHALL support a `SensitiveKeys` property in `TransactionalLoggingOptions` of type `List<string>` for adding custom sensitive key patterns beyond the defaults.
4. WHEN `SanitizeParameters` is false, THE TransactionalLogging_Module SHALL store parameter values as-is without any redaction.
5. THE TransactionalLogging_Module SHALL never log or store raw request/response bodies — only the structured parameters extracted by the provider interceptors.

### Requirement 11: Observability — Metrics

**User Story:** As an SRE, I want metrics on transaction log recording, so that I can monitor the health and performance of the logging infrastructure itself.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL emit a `genesis.txlog.transactions` counter metric for each finalized transaction, tagged with `status` (completed/failed), `tenant_id` (when resolved), and `capture_mode` (implicit/explicit/mixed).
2. THE TransactionalLogging_Module SHALL emit a `genesis.txlog.entries` counter metric for each recorded entry, tagged with `provider`, `operation`, `result` (success/error), and `capture_type` (implicit/explicit).
3. THE TransactionalLogging_Module SHALL emit a `genesis.txlog.persist.duration` histogram metric measuring the time to persist a transaction log to DynamoDB in milliseconds, tagged with `result` (success/error).
4. THE TransactionalLogging_Module SHALL emit a `genesis.txlog.persist.failures` counter metric for persistence failures, tagged with `reason` (timeout/throttled/size_exceeded/other).
5. THE TransactionalLogging_Module SHALL create all metrics as `static readonly` fields using `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>`.
6. IF metric emission fails, THEN THE TransactionalLogging_Module SHALL suppress the failure without affecting request processing.

### Requirement 12: Observability — Tracing

**User Story:** As an SRE, I want distributed trace spans for transaction log operations, so that I can understand logging overhead in the context of a request.

#### Acceptance Criteria

1. WHEN a Transaction_Context is created, THE TransactionalLogging_Module SHALL create a trace activity using `PervaxisActivitySource` with span name `txlog.scope` and ActivityKind `Internal`.
2. WHEN the Transaction_Context is finalized, THE trace activity SHALL include tags: `txlog.transaction_id`, `txlog.entry_count`, `txlog.duration_ms`, `txlog.status`, and `txlog.capture_mode`.
3. WHEN `ITenantContext` is resolved, THE trace activity SHALL include `tenant.id` and `tenant.name` tags.
4. WHEN a persistence operation occurs, THE TransactionalLogging_Module SHALL create a child trace activity with span name `txlog.persist` and tags `txlog.store` (dynamodb/s3) and `txlog.result`.
5. IF tracing is not configured (no activity listener), THE TransactionalLogging_Module SHALL skip tracing operations without affecting functionality.

### Requirement 13: Observability — Logging

**User Story:** As an SRE, I want structured logging for transaction log lifecycle events, so that I can troubleshoot issues with the logging infrastructure.

#### Acceptance Criteria

1. WHEN a Transaction_Context is created, THE TransactionalLogging_Module SHALL emit a structured log at Debug level containing the TransactionId, TraceId, TenantId, and RequestPath.
2. WHEN a Transaction_Context is finalized and persisted successfully, THE TransactionalLogging_Module SHALL emit a structured log at Debug level containing the TransactionId, EntryCount, DurationMs, and Status.
3. WHEN persistence to DynamoDB fails, THE TransactionalLogging_Module SHALL emit a structured log at Warning level containing the TransactionId, error details, and whether the transaction will be retried.
4. WHEN a transaction log exceeds the DynamoDB size limit and is split to S3, THE TransactionalLogging_Module SHALL emit a structured log at Information level containing the TransactionId, entry count, and S3 object key.
5. THE TransactionalLogging_Module SHALL use source-generated `LoggerMessage` attributes for all log messages to minimize allocation overhead.

### Requirement 14: Resilience and Fail-Open

**User Story:** As a platform engineer, I want the transactional logging module to never impact service availability, so that a logging infrastructure failure does not degrade the business service.

#### Acceptance Criteria

1. IF DynamoDB is unreachable when persisting a transaction log, THE TransactionalLogging_Module SHALL retry using the configured resilience pipeline and, if all retries are exhausted, log a warning and discard the transaction log without affecting the HTTP response.
2. THE TransactionalLogging_Module SHALL never throw an exception that propagates to the caller or affects the HTTP response pipeline — all failures are contained within the module.
3. IF the circuit breaker opens due to sustained DynamoDB failures, THE TransactionalLogging_Module SHALL skip persistence attempts (fail-open) and log a warning indicating degraded audit coverage.
4. WHEN the circuit breaker transitions from open to half-open to closed, THE TransactionalLogging_Module SHALL resume normal persistence and emit an informational log indicating recovery.
5. THE implicit capture interceptors SHALL add no more than 2ms synchronous overhead per intercepted operation. Persistence is asynchronous and off the critical path.

### Requirement 15: Multi-Tenancy

**User Story:** As a platform engineer, I want transaction logs isolated by tenant, so that one tenant's audit data is never accessible to another tenant.

#### Acceptance Criteria

1. WHEN `EnableTenantIsolation` is true AND `ITenantContext` is resolved, THE TransactionalLogging_Module SHALL include the tenant ID in all storage keys (DynamoDB partition key prefix and S3 key prefix).
2. WHEN `EnableTenantIsolation` is true AND `ITenantContext` is NOT resolved, THE TransactionalLogging_Module SHALL use `__global__` as the tenant prefix for storage keys.
3. WHEN querying transaction logs, THE TransactionalLogging_Module SHALL enforce that results only include records matching the current tenant from `ITenantContext`.
4. WHEN `EnableTenantIsolation` is false, THE TransactionalLogging_Module SHALL use `__global__` for all storage keys and not apply tenant filtering on queries.

### Requirement 16: Forge Integration

**User Story:** As a platform engineer, I want Forge to offer transactional logging as a selectable module during service scaffold generation, so that teams can opt-in at project creation time.

#### Acceptance Criteria

1. THE TransactionalLogging_Module SHALL be selectable in Forge as an optional Genesis module (checkbox during service generation).
2. WHEN selected in Forge, THE scaffold SHALL include `AddGenesisTransactionalLogging(builder.Configuration)` in the generated `Program.cs`.
3. WHEN selected in Forge, THE scaffold SHALL include a default `Genesis:TransactionalLogging` configuration section in `appsettings.json` with production-ready defaults.
4. WHEN NOT selected in Forge, THE scaffold SHALL not include any transactional logging references or configuration.

### Requirement 17: Correlation with Idempotency Module

**User Story:** As a platform engineer, I want transaction logs to reference idempotency keys when present, so that I can correlate audit trails with idempotent request tracking.

#### Acceptance Criteria

1. WHEN the `Idempotency-Key` header is present on the request, THE Transaction_Context SHALL capture and store the idempotency key value.
2. WHEN querying transaction logs by `IdempotencyKey`, THE TransactionalLogging_Module SHALL return all transaction logs associated with that key.
3. THE TransactionalLogging_Module SHALL NOT duplicate any data stored by the idempotency module — it stores only a reference (the key value) for correlation.

### Requirement 18: Local Development Support

**User Story:** As a developer, I want transactional logging to work locally without AWS dependencies, so that I can develop and test audit behavior offline.

#### Acceptance Criteria

1. WHEN `UseLocalEmulator` is true, THE TransactionalLogging_Module SHALL use LocalStack for DynamoDB and S3 operations using the configured `LocalStackUrl`.
2. WHEN the environment is "Development", THE TransactionalLogging_Module SHALL create the DynamoDB table and S3 bucket automatically if they do not exist.
3. THE TransactionalLogging_Module SHALL support an `InMemoryTransactionLogStore` for unit testing scenarios, registered when `UseLocalEmulator` is true and LocalStack is unavailable.
4. WHEN running locally, THE TransactionalLogging_Module SHALL log transaction summaries at Debug level to the console for developer visibility.
