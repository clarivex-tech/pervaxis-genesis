# Implementation Plan: OData REST Support

## Overview

Implement the `Pervaxis.Genesis.OData` module providing OData query capabilities for REST API endpoints. The implementation follows existing Genesis conventions for DI registration, options validation, observability, resilience, security controls, and multi-tenancy. The module wraps `Microsoft.AspNetCore.OData` and operates on `IQueryable<T>` expression trees. Implementation uses C# with ASP.NET Core, Entity Framework Core, FsCheck for property-based testing, and xUnit with NSubstitute for unit testing.

## Tasks

- [ ] 1. Set up project structure and core abstractions
  - [ ] 1.1 Create the `Pervaxis.Genesis.OData` project with directory structure and csproj
    - Create `src/Pervaxis.Genesis.OData/Pervaxis.Genesis.OData.csproj` targeting the same framework as other Genesis modules
    - Add NuGet references: `Microsoft.AspNetCore.OData`, `Pervaxis.Core.Abstractions`, `Pervaxis.Core.Observability`
    - Create folder structure: `Abstractions/`, `Configuration/`, `Options/`, `Extensions/`, `Filters/`, `Middleware/`, `Services/`, `Diagnostics/`, `Results/`
    - _Requirements: 1.1, 1.2_

  - [ ] 1.2 Define the `ODataQueryOptions` flags enum
    - Create `Options/ODataQueryOptions.cs` with flags: None, Filter, OrderBy, Select, Expand, Top, Skip, Count, All
    - _Requirements: 2.10, 3.9_

  - [ ] 1.3 Define core interfaces (`IQueryValidator`, `IQueryComplexityCalculator`, `ITenantQueryScopeApplier`, `IPageResultBuilder`)
    - Create `Abstractions/IQueryValidator.cs`, `Abstractions/IQueryComplexityCalculator.cs`, `Abstractions/ITenantQueryScopeApplier.cs`, `Abstractions/IPageResultBuilder.cs`
    - Define method signatures as specified in the design document
    - _Requirements: 4.1–4.10, 9.1–9.6, 8.1–8.7_

  - [ ] 1.4 Define the `IEntityODataConfiguration<TEntity>` interface
    - Create `Abstractions/IEntityODataConfiguration.cs` with fluent methods: `ConfigureFilter`, `ConfigureSort`, `ConfigureSelect`, `ConfigureExpand`, `ExcludeProperty`, `ConfigureTenantProperty`
    - _Requirements: 5.1, 5.2, 5.5, 9.5_

  - [ ] 1.5 Define data model classes (`ODataQueryContext`, `PageResult<T>`, `EntityPropertyDescriptor`, `EffectiveQueryLimits`, `QueryValidationResult`, `TenantScopeResult<T>`)
    - Create `Services/ODataQueryContext.cs`, `Results/PageResult.cs`, `Configuration/EntityPropertyDescriptor.cs`, `Services/EffectiveQueryLimits.cs`, `Services/QueryValidationResult.cs`, `Services/TenantScopeResult.cs`
    - Include JSON serialization attributes on `PageResult<T>` for OData response conventions
    - _Requirements: 8.1, 8.5_

- [ ] 2. Implement options and validation
  - [ ] 2.1 Implement `ODataOptions` extending `GenesisOptionsBase`
    - Create `Options/ODataOptions.cs` with all properties (MaxTop, DefaultPageSize, MaxExpandDepth, MaxFilterDepth, MaxOrderByProperties, EnableTenantIsolation, EnableCount, MaxQueryComplexityScore, AllowedQueryOptions, UseLocalEmulator)
    - Implement `Validate()` method with all range checks per requirements
    - Implement `ConfigureEntityOData<TEntity>` builder method
    - _Requirements: 2.1–2.17_

  - [ ]* 2.2 Write property tests for `ODataOptions.Validate()`
    - **Property 2: Options Validation Correctness**
    - **Validates: Requirements 2.11, 2.12, 2.13, 2.14, 2.15, 2.16, 2.17**

  - [ ] 2.3 Implement `ODataMiddlewareOptions`
    - Create `Options/ODataMiddlewareOptions.cs` with `RoutePatterns` list and `AllowedMethods` hash set (default: GET)
    - _Requirements: 7.1, 7.2, 7.5_

  - [ ]* 2.4 Write property tests for options configuration round-trip
    - **Property 1: Options Configuration Round-Trip**
    - **Validates: Requirements 1.4, 1.5**

- [ ] 3. Implement entity configuration
  - [ ] 3.1 Implement `EntityODataConfiguration<TEntity>` fluent builder
    - Create `Configuration/EntityODataConfiguration.cs` implementing `IEntityODataConfiguration<TEntity>`
    - Support lambda expressions for property references using `Expression<Func<TEntity, object>>`
    - Implement `ExcludeProperty` and `ConfigureTenantProperty` methods
    - _Requirements: 5.1, 5.2, 5.4, 5.5, 9.5_

  - [ ] 3.2 Implement `EntityConfigurationRegistry`
    - Create `Configuration/EntityConfigurationRegistry.cs` to store and resolve `EntityPropertyDescriptor` per entity type
    - Apply default configuration (all primitive properties filterable/sortable/selectable, no navigation expandable) when no explicit configuration exists
    - Detect tenant property by convention (`TenantId` string property)
    - _Requirements: 5.3, 5.6, 9.5, 9.6_

  - [ ]* 3.3 Write unit tests for entity configuration fluent API and default conventions
    - Test ConfigureFilter/Sort/Select/Expand mark correct properties
    - Test ExcludeProperty removes from all operations
    - Test default convention detects all primitive properties
    - Test tenant property convention detection
    - _Requirements: 5.1–5.6, 9.5, 9.6_

- [ ] 4. Implement query complexity calculator and validator
  - [ ] 4.1 Implement `QueryComplexityCalculator`
    - Create `Services/QueryComplexityCalculator.cs` implementing `IQueryComplexityCalculator`
    - Formula: `filterConditions*2 + expandDepth*10 + orderByProperties*3 + (count?5:0) + max(0, selectProperties-5)`
    - _Requirements: 4.6_

  - [ ]* 4.2 Write property tests for `QueryComplexityCalculator`
    - **Property 3: Query Complexity Score Computation**
    - **Validates: Requirements 4.6**

  - [ ] 4.3 Implement `QueryValidator`
    - Create `Services/QueryValidator.cs` implementing `IQueryValidator`
    - Validate $top against MaxTop, $expand depth against MaxExpandDepth, $filter depth against MaxFilterDepth, $orderby count against MaxOrderByProperties, complexity score against MaxQueryComplexityScore
    - Validate property access against `EntityPropertyDescriptor` (filterable, sortable, expandable)
    - Validate AllowedQueryOptions flags
    - Return appropriate error codes per error catalog
    - _Requirements: 4.1–4.10, 3.9_

  - [ ]* 4.4 Write property tests for `QueryValidator` limit enforcement
    - **Property 4: Validation Limit Enforcement**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**

  - [ ]* 4.5 Write property tests for entity property access control
    - **Property 5: Entity Property Access Control**
    - **Validates: Requirements 4.7, 4.8, 4.9, 5.5, 5.6**

  - [ ]* 4.6 Write property tests for disabled query option rejection
    - **Property 10: Disabled Query Option Rejection**
    - **Validates: Requirements 3.9**

- [ ] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. Implement tenant isolation
  - [ ] 6.1 Implement `TenantQueryScopeApplier`
    - Create `Services/TenantQueryScopeApplier.cs` implementing `ITenantQueryScopeApplier`
    - Resolve `ITenantContext` for current tenant ID
    - Apply tenant filter as outermost `Where` clause before any client-supplied queries
    - Return HTTP 403 with `ODATA_TENANT_REQUIRED` when EnableTenantIsolation is true but tenant context missing/empty
    - Skip tenant filtering when EnableTenantIsolation is false
    - Log warning at startup for entities without tenant property
    - _Requirements: 9.1–9.6_

  - [ ]* 6.2 Write property tests for tenant isolation enforcement
    - **Property 13: Tenant Isolation Enforcement**
    - **Validates: Requirements 9.1, 9.2**

- [ ] 7. Implement pagination and response envelope
  - [ ] 7.1 Implement `PageResultBuilder`
    - Create `Services/PageResultBuilder.cs` implementing `IPageResultBuilder`
    - Build `PageResult<T>` with `value`, `@odata.count` (when requested), `@odata.nextLink` (when more pages), `@odata.context`
    - Compute nextLink URL with correct `$skip` value
    - Omit nextLink when `$skip + effectiveTop >= totalCount`
    - Omit `@odata.count` when `$count` not requested or `EnableCount` is false
    - Set Content-Type to `application/json;odata.metadata=minimal`
    - _Requirements: 8.1–8.7_

  - [ ]* 7.2 Write property tests for pagination nextLink correctness
    - **Property 14: Pagination NextLink Correctness**
    - **Validates: Requirements 8.2, 8.6**

  - [ ]* 7.3 Write property tests for `$count` correctness
    - **Property 9: $count Correctness**
    - **Validates: Requirements 3.7, 8.3, 8.4**

  - [ ]* 7.4 Write property tests for `$top` and `$skip` pagination
    - **Property 8: $top and $skip Pagination Correctness**
    - **Validates: Requirements 3.5, 3.6, 3.8**

- [ ] 8. Implement OData query filter and attribute
  - [ ] 8.1 Implement `ODataQueryableAttribute`
    - Create `Filters/ODataQueryableAttribute.cs` as `IFilterFactory`
    - Properties: `MaxTop` (default 0), `MaxExpandDepth` (default -1), `AllowedQueryOptions` (default All)
    - Attribute usage: `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]`
    - Return HTTP 500 with log error if MaxTop is set outside 1-10000 range
    - _Requirements: 6.1–6.8_

  - [ ] 8.2 Implement `ODataQueryFilter` (core query processing pipeline)
    - Create `Filters/ODataQueryFilter.cs` as the `IAsyncActionFilter`/`IAsyncResultFilter`
    - Parse query options using Microsoft.AspNetCore.OData URI parser
    - Resolve effective limits (attribute overrides > middleware > global ODataOptions)
    - Call QueryValidator, TenantQueryScopeApplier, apply query transformations, materialize results
    - Apply query options in correct order: tenant filter → $filter → $orderby → $skip → $top → $select/$expand
    - Detect `IQueryable<T>` from action results (direct, `ActionResult<IQueryable<T>>`, `Task<IQueryable<T>>`)
    - Catch `InvalidOperationException` from non-translatable expressions and return HTTP 400 with `ODATA_QUERY_NOT_TRANSLATABLE`
    - Build `PageResult<T>` response envelope
    - _Requirements: 3.1–3.9, 10.1–10.7, 6.8_

  - [ ]* 8.3 Write property tests for attribute override resolution
    - **Property 12: Attribute Override Resolution**
    - **Validates: Requirements 6.4, 6.5, 6.6, 6.8**

  - [ ]* 8.4 Write property tests for `$filter` application correctness
    - **Property 6: $filter Application Correctness**
    - **Validates: Requirements 3.1**

  - [ ]* 8.5 Write property tests for `$orderby` sorting correctness
    - **Property 7: $orderby Sorting Correctness**
    - **Validates: Requirements 3.2**

- [ ] 9. Implement middleware and route matching
  - [ ] 9.1 Implement `ODataMiddleware`
    - Create `Middleware/ODataMiddleware.cs`
    - Match request path against configured route patterns (case-insensitive, `{parameter}` placeholders)
    - Filter by allowed HTTP methods (default: GET only)
    - Defer to `[ODataQueryable]` attribute when both target the same endpoint
    - Pass non-matching requests through without modification
    - _Requirements: 7.1–7.7_

  - [ ]* 9.2 Write property tests for route pattern matching
    - **Property 11: Route Pattern Matching**
    - **Validates: Requirements 7.2, 7.3, 7.4, 7.5**

- [ ] 10. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Implement observability
  - [ ] 11.1 Implement `ODataMetrics`
    - Create `Diagnostics/ODataMetrics.cs` with static readonly metric fields
    - Counter: `genesis.odata.requests` (tags: outcome, http_method, endpoint, tenant_id when applicable)
    - Histograms: `genesis.odata.query.duration` (ms), `genesis.odata.query.result_count`, `genesis.odata.query.complexity`
    - Use `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>`
    - Suppress metric emission failures silently
    - _Requirements: 11.1–11.7_

  - [ ] 11.2 Implement `ODataTracing`
    - Create `Diagnostics/ODataTracing.cs` with trace activity creation
    - Root span: `odata.query` (ActivityKind.Internal) with tags: odata.query_options, odata.outcome, odata.complexity_score, http.method, http.route, tenant.id, tenant.name
    - Child span: `odata.validate` with tags: odata.complexity_score, odata.validation_result
    - Set status to Error on failure; use null-safe activity pattern
    - _Requirements: 12.1–12.6_

  - [ ] 11.3 Implement `ODataLogMessages` with source-generated `LoggerMessage` methods
    - Create `Diagnostics/ODataLogMessages.cs` with `[LoggerMessage]` attributes
    - Debug: successful query (endpoint, method, options, result count, duration, complexity, tenant)
    - Warning: validation rejection, parse error, tenant isolation block
    - Error: non-translatable query
    - _Requirements: 13.1–13.6_

  - [ ]* 11.4 Write unit tests for metrics emission
    - Verify counter increments and histogram recordings with correct tags
    - Verify tenant_id tag inclusion/exclusion based on EnableTenantIsolation
    - Verify silent failure suppression
    - _Requirements: 11.1–11.7_

  - [ ]* 11.5 Write unit tests for tracing
    - Verify activity creation, tag setting, status on error
    - Verify null-safe pattern when no listeners
    - _Requirements: 12.1–12.6_

  - [ ]* 11.6 Write unit tests for structured logging
    - Verify log levels and structured properties for each scenario
    - _Requirements: 13.1–13.6_

- [ ] 12. Implement DI registration and application builder extensions
  - [ ] 12.1 Implement `ODataServiceCollectionExtensions`
    - Create `Extensions/ODataServiceCollectionExtensions.cs`
    - `AddGenesisOData(IServiceCollection, IConfiguration)` — binds from `Genesis:OData` section
    - `AddGenesisOData(IServiceCollection, Action<ODataOptions>)` — applies delegate
    - Register services using TryAdd pattern for idempotency
    - Configure `Microsoft.AspNetCore.OData` services with EDM model and route prefix
    - Throw `ArgumentNullException` for null parameters
    - _Requirements: 1.1–1.8_

  - [ ] 12.2 Implement `ODataApplicationBuilderExtensions`
    - Create `Extensions/ODataApplicationBuilderExtensions.cs`
    - `UseGenesisOData(IApplicationBuilder, Action<ODataMiddlewareOptions>)` — registers middleware
    - Throw `ArgumentNullException` for null parameters
    - _Requirements: 7.1, 7.6_

  - [ ]* 12.3 Write unit tests for DI registration
    - Verify services registered correctly, TryAdd idempotency, ArgumentNullException for null inputs
    - Verify Microsoft.AspNetCore.OData configured
    - _Requirements: 1.1–1.8_

- [ ] 13. Implement local development support
  - [ ] 13.1 Implement local emulator mode behavior
    - When `UseLocalEmulator` is true: relax limits (MaxTop=1000, MaxExpandDepth=5, MaxFilterDepth=10, MaxQueryComplexityScore=200)
    - Log Information-level message indicating relaxed limits
    - Include raw query, parsed AST, translation details in error response extensions
    - When false: sanitize error messages, enforce standard limits
    - _Requirements: 15.1–15.5_

  - [ ]* 13.2 Write unit tests for local emulator mode
    - Verify relaxed limits applied, detailed error responses, sanitized responses when disabled
    - _Requirements: 15.1–15.5_

- [ ] 14. Implement Forge integration
  - [ ] 14.1 Register OData module in Forge module catalog
    - Register with identifier `"OData"` and category `"REST Infrastructure"`
    - Define code generation templates: NuGet reference, `AddGenesisOData` registration, `UseGenesisOData` middleware, default appsettings section, sample controller action with `[ODataQueryable]`
    - Ensure module is excluded from generated output when not selected
    - _Requirements: 14.1–14.6_

  - [ ]* 14.2 Write unit tests for Forge catalog registration
    - Verify module registered with correct identifier and category
    - Verify generated output includes/excludes OData artifacts based on selection
    - _Requirements: 14.1–14.6_

- [ ] 15. Create test project and integration tests
  - [ ] 15.1 Create `Pervaxis.Genesis.OData.Tests` test project
    - Create `tests/Pervaxis.Genesis.OData.Tests/Pervaxis.Genesis.OData.Tests.csproj`
    - Add references: `xunit`, `FsCheck.Xunit`, `NSubstitute`, `Microsoft.EntityFrameworkCore.InMemory`, project reference to `Pervaxis.Genesis.OData`
    - Set up test folder structure matching design (Options/, Services/, Filters/, Middleware/, Configuration/, Diagnostics/, Registration/, Integration/)
    - _Requirements: all_

  - [ ]* 15.2 Write integration tests for end-to-end OData query flow
    - Test complete pipeline: middleware → filter → IQueryable → materialization → PageResult
    - Test with EF Core InMemory provider
    - Test $expand with Include/eager loading
    - Test nested $expand with inner $filter/$orderby/$top/$select
    - Test all supported $filter operators (eq, ne, gt, ge, lt, le, and, or, not, contains, startswith, endswith, in)
    - Test all supported property types (DateTime, DateTimeOffset, integer, decimal, string, boolean, GUID)
    - Test query application order
    - Test IQueryable<T> return type detection
    - _Requirements: 3.1–3.9, 10.1–10.7_

  - [ ]* 15.3 Write integration tests for tenant isolation with multi-tenant data
    - Test tenant filter applied as outermost Where clause
    - Test client cannot bypass tenant isolation via $filter
    - Test 403 response when tenant context missing
    - Test no tenant filtering when EnableTenantIsolation is false
    - _Requirements: 9.1–9.6_

  - [ ]* 15.4 Write integration tests for attribute precedence over middleware
    - Test attribute MaxTop overrides global
    - Test attribute MaxExpandDepth overrides global
    - Test attribute AllowedQueryOptions overrides global
    - Test middleware defers to attribute when both target same endpoint
    - _Requirements: 6.4–6.8, 7.7_

- [ ] 16. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties (FsCheck with xUnit, 100+ iterations)
- Unit tests validate specific examples and edge cases (xUnit + NSubstitute)
- Integration tests use EF Core InMemory provider for end-to-end validation
- The module does not require an AWS-specific implementation project — it is purely ASP.NET Core middleware/library

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4", "1.5"] },
    { "id": 2, "tasks": ["2.1", "2.3", "3.1"] },
    { "id": 3, "tasks": ["2.2", "2.4", "3.2", "4.1"] },
    { "id": 4, "tasks": ["3.3", "4.2", "4.3"] },
    { "id": 5, "tasks": ["4.4", "4.5", "4.6", "6.1"] },
    { "id": 6, "tasks": ["6.2", "7.1"] },
    { "id": 7, "tasks": ["7.2", "7.3", "7.4", "8.1"] },
    { "id": 8, "tasks": ["8.2", "9.1"] },
    { "id": 9, "tasks": ["8.3", "8.4", "8.5", "9.2"] },
    { "id": 10, "tasks": ["11.1", "11.2", "11.3"] },
    { "id": 11, "tasks": ["11.4", "11.5", "11.6", "12.1", "12.2"] },
    { "id": 12, "tasks": ["12.3", "13.1"] },
    { "id": 13, "tasks": ["13.2", "14.1", "15.1"] },
    { "id": 14, "tasks": ["14.2", "15.2", "15.3", "15.4"] }
  ]
}
```
