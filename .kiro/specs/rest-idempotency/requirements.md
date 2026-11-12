# Requirements Document

## Introduction

The Genesis REST Idempotency module provides idempotent request handling for REST API endpoints within the Pervaxis platform. It ensures that duplicate HTTP requests (caused by retries, network issues, or client errors) produce the same response without re-executing side effects. The module uses an `Idempotency-Key` header to identify duplicate requests and stores response records in a backing store (DynamoDB for AWS). This module follows existing Genesis patterns for DI registration, options validation, observability, resilience, and multi-tenancy. It is opt-in per endpoint via attributes or middleware configuration, and selectable in Forge when generating REST service prints.

## Glossary

- **Idempotency_Module**: The Genesis abstraction library (`Pervaxis.Genesis.Idempotency`) that defines the contracts, options, and extension methods for idempotent request handling.
- **Idempotency_AWS_Module**: The AWS implementation library (`Pervaxis.Genesis.Idempotency.AWS`) that implements idempotency record storage using DynamoDB.
- **Idempotency_Key**: An HTTP request header (`Idempotency-Key`) containing a client-generated unique identifier (UUID/GUID format) that identifies a specific operation intent.
- **Idempotency_Record**: A stored entry containing the idempotency key, request fingerprint, response status code, response headers, response body, creation timestamp, and expiration timestamp.
- **Idempotency_Store**: The abstraction interface (`IIdempotencyStore`) for persisting and retrieving Idempotency_Records.
- **DynamoDB_Store**: The AWS DynamoDB implementation of the Idempotency_Store that uses a dedicated table with TTL-based automatic expiration.
- **IdempotencyOptions**: The options class extending `GenesisOptionsBase` that configures the Idempotency module (table name, TTL, header name, key validation rules).
- **Idempotent_Attribute**: An ASP.NET Core action filter attribute (`[Idempotent]`) that enables idempotency handling on individual controller actions.
- **Idempotency_Middleware**: An ASP.NET Core middleware component that intercepts HTTP requests and applies idempotency logic for configured endpoints.
- **Request_Fingerprint**: A hash computed from the HTTP method, route, and optionally the request body that ensures the same idempotency key is not reused for different operations.
- **TTL**: Time-To-Live — the duration after which an Idempotency_Record expires and is automatically removed from the store.
- **Forge**: The code generation engine that auto-wires Genesis module registration into every generated service.
- **ITenantContext**: The tenant resolution abstraction from `Pervaxis.Core.Abstractions.MultiTenancy` providing current tenant identity.
- **PervaxisMeter**: The static metrics factory from `Pervaxis.Core.Observability.Metrics` used to create counters and histograms.
- **PervaxisActivitySource**: The static tracing source from `Pervaxis.Core.Observability.Tracing` used to create distributed trace activities.
- **In_Flight_Request**: A request that is currently being processed (the first occurrence of an idempotency key where the response has not yet been stored).

## Requirements

### Requirement 1: Module Registration

**User Story:** As a platform engineer, I want to register the Idempotency module using a standard Genesis extension method, so that it integrates consistently with other Genesis modules.

#### Acceptance Criteria

1. THE Idempotency_Module SHALL provide an `AddGenesisIdempotency` extension method on `IServiceCollection` that accepts an `IConfiguration` parameter and returns `IServiceCollection` for method chaining.
2. THE Idempotency_Module SHALL provide an `AddGenesisIdempotency` extension method on `IServiceCollection` that accepts an `Action<IdempotencyOptions>` parameter and returns `IServiceCollection` for method chaining.
3. WHEN `AddGenesisIdempotency` is called, THE Idempotency_Module SHALL register `IIdempotencyStore` in the dependency injection container as a singleton.
4. WHEN `AddGenesisIdempotency` is called, THE Idempotency_Module SHALL register the Idempotency_Middleware and the Idempotent_Attribute action filter in the service container.
5. WHEN `AddGenesisIdempotency` is called with an `IConfiguration` parameter, THE Idempotency_Module SHALL bind options from the `Genesis:Idempotency` configuration section.
6. WHEN `AddGenesisIdempotency` is called with an `Action<IdempotencyOptions>` parameter, THE Idempotency_Module SHALL apply the action delegate to configure the options instance.
7. IF `AddGenesisIdempotency` is called with a null `IServiceCollection`, null `IConfiguration`, or null `Action<IdempotencyOptions>` parameter, THEN THE Idempotency_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.
8. IF `AddGenesisIdempotency` is called multiple times on the same `IServiceCollection`, THEN THE Idempotency_Module SHALL register services using the TryAdd pattern, ensuring idempotent registration without duplicate service descriptors.

### Requirement 2: Options Configuration

**User Story:** As a platform engineer, I want to configure the Idempotency module through a validated options class, so that misconfiguration is caught early at startup.

#### Acceptance Criteria

1. THE IdempotencyOptions SHALL extend `GenesisOptionsBase`.
2. THE IdempotencyOptions SHALL include a `TableName` property of type string for specifying the DynamoDB table name used to store idempotency records, with a default value of `"genesis-idempotency"` and a maximum length of 255 characters.
3. THE IdempotencyOptions SHALL include a `TtlMinutes` property of type integer for specifying how long idempotency records are retained, with a default value of 1440 (24 hours) and a valid range of 1 to 10080 (7 days).
4. THE IdempotencyOptions SHALL include a `HeaderName` property of type string for specifying the HTTP header name that carries the idempotency key, with a default value of `"Idempotency-Key"` and a maximum length of 128 characters.
5. THE IdempotencyOptions SHALL include an `EnableTenantIsolation` property of type boolean with a default value of true.
6. THE IdempotencyOptions SHALL include a `Resilience` property of type `ResilienceOptions` initialized with default values.
7. THE IdempotencyOptions SHALL include a `ValidateRequestFingerprint` property of type boolean with a default value of true, controlling whether the module verifies that a reused idempotency key corresponds to the same HTTP method, route, and body hash.
8. THE IdempotencyOptions `Validate()` method SHALL return false when `base.Validate()` returns false.
9. THE IdempotencyOptions `Validate()` method SHALL return false when `TableName` is null or empty and `UseLocalEmulator` is false.
10. THE IdempotencyOptions `Validate()` method SHALL return false when `TtlMinutes` is less than 1 or greater than 10080.
11. THE IdempotencyOptions `Validate()` method SHALL return false when `HeaderName` is null or empty.
12. THE IdempotencyOptions `Validate()` method SHALL return false when `Resilience.Validate()` returns false.

### Requirement 3: Idempotency Key Handling

**User Story:** As a domain developer, I want the module to extract and validate idempotency keys from incoming requests, so that only well-formed keys are accepted.

#### Acceptance Criteria

1. WHEN an HTTP request arrives at an idempotency-enabled endpoint, THE Idempotency_Module SHALL extract the value of the header specified by `IdempotencyOptions.HeaderName` (default: `Idempotency-Key`).
2. IF the idempotency key header is missing on a request to an idempotency-enabled endpoint, THEN THE Idempotency_Module SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"IDEMPOTENCY_KEY_MISSING"` and a message stating that the idempotency key header is required.
3. IF the idempotency key header value is empty, contains only whitespace, exceeds 256 characters, or contains characters outside the allowed set (alphanumeric characters, hyphens, underscores, and periods), THEN THE Idempotency_Module SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"IDEMPOTENCY_KEY_INVALID"` and a message describing the validation failure.
4. WHEN an idempotency key header value passes validation, THE Idempotency_Module SHALL accept values in UUID/GUID format (with or without hyphens, 32 to 36 characters), as well as arbitrary string identifiers from 1 to 256 characters containing only alphanumeric characters, hyphens, underscores, and periods.
5. IF the request contains multiple values for the idempotency key header, THEN THE Idempotency_Module SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"IDEMPOTENCY_KEY_INVALID"` and a message indicating that exactly one idempotency key value must be provided.

### Requirement 4: Duplicate Request Detection

**User Story:** As a domain developer, I want the module to detect duplicate requests and return cached responses, so that retried operations do not cause unintended side effects.

#### Acceptance Criteria

1. WHEN a request arrives with an Idempotency_Key that has a completed Idempotency_Record in the store, THE Idempotency_Module SHALL return the stored response (status code, headers, and body) without executing the endpoint action.
2. WHEN a request arrives with an Idempotency_Key that has a completed Idempotency_Record, THE Idempotency_Module SHALL include an `Idempotency-Replayed: true` response header to indicate the response was served from cache.
3. WHEN a request arrives with an Idempotency_Key that has no existing record, THE Idempotency_Module SHALL atomically create an In_Flight_Request record in the store, execute the endpoint action, store the response as an Idempotency_Record upon successful completion, and return the response to the caller.
4. WHEN a request arrives with an Idempotency_Key that has an In_Flight_Request record (response not yet stored), THE Idempotency_Module SHALL return HTTP 409 (Conflict) with a problem details response body containing an error code of `"IDEMPOTENCY_KEY_IN_FLIGHT"` and a message indicating the original request is still being processed.
5. THE Idempotency_Module SHALL store the response within the same logical operation as endpoint execution, ensuring that a stored record always corresponds to a completed operation.
6. IF the endpoint action throws an unhandled exception after an In_Flight_Request record has been created, THEN THE Idempotency_Module SHALL delete the In_Flight_Request record from the store, allowing the client to retry the same Idempotency_Key, and SHALL propagate the exception to the normal ASP.NET Core error-handling pipeline without caching the error response.
7. WHEN a request arrives with an Idempotency_Key that has an In_Flight_Request record whose creation timestamp is older than the configured `TtlMinutes` value, THE Idempotency_Module SHALL treat the in-flight record as abandoned, remove it from the store, and process the request as a new operation.

### Requirement 5: Request Fingerprint Validation

**User Story:** As a platform engineer, I want the module to detect when a client reuses an idempotency key for a different operation, so that accidental key collisions are prevented.

#### Acceptance Criteria

1. WHILE `IdempotencyOptions.ValidateRequestFingerprint` is true, THE Idempotency_Module SHALL compute a Request_Fingerprint by concatenating the HTTP method, the route template path (e.g., `/api/users/{id}`), and a SHA-256 hash of the request body (using an empty byte array as input when the request body is null or empty).
2. WHILE `IdempotencyOptions.ValidateRequestFingerprint` is true, WHEN a request arrives with an Idempotency_Key that has an existing completed Idempotency_Record, THE Idempotency_Module SHALL compare the incoming Request_Fingerprint with the stored fingerprint.
3. IF the incoming Request_Fingerprint does not match the stored fingerprint for the same Idempotency_Key, THEN THE Idempotency_Module SHALL return HTTP 422 (Unprocessable Entity) with a problem details response body containing an error code of `"IDEMPOTENCY_KEY_REUSE"` and a message indicating the key was previously used for a different request.
4. WHILE `IdempotencyOptions.ValidateRequestFingerprint` is false, THE Idempotency_Module SHALL skip fingerprint computation and comparison, returning the cached response for any request bearing a known Idempotency_Key regardless of request content.
5. WHILE `IdempotencyOptions.ValidateRequestFingerprint` is true, WHEN a request arrives with an Idempotency_Key that has an existing In_Flight_Request record and the incoming Request_Fingerprint does not match the stored fingerprint, THEN THE Idempotency_Module SHALL return HTTP 422 (Unprocessable Entity) with the `"IDEMPOTENCY_KEY_REUSE"` error code instead of the HTTP 409 conflict response.

### Requirement 6: Record Expiration (TTL)

**User Story:** As a platform engineer, I want idempotency records to expire automatically after a configurable period, so that storage is bounded and keys can be reused after expiration.

#### Acceptance Criteria

1. WHEN an Idempotency_Record is created, THE Idempotency_Module SHALL set an expiration timestamp as a Unix epoch value in seconds equal to the current UTC time plus the configured `TtlMinutes` value (converted to seconds).
2. THE DynamoDB_Store SHALL use DynamoDB's native TTL feature to automatically delete expired Idempotency_Records without requiring application-level cleanup.
3. WHEN a request arrives with an Idempotency_Key whose record exists in the store but has an expiration timestamp earlier than or equal to the current UTC time, THE Idempotency_Module SHALL treat the record as non-existent, proceed as a new operation, and overwrite the expired record with a fresh Idempotency_Record using a conditional write that treats expired items as absent.
4. THE Idempotency_Module SHALL not return cached responses from expired records, even if DynamoDB TTL deletion has not yet physically removed the item (the module SHALL compare the record's expiration timestamp against the current UTC time before returning a cached response).

### Requirement 7: Tenant Isolation

**User Story:** As a platform engineer, I want idempotency records to be isolated per tenant, so that one tenant's idempotency keys do not conflict with another tenant's keys.

#### Acceptance Criteria

1. WHILE `IdempotencyOptions.EnableTenantIsolation` is true AND `ITenantContext` is resolved with a non-null, non-empty tenant ID, THE Idempotency_Module SHALL construct the Idempotency_Record storage key by concatenating the tenant ID, a `#` separator character, and the Idempotency_Key value, ensuring that the same Idempotency_Key value used by different tenants produces separate records.
2. WHILE `IdempotencyOptions.EnableTenantIsolation` is true AND `ITenantContext` is not resolved (null, not registered, or returns a null/empty tenant ID), THE Idempotency_Module SHALL construct the storage key using the literal prefix `__global__#` followed by the Idempotency_Key value, storing the record in a global partition.
3. WHILE `IdempotencyOptions.EnableTenantIsolation` is false, THE Idempotency_Module SHALL construct the storage key using the literal prefix `__global__#` followed by the Idempotency_Key value for all idempotency records, regardless of whether `ITenantContext` is resolved.
4. THE DynamoDB_Store SHALL use the composite storage key (tenant ID or `__global__` prefix, separator, and idempotency key) as the DynamoDB partition key, enabling per-tenant record retrieval without cross-tenant scanning.
5. IF the resolved tenant ID contains the `#` separator character, THEN THE Idempotency_Module SHALL reject the request with HTTP 400 (Bad Request) with a problem details response body containing an error code of `"IDEMPOTENCY_TENANT_INVALID"` and a message indicating the tenant ID contains disallowed characters.

### Requirement 8: Opt-In Per Endpoint — Attribute

**User Story:** As a domain developer, I want to enable idempotency on specific controller actions using an attribute, so that I can selectively protect endpoints that cause side effects.

#### Acceptance Criteria

1. THE Idempotency_Module SHALL provide an `[Idempotent]` attribute decorated with `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]` that can be applied to individual ASP.NET Core controller actions.
2. WHEN a controller action is decorated with `[Idempotent]`, THE Idempotency_Module SHALL apply idempotency handling (key extraction, duplicate detection, response caching) to requests targeting that action.
3. WHEN a controller action is not decorated with `[Idempotent]` and no middleware-level configuration targets it, THE Idempotency_Module SHALL not intercept or modify the request or response for that action.
4. THE `[Idempotent]` attribute SHALL accept an optional `TtlMinutes` parameter of type integer with a default value of 0, where 0 means "use the global `IdempotencyOptions.TtlMinutes` value" and any non-zero value overrides the global setting for that specific endpoint.
5. IF the `[Idempotent]` attribute's `TtlMinutes` parameter is set to a non-zero value outside the range 1 to 10080, THEN THE Idempotency_Module SHALL return HTTP 500 (Internal Server Error) on the first request to that endpoint and log an error indicating an invalid per-endpoint TTL configuration.
6. THE `[Idempotent]` attribute SHALL accept an optional `ValidateFingerprint` parameter of type nullable boolean with a default value of null, where null means "use the global `IdempotencyOptions.ValidateRequestFingerprint` value" and an explicit true or false overrides the global setting for that specific endpoint.
7. WHEN both the `[Idempotent]` attribute and the Idempotency_Middleware route pattern target the same endpoint, THE Idempotency_Module SHALL apply idempotency handling exactly once, with attribute-level parameter overrides (`TtlMinutes`, `ValidateFingerprint`) taking precedence over middleware-level and global configuration.

### Requirement 9: Opt-In Per Endpoint — Middleware Configuration

**User Story:** As a platform engineer, I want to configure idempotency for groups of endpoints via middleware, so that I can apply it broadly without decorating individual actions.

#### Acceptance Criteria

1. THE Idempotency_Module SHALL provide a `UseGenesisIdempotency` extension method on `IApplicationBuilder` that accepts an `Action<IdempotencyMiddlewareOptions>` parameter for configuring route patterns and HTTP method filters, and registers the Idempotency_Middleware in the request pipeline.
2. THE Idempotency_Middleware SHALL accept a route pattern configuration as a list of ASP.NET Core route template patterns (supporting literal segments and `{parameter}` placeholders, e.g., `"/api/orders/{id}"`) that determines which endpoints are idempotency-enabled.
3. WHEN a request's path matches a configured route pattern using case-insensitive comparison, THE Idempotency_Middleware SHALL apply idempotency handling to that request.
4. WHEN a request does not match any configured route pattern and the action is not decorated with `[Idempotent]`, THE Idempotency_Middleware SHALL pass the request through without idempotency processing.
5. WHEN a request matches both a configured middleware route pattern and the action is decorated with `[Idempotent]`, THE Idempotency_Middleware SHALL defer to the `[Idempotent]` attribute configuration, allowing attribute-level `TtlMinutes` and `ValidateFingerprint` overrides to take precedence over middleware defaults.
6. THE Idempotency_Middleware SHALL support HTTP method filtering, applying idempotency handling only to the configured HTTP methods (default: POST and PATCH), and SHALL ignore requests using non-configured HTTP methods even if the route pattern matches.
7. IF `UseGenesisIdempotency` is called with a null `IApplicationBuilder` or null options action, THEN THE Idempotency_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.

### Requirement 10: Idempotency Store Abstraction

**User Story:** As a platform engineer, I want the idempotency store defined as an abstraction, so that different backing stores can be swapped without changing application code.

#### Acceptance Criteria

1. THE Idempotency_Module SHALL define an `IIdempotencyStore` interface with methods: `TryGetRecordAsync(string tenantId, string idempotencyKey, CancellationToken)`, `CreateInFlightRecordAsync(string tenantId, string idempotencyKey, string fingerprint, CancellationToken)`, `CompleteRecordAsync(string tenantId, string idempotencyKey, IdempotencyRecord record, CancellationToken)`, and `DeleteRecordAsync(string tenantId, string idempotencyKey, CancellationToken)`.
2. THE `TryGetRecordAsync` method SHALL return a nullable `IdempotencyRecord` — returning the record if one exists and has not expired, or null if no record exists or the record's expiration timestamp is in the past.
3. THE `CreateInFlightRecordAsync` method SHALL atomically create an in-flight record only if no unexpired record currently exists for the given tenant and key combination, returning a boolean indicating success (true) or conflict (false). An expired record for the same tenant and key SHALL be treated as nonexistent, allowing creation to succeed.
4. THE `CompleteRecordAsync` method SHALL update an existing in-flight record with the completed response data (status code, response headers, response body, and expiration timestamp). IF no in-flight record exists for the given tenant and key combination (record missing or already completed), THEN the method SHALL return false to indicate the update was not applied.
5. THE `DeleteRecordAsync` method SHALL remove the record for the given tenant and key combination if one exists. IF no record exists for the given tenant and key combination, THEN the method SHALL complete successfully as a no-op.
6. THE `IIdempotencyStore` interface SHALL require all method implementations to be thread-safe, allowing concurrent calls with different tenant and key combinations without data corruption.

### Requirement 11: DynamoDB Store Implementation

**User Story:** As a platform engineer, I want the AWS implementation to use DynamoDB for record storage, so that idempotency records are durable and scalable.

#### Acceptance Criteria

1. THE DynamoDB_Store SHALL implement `IIdempotencyStore` using the AWS DynamoDB SDK.
2. THE DynamoDB_Store SHALL use conditional writes (`ConditionExpression`) for `CreateInFlightRecordAsync` to guarantee atomic record creation, returning false when a `ConditionalCheckFailedException` indicates a record already exists for the given tenant and key combination.
3. THE DynamoDB_Store SHALL configure the DynamoDB table with a TTL attribute mapped to the record's expiration timestamp (stored as a Unix epoch seconds value), enabling automatic cleanup by DynamoDB.
4. THE DynamoDB_Store SHALL store response bodies up to 400 KB (DynamoDB item size limit).
5. IF the serialized DynamoDB item for a response exceeds 400 KB during `CompleteRecordAsync`, THEN THE DynamoDB_Store SHALL return an error result indicating the payload size limit was exceeded, and the endpoint response SHALL still be returned to the client without being cached as an Idempotency_Record.
6. IF `IdempotencyOptions.UseLocalEmulator` is true AND the configured DynamoDB table does not exist, THEN THE DynamoDB_Store SHALL create the table automatically on first use with the configured `TableName`, the required composite key schema (tenant ID and idempotency key), and the TTL attribute.
7. THE DynamoDB_Store SHALL use the `GenesisResiliencePipelineBuilder` to wrap all DynamoDB SDK calls with the configured retry, circuit breaker, and timeout policies.

### Requirement 12: Observability — Metrics

**User Story:** As an SRE, I want metrics on idempotency request handling, so that I can monitor cache hit rates, conflicts, and error conditions.

#### Acceptance Criteria

1. WHEN an idempotency-enabled request completes processing (outcome determined), THE Idempotency_Module SHALL increment the `genesis.idempotency.requests` counter metric by 1, tagged with `outcome` (values: `new`, `replayed`, `conflict`, `key_invalid`, `key_missing`, `fingerprint_mismatch`), `http_method`, and `endpoint`.
2. WHEN a store operation (`get`, `create`, `complete`, or `delete`) finishes, THE Idempotency_Module SHALL record the elapsed wall-clock time in the `genesis.idempotency.store.duration` histogram metric in milliseconds, tagged with `operation` (values: `get`, `create`, `complete`, `delete`) and `outcome` (values: `success`, `error`).
3. WHILE `EnableTenantIsolation` is true AND `ITenantContext` is resolved, THE Idempotency_Module SHALL include a `tenant_id` tag on all emitted metrics; IF `EnableTenantIsolation` is false OR `ITenantContext` is not resolved, THEN THE Idempotency_Module SHALL omit the `tenant_id` tag.
4. THE Idempotency_Module SHALL create all metrics as `static readonly` fields using `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>` with the unit parameter set to `"1"` for counters and `"ms"` for histograms.
5. IF metric emission fails for any reason, THEN THE Idempotency_Module SHALL suppress the failure silently without affecting the request outcome or throwing an exception to the caller.

### Requirement 13: Observability — Tracing

**User Story:** As an SRE, I want distributed trace spans for idempotency operations, so that I can understand the request flow and diagnose latency issues.

#### Acceptance Criteria

1. WHEN an idempotency-enabled request is processed, THE Idempotency_Module SHALL create a trace activity using `PervaxisActivitySource` with span name `idempotency.process` and ActivityKind `Internal`.
2. WHEN the `idempotency.process` trace activity is created, THE Idempotency_Module SHALL set the following tags on the activity: `idempotency.key` (the idempotency key value), `idempotency.outcome` (set upon completion, using the same values as the metrics `outcome` tag: `new`, `replayed`, `conflict`, `key_invalid`, `key_missing`, `fingerprint_mismatch`), `http.method` (the HTTP method of the request), and `http.route` (the matched route template).
3. WHEN `ITenantContext` is resolved, THE Idempotency_Module SHALL set `tenant.id` (from `ITenantContext.TenantId.Value`) and `tenant.name` (from `ITenantContext.TenantName`) tags on the `idempotency.process` trace activity.
4. WHEN a store operation is performed, THE Idempotency_Module SHALL create a child trace activity with span name `idempotency.store.{operation}` (where operation is `get`, `create`, `complete`, or `delete`) and ActivityKind `Client`, including the tag `idempotency.key` with the current key value.
5. IF a store operation fails, THEN THE child `idempotency.store.{operation}` trace activity SHALL set its status to `ActivityStatusCode.Error` with the exception message as the status description.
6. IF `PervaxisActivitySource` has no registered listeners, THEN THE Idempotency_Module SHALL skip all tracing operations without affecting request processing (null-safe activity pattern using `activity?.SetTag` and `activity?.SetStatus`).

### Requirement 14: Observability — Logging

**User Story:** As an SRE, I want structured logging for idempotency events, so that I can troubleshoot duplicate request handling.

#### Acceptance Criteria

1. WHEN a duplicate request is detected and a cached response is returned, THE Idempotency_Module SHALL emit a structured log at Information level containing the idempotency key, endpoint, HTTP method, original request timestamp, and tenant ID (when `ITenantContext` is resolved).
2. WHEN a new idempotency record is created and the response is stored, THE Idempotency_Module SHALL emit a structured log at Debug level containing the idempotency key, endpoint, HTTP method, response status code, TTL expiration timestamp, and tenant ID (when `ITenantContext` is resolved).
3. WHEN a request fingerprint mismatch is detected, THE Idempotency_Module SHALL emit a structured log at Warning level containing the idempotency key, endpoint, expected fingerprint hash, actual fingerprint hash, and tenant ID (when `ITenantContext` is resolved).
4. WHEN a request arrives while an in-flight record exists (conflict), THE Idempotency_Module SHALL emit a structured log at Warning level containing the idempotency key, endpoint, the age of the in-flight record in milliseconds, and tenant ID (when `ITenantContext` is resolved).
5. IF a store operation fails, THEN THE Idempotency_Module SHALL emit a structured log at Error level containing the idempotency key, operation type (get, create, complete, or delete), exception type, exception message, and tenant ID (when `ITenantContext` is resolved).
6. WHEN a request is rejected due to a missing or invalid idempotency key, THE Idempotency_Module SHALL emit a structured log at Warning level containing the endpoint, HTTP method, the validation failure reason (missing, empty, exceeds length, or invalid characters), and tenant ID (when `ITenantContext` is resolved).
7. THE Idempotency_Module SHALL emit all structured logs using `ILogger<T>` with compile-time source-generated `LoggerMessage` methods, ensuring log field names are consistent across all criteria and queryable as discrete structured properties.

### Requirement 15: Resilience

**User Story:** As a platform engineer, I want the Idempotency module to handle transient DynamoDB failures gracefully, so that endpoint availability is not degraded by temporary store issues.

#### Acceptance Criteria

1. THE IdempotencyOptions SHALL include a `Resilience` property of type `ResilienceOptions` for configuring retry, circuit breaker, and timeout policies with the following defaults: `Enabled` = true, `RetryCount` = 3, `RetryDelayMs` = 1000 (exponential backoff with jitter), `MaxRetryDelayMs` = 30000, `CircuitBreakerFailureThreshold` = 0.5, `CircuitBreakerMinimumThroughput` = 10, `CircuitBreakerDurationSeconds` = 60, `CircuitBreakerSamplingDurationSeconds` = 30, and `TimeoutSeconds` = 30.
2. WHEN a DynamoDB operation encounters a transient error (HTTP 429, HTTP 500-599, `ProvisionedThroughputExceededException`, `InternalServerErrorException`, `HttpRequestException`, `SocketException`, `IOException`, or `TimeoutException`), THE DynamoDB_Store SHALL retry with exponential backoff and jitter (full jitter: delay randomized between 0 and the computed exponential delay) up to the configured `RetryCount` (default 3 attempts).
3. IF resilience retries are exhausted during a `TryGetRecordAsync` call, THEN THE Idempotency_Module SHALL allow the request to proceed as a new request (fail-open) and log a warning indicating the store was unreachable.
4. IF resilience retries are exhausted during a `CreateInFlightRecordAsync` or `CompleteRecordAsync` call, THEN THE Idempotency_Module SHALL allow the endpoint response to return to the client and log an error indicating the idempotency record could not be persisted.
5. IF `Resilience.Enabled` is set to false, THEN THE Idempotency_Module SHALL execute DynamoDB operations directly without retry, circuit breaker, or timeout wrapping.
6. WHEN the circuit breaker transitions to the open state, THE Idempotency_Module SHALL fail-open for all store operations (behave as if the store is unreachable) and log a warning indicating the circuit breaker is open and idempotency protection is temporarily degraded.
7. IF resilience retries are exhausted during a `DeleteRecordAsync` call (in-flight record cleanup after endpoint failure), THEN THE Idempotency_Module SHALL log an error indicating the orphaned in-flight record and allow the exception to propagate normally to the client.

### Requirement 16: Forge Integration

**User Story:** As a platform engineer, I want the Idempotency module to be selectable in Forge when generating REST service prints, so that generated services can opt into idempotency support.

#### Acceptance Criteria

1. THE Idempotency_Module SHALL be registered as an optional module in the Forge module catalog with the identifier `"Idempotency"` and category `"REST Infrastructure"`.
2. WHEN a user selects the Idempotency module in the Forge UI, THE generated service print SHALL include the `Pervaxis.Genesis.Idempotency.AWS` NuGet package reference in the service project file.
3. WHEN a user selects the Idempotency module in the Forge UI, THE generated service print SHALL include `AddGenesisIdempotency` registration in `Program.cs` with configuration binding to the `Genesis:Idempotency` section, and `UseGenesisIdempotency` middleware registration in the application pipeline.
4. WHEN a user selects the Idempotency module in the Forge UI, THE generated service print SHALL include a default `Genesis:Idempotency` configuration section in `appsettings.json` containing: `TableName` set to `"genesis-idempotency"`, `TtlMinutes` set to `1440`, `HeaderName` set to `"Idempotency-Key"`, `EnableTenantIsolation` set to `true`, and `ValidateRequestFingerprint` set to `true`.
5. WHEN a user does not select the Idempotency module in the Forge UI, THE generated service print SHALL not contain any idempotency-related code, packages, or configuration.
6. WHEN a user selects the Idempotency module in the Forge UI, THE generated service print SHALL include a `Genesis:Idempotency:Resilience` sub-section in `appsettings.json` with defaults: `Enabled` set to `true`, `RetryCount` set to `3`, `RetryDelayMs` set to `1000`, and `TimeoutSeconds` set to `30`.

### Requirement 17: Local Development Support

**User Story:** As a developer, I want to use the Idempotency module locally without AWS dependencies, so that I can develop and test idempotency behavior offline.

#### Acceptance Criteria

1. WHEN the application starts with `UseLocalEmulator` set to true in IdempotencyOptions, THE DynamoDB_Store SHALL connect to a local DynamoDB instance at the configured `LocalStackUrl` (default: `http://localhost:4566`).
2. WHEN the application starts with `UseLocalEmulator` set to true AND the idempotency table does not exist at the configured `LocalStackUrl`, THE DynamoDB_Store SHALL create the table automatically using the configured `TableName`, the required composite key schema (tenant ID + idempotency key), and a TTL attribute.
3. IF `UseLocalEmulator` is true AND the local DynamoDB instance does not respond within 5 seconds of the initial connection attempt, THEN THE Idempotency_Module SHALL fall back to an in-memory store implementation and log a warning at Warning level indicating local persistence is unavailable and the in-memory store is active.
4. THE in-memory store fallback SHALL implement `IIdempotencyStore` with a `ConcurrentDictionary` backing, honor the configured TTL by checking record expiration on retrieval, guarantee atomicity on `CreateInFlightRecordAsync` using `ConcurrentDictionary.TryAdd`, and evict expired entries when the store exceeds 10000 records.
5. IF `UseLocalEmulator` is true AND table creation fails due to insufficient permissions or a connection error, THEN THE Idempotency_Module SHALL fall back to the in-memory store implementation and log a warning indicating the reason for fallback.
