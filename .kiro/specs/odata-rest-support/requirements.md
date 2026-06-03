# Requirements Document

## Introduction

The Genesis OData REST Support module provides OData query capabilities for REST API endpoints within the Pervaxis platform. It enables clients to use standardized OData query options ($filter, $orderby, $select, $expand, $top, $skip, $count) against API endpoints, translating these into efficient queries against Entity Framework Core and IQueryable providers. The module uses Microsoft.AspNetCore.OData as the underlying implementation, wrapped in Genesis conventions for DI registration, options validation, observability, resilience, security controls, and multi-tenancy. It is opt-in per endpoint via attributes or conventions, and selectable in Forge when generating REST service prints. Unlike other Genesis modules, OData support is purely an ASP.NET Core middleware/library concern and does not require an AWS-specific implementation project.

## Glossary

- **OData_Module**: The Genesis library (`Pervaxis.Genesis.OData`) that defines the contracts, options, attributes, middleware, and query processing logic for OData query support on REST endpoints.
- **OData_Query_Options**: The set of OData system query options supported by the module: `$filter`, `$orderby`, `$select`, `$expand`, `$top`, `$skip`, and `$count`.
- **ODataOptions**: The options class extending `GenesisOptionsBase` that configures the OData module (max page size, allowed expand depth, security limits, tenant isolation settings).
- **ODataQueryable_Attribute**: An ASP.NET Core action filter attribute (`[ODataQueryable]`) that enables OData query processing on individual controller actions.
- **OData_Middleware**: An ASP.NET Core middleware component that intercepts HTTP requests and applies OData query parsing and validation for configured endpoints.
- **Query_Validator**: The component responsible for validating incoming OData query options against configured security and structural limits before execution.
- **Entity_Configuration**: A per-entity configuration that defines which properties are filterable, sortable, selectable, and expandable for a given entity type.
- **OData_Query_Context**: A context object created per request containing the parsed OData query options, validation results, and tenant scope information.
- **ITenantContext**: The tenant resolution abstraction from `Pervaxis.Core.Abstractions.MultiTenancy` providing current tenant identity.
- **PervaxisMeter**: The static metrics factory from `Pervaxis.Core.Observability.Metrics` used to create counters and histograms.
- **PervaxisActivitySource**: The static tracing source from `Pervaxis.Core.Observability.Tracing` used to create distributed trace activities.
- **Forge**: The code generation engine that auto-wires Genesis module registration into every generated service.
- **Page_Result**: A standardized response envelope containing the query results, total count (when requested), next page link, and pagination metadata.
- **IQueryable_Provider**: An Entity Framework Core `DbContext` or any `IQueryable<T>` data source that the OData module applies query transformations against.
- **Query_Complexity_Score**: A computed numeric value representing the estimated cost of an OData query based on the combination of filter depth, expand levels, result size, and sort operations.

## Requirements

### Requirement 1: Module Registration

**User Story:** As a platform engineer, I want to register the OData module using a standard Genesis extension method, so that it integrates consistently with other Genesis modules.

#### Acceptance Criteria

1. THE OData_Module SHALL provide an `AddGenesisOData` extension method on `IServiceCollection` that accepts an `IConfiguration` parameter and returns `IServiceCollection` for method chaining.
2. THE OData_Module SHALL provide an `AddGenesisOData` extension method on `IServiceCollection` that accepts an `Action<ODataOptions>` parameter and returns `IServiceCollection` for method chaining.
3. WHEN `AddGenesisOData` is called, THE OData_Module SHALL register the OData query processing services, the Query_Validator, and the OData_Middleware in the dependency injection container.
4. WHEN `AddGenesisOData` is called with an `IConfiguration` parameter, THE OData_Module SHALL bind options from the `Genesis:OData` configuration section.
5. WHEN `AddGenesisOData` is called with an `Action<ODataOptions>` parameter, THE OData_Module SHALL apply the action delegate to configure the options instance.
6. IF `AddGenesisOData` is called with a null `IServiceCollection`, null `IConfiguration`, or null `Action<ODataOptions>` parameter, THEN THE OData_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.
7. IF `AddGenesisOData` is called multiple times on the same `IServiceCollection`, THEN THE OData_Module SHALL register services using the TryAdd pattern, ensuring idempotent registration without duplicate service descriptors.
8. WHEN `AddGenesisOData` is called, THE OData_Module SHALL configure `Microsoft.AspNetCore.OData` services with the route prefix, EDM model, and query option settings derived from `ODataOptions`.

### Requirement 2: Options Configuration

**User Story:** As a platform engineer, I want to configure the OData module through a validated options class, so that misconfiguration is caught early at startup.

#### Acceptance Criteria

1. THE ODataOptions SHALL extend `GenesisOptionsBase`.
2. THE ODataOptions SHALL include a `MaxTop` property of type integer for specifying the maximum allowed value for `$top`, with a default value of 100 and a valid range of 1 to 10000.
3. THE ODataOptions SHALL include a `DefaultPageSize` property of type integer for specifying the default number of results returned when `$top` is not specified, with a default value of 20 and a valid range of 1 to 10000.
4. THE ODataOptions SHALL include a `MaxExpandDepth` property of type integer for specifying the maximum nesting depth allowed in `$expand` clauses, with a default value of 2 and a valid range of 0 to 5.
5. THE ODataOptions SHALL include a `MaxFilterDepth` property of type integer for specifying the maximum nesting depth of logical operators in `$filter` expressions, with a default value of 3 and a valid range of 1 to 10.
6. THE ODataOptions SHALL include a `MaxOrderByProperties` property of type integer for specifying the maximum number of properties in an `$orderby` clause, with a default value of 3 and a valid range of 1 to 10.
7. THE ODataOptions SHALL include an `EnableTenantIsolation` property of type boolean with a default value of true.
8. THE ODataOptions SHALL include an `EnableCount` property of type boolean with a default value of true, controlling whether the `$count` query option is permitted.
9. THE ODataOptions SHALL include a `MaxQueryComplexityScore` property of type integer with a default value of 50 and a valid range of 1 to 200, representing the maximum computed Query_Complexity_Score allowed for any single request.
10. THE ODataOptions SHALL include an `AllowedQueryOptions` property of type `ODataQueryOptions` flags enum with a default value that enables all supported query options ($filter, $orderby, $select, $expand, $top, $skip, $count).
11. THE ODataOptions `Validate()` method SHALL return false when `base.Validate()` returns false.
12. THE ODataOptions `Validate()` method SHALL return false when `MaxTop` is less than 1 or greater than 10000.
13. THE ODataOptions `Validate()` method SHALL return false when `DefaultPageSize` is less than 1 or greater than `MaxTop`.
14. THE ODataOptions `Validate()` method SHALL return false when `MaxExpandDepth` is less than 0 or greater than 5.
15. THE ODataOptions `Validate()` method SHALL return false when `MaxFilterDepth` is less than 1 or greater than 10.
16. THE ODataOptions `Validate()` method SHALL return false when `MaxOrderByProperties` is less than 1 or greater than 10.
17. THE ODataOptions `Validate()` method SHALL return false when `MaxQueryComplexityScore` is less than 1 or greater than 200.

### Requirement 3: OData Query Option Support

**User Story:** As a domain developer, I want endpoints to support standard OData query options, so that clients can filter, sort, page, and shape response data using a well-known protocol.

#### Acceptance Criteria

1. WHEN a request to an OData-enabled endpoint includes a `$filter` query parameter, THE OData_Module SHALL parse the filter expression and apply it as a LINQ `Where` clause against the endpoint's IQueryable_Provider.
2. WHEN a request to an OData-enabled endpoint includes a `$orderby` query parameter, THE OData_Module SHALL parse the ordering expression and apply it as LINQ `OrderBy`/`ThenBy` clauses against the endpoint's IQueryable_Provider.
3. WHEN a request to an OData-enabled endpoint includes a `$select` query parameter, THE OData_Module SHALL parse the property list and project only the specified properties in the query result.
4. WHEN a request to an OData-enabled endpoint includes an `$expand` query parameter, THE OData_Module SHALL parse the navigation property references and apply eager loading (Include) against the endpoint's IQueryable_Provider.
5. WHEN a request to an OData-enabled endpoint includes a `$top` query parameter, THE OData_Module SHALL limit the result set to the specified number of items, applying the limit after filtering and ordering.
6. WHEN a request to an OData-enabled endpoint includes a `$skip` query parameter, THE OData_Module SHALL skip the specified number of items from the result set, applying the offset after filtering and ordering but before `$top`.
7. WHEN a request to an OData-enabled endpoint includes `$count=true`, THE OData_Module SHALL include the total count of matching items (before `$top` and `$skip` are applied) in the response envelope.
8. WHEN a request to an OData-enabled endpoint omits the `$top` query parameter, THE OData_Module SHALL apply the configured `DefaultPageSize` as the result limit.
9. IF a query option is used that is not included in the configured `AllowedQueryOptions` flags, THEN THE OData_Module SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_QUERY_OPTION_DISABLED"` and a message identifying the disallowed option.

### Requirement 4: Query Validation and Security

**User Story:** As a platform engineer, I want OData queries validated against configurable limits, so that expensive or malicious queries cannot degrade system performance.

#### Acceptance Criteria

1. IF the `$top` value exceeds the configured `MaxTop`, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_TOP_EXCEEDED"` and a message stating the maximum allowed value.
2. IF the `$expand` clause depth exceeds the configured `MaxExpandDepth`, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_EXPAND_DEPTH_EXCEEDED"` and a message stating the maximum allowed depth.
3. IF the `$filter` expression nesting depth exceeds the configured `MaxFilterDepth`, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_FILTER_DEPTH_EXCEEDED"` and a message stating the maximum allowed depth.
4. IF the `$orderby` clause references more properties than the configured `MaxOrderByProperties`, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_ORDERBY_EXCEEDED"` and a message stating the maximum allowed properties.
5. IF the computed Query_Complexity_Score for a request exceeds the configured `MaxQueryComplexityScore`, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_QUERY_TOO_COMPLEX"` and a message stating the query exceeds the allowed complexity.
6. THE Query_Validator SHALL compute the Query_Complexity_Score by summing: the number of `$filter` conditions multiplied by 2, the `$expand` depth multiplied by 10, the number of `$orderby` properties multiplied by 3, 5 points if `$count=true`, and 1 point per `$select` property beyond the first 5.
7. IF a `$filter` expression references a property that is not marked as filterable in the Entity_Configuration, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_PROPERTY_NOT_FILTERABLE"` and a message identifying the property.
8. IF an `$orderby` clause references a property that is not marked as sortable in the Entity_Configuration, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_PROPERTY_NOT_SORTABLE"` and a message identifying the property.
9. IF an `$expand` clause references a navigation property that is not marked as expandable in the Entity_Configuration, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_PROPERTY_NOT_EXPANDABLE"` and a message identifying the property.
10. IF a `$filter` or `$orderby` expression contains a malformed or unparseable syntax, THEN THE Query_Validator SHALL return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_QUERY_PARSE_ERROR"` and a message describing the parse failure location.

### Requirement 5: Entity Configuration

**User Story:** As a domain developer, I want to configure which properties are queryable per entity type, so that I can control what clients can filter, sort, expand, and select.

#### Acceptance Criteria

1. THE OData_Module SHALL provide an `IEntityODataConfiguration<TEntity>` interface with methods `ConfigureFilter`, `ConfigureSort`, `ConfigureSelect`, and `ConfigureExpand` for defining per-entity query permissions.
2. THE OData_Module SHALL provide a fluent builder API on `IEntityODataConfiguration<TEntity>` that accepts lambda expressions referencing entity properties (e.g., `config.ConfigureFilter(e => e.Name)`) to mark properties as filterable, sortable, selectable, or expandable.
3. WHEN no Entity_Configuration is registered for a given entity type, THE OData_Module SHALL apply a default configuration that marks all primitive properties as filterable, sortable, and selectable, and marks no navigation properties as expandable.
4. THE OData_Module SHALL provide a `ConfigureEntityOData<TEntity>` method on the `ODataOptions` builder that accepts an `Action<IEntityODataConfiguration<TEntity>>` for registering per-entity configurations during service registration.
5. THE OData_Module SHALL support an `ExcludeProperty` method on `IEntityODataConfiguration<TEntity>` that removes a property from all query operations (filter, sort, select, expand), allowing sensitive properties to be hidden from OData queries.
6. WHEN a property is excluded via `ExcludeProperty`, THE OData_Module SHALL omit that property from `$select` results even if explicitly requested, and SHALL reject `$filter` and `$orderby` references to that property with the appropriate error code.

### Requirement 6: Opt-In Per Endpoint — Attribute

**User Story:** As a domain developer, I want to enable OData query support on specific controller actions using an attribute, so that I can selectively expose query capabilities on endpoints that return collections.

#### Acceptance Criteria

1. THE OData_Module SHALL provide an `[ODataQueryable]` attribute decorated with `[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]` that can be applied to individual ASP.NET Core controller actions.
2. WHEN a controller action is decorated with `[ODataQueryable]`, THE OData_Module SHALL apply OData query processing (parsing, validation, and IQueryable transformation) to requests targeting that action.
3. WHEN a controller action is not decorated with `[ODataQueryable]` and no middleware-level configuration targets it, THE OData_Module SHALL not intercept or modify the request or response for that action.
4. THE `[ODataQueryable]` attribute SHALL accept an optional `MaxTop` parameter of type integer with a default value of 0, where 0 means "use the global `ODataOptions.MaxTop` value" and any non-zero value overrides the global setting for that specific endpoint.
5. THE `[ODataQueryable]` attribute SHALL accept an optional `MaxExpandDepth` parameter of type integer with a default value of -1, where -1 means "use the global `ODataOptions.MaxExpandDepth` value" and any non-negative value overrides the global setting for that specific endpoint.
6. THE `[ODataQueryable]` attribute SHALL accept an optional `AllowedQueryOptions` parameter of type `ODataQueryOptions` flags enum with a default value of `ODataQueryOptions.All`, where the value overrides the global setting for that specific endpoint, enabling per-endpoint restriction of query capabilities.
7. IF the `[ODataQueryable]` attribute's `MaxTop` parameter is set to a value outside the range 1 to 10000, THEN THE OData_Module SHALL return HTTP 500 (Internal Server Error) on the first request to that endpoint and log an error indicating an invalid per-endpoint configuration.
8. WHEN both the `[ODataQueryable]` attribute and the OData_Middleware route pattern target the same endpoint, THE OData_Module SHALL apply OData query processing exactly once, with attribute-level parameter overrides taking precedence over middleware-level and global configuration.

### Requirement 7: Opt-In Per Endpoint — Middleware Configuration

**User Story:** As a platform engineer, I want to configure OData query support for groups of endpoints via middleware, so that I can apply it broadly without decorating individual actions.

#### Acceptance Criteria

1. THE OData_Module SHALL provide a `UseGenesisOData` extension method on `IApplicationBuilder` that accepts an `Action<ODataMiddlewareOptions>` parameter for configuring route patterns, and registers the OData_Middleware in the request pipeline.
2. THE OData_Middleware SHALL accept a route pattern configuration as a list of ASP.NET Core route template patterns (supporting literal segments and `{parameter}` placeholders, e.g., `"/api/products"`, `"/api/orders/{id}/items"`) that determines which endpoints are OData-enabled.
3. WHEN a request's path matches a configured route pattern using case-insensitive comparison, THE OData_Middleware SHALL apply OData query processing to that request.
4. WHEN a request does not match any configured route pattern and the action is not decorated with `[ODataQueryable]`, THE OData_Middleware SHALL pass the request through without OData processing.
5. THE OData_Middleware SHALL support HTTP method filtering, applying OData query processing only to GET requests by default. Additional HTTP methods can be configured via `ODataMiddlewareOptions.AllowedMethods`.
6. IF `UseGenesisOData` is called with a null `IApplicationBuilder` or null options action, THEN THE OData_Module SHALL throw an `ArgumentNullException` identifying the null parameter by name.
7. WHEN a request matches both a configured middleware route pattern and the action is decorated with `[ODataQueryable]`, THE OData_Middleware SHALL defer to the `[ODataQueryable]` attribute configuration, allowing attribute-level overrides to take precedence over middleware defaults.

### Requirement 8: Pagination and Response Envelope

**User Story:** As a domain developer, I want OData query results returned in a standardized pagination envelope, so that clients can implement consistent pagination across all endpoints.

#### Acceptance Criteria

1. THE OData_Module SHALL return query results in a Page_Result envelope containing: `value` (the array of result items), `@odata.count` (the total count when `$count=true`), and `@odata.nextLink` (the URL for the next page when more results are available).
2. WHEN the number of matching items exceeds the effective page size (`$top` or `DefaultPageSize`), THE OData_Module SHALL include an `@odata.nextLink` property containing a fully-qualified URL with the appropriate `$skip` value to retrieve the next page of results.
3. WHEN `$count=true` is included in the request, THE OData_Module SHALL execute a count query against the filtered (but unpaged) IQueryable_Provider and include the result as the `@odata.count` value in the response.
4. WHEN `$count=true` is not included in the request or `EnableCount` is false, THE OData_Module SHALL omit the `@odata.count` property from the response envelope.
5. THE OData_Module SHALL include `@odata.context` metadata in the response containing the entity set URL and any applied `$select` or `$expand` information.
6. WHEN the effective `$skip` value plus the effective `$top` value is greater than or equal to the total count of matching items, THE OData_Module SHALL omit the `@odata.nextLink` property from the response, indicating no further pages are available.
7. THE Page_Result response SHALL use HTTP 200 (OK) status code for successful queries and set the `Content-Type` header to `application/json;odata.metadata=minimal`.

### Requirement 9: Tenant Isolation

**User Story:** As a platform engineer, I want OData queries automatically scoped to the current tenant, so that one tenant cannot access another tenant's data through query manipulation.

#### Acceptance Criteria

1. WHILE `ODataOptions.EnableTenantIsolation` is true AND `ITenantContext` is resolved with a non-null, non-empty tenant ID, THE OData_Module SHALL automatically append a tenant filter condition to the IQueryable_Provider before applying any client-supplied OData query options, restricting results to entities belonging to the current tenant.
2. WHILE `ODataOptions.EnableTenantIsolation` is true, THE OData_Module SHALL apply the tenant filter as the outermost `Where` clause, ensuring that no client-supplied `$filter` expression can override or remove the tenant scope.
3. WHILE `ODataOptions.EnableTenantIsolation` is true AND `ITenantContext` is not resolved (null, not registered, or returns a null/empty tenant ID), THE OData_Module SHALL return HTTP 403 (Forbidden) with a problem details response body containing an error code of `"ODATA_TENANT_REQUIRED"` and a message indicating that tenant context is required for this operation.
4. WHILE `ODataOptions.EnableTenantIsolation` is false, THE OData_Module SHALL not apply any automatic tenant filtering, allowing queries to span all data regardless of tenant context.
5. THE OData_Module SHALL detect the tenant property on entities by convention (a property named `TenantId` of type string) or by explicit configuration via a `ConfigureTenantProperty<TEntity>(Expression<Func<TEntity, string>>)` method on the entity configuration builder.
6. IF an entity type does not have a detectable or configured tenant property AND `EnableTenantIsolation` is true, THEN THE OData_Module SHALL log a warning at startup indicating the entity lacks tenant isolation and SHALL not apply tenant filtering for that entity type.

### Requirement 10: IQueryable Integration

**User Story:** As a domain developer, I want the OData module to work seamlessly with Entity Framework Core and other IQueryable providers, so that OData queries are translated to efficient database queries.

#### Acceptance Criteria

1. THE OData_Module SHALL accept `IQueryable<T>` return values from controller actions and apply OData query transformations (filter, orderby, select, expand, top, skip) as LINQ expression tree modifications before materialization.
2. THE OData_Module SHALL support controller actions that return `IQueryable<T>` directly, `ActionResult<IQueryable<T>>`, or `Task<IQueryable<T>>`, detecting the IQueryable_Provider from the action result.
3. WHEN an OData query transformation produces a LINQ expression that is not translatable by the IQueryable_Provider (e.g., unsupported function in Entity Framework Core), THE OData_Module SHALL catch the `InvalidOperationException` and return HTTP 400 (Bad Request) with a problem details response body containing an error code of `"ODATA_QUERY_NOT_TRANSLATABLE"` and a message indicating which part of the query could not be translated.
4. THE OData_Module SHALL apply query options in the correct order: tenant filter first, then `$filter`, then `$orderby`, then `$skip`, then `$top`, then `$select`/`$expand`.
5. THE OData_Module SHALL support the following `$filter` operators: `eq`, `ne`, `gt`, `ge`, `lt`, `le`, `and`, `or`, `not`, `contains`, `startswith`, `endswith`, `in`.
6. THE OData_Module SHALL support `$filter` on DateTime, DateTimeOffset, integer, decimal, string, boolean, and GUID property types.
7. THE OData_Module SHALL support nested `$expand` with inner `$filter`, `$orderby`, `$top`, and `$select` (e.g., `$expand=Orders($filter=Status eq 'Active';$top=5)`), subject to the configured `MaxExpandDepth` limit.

### Requirement 11: Observability — Metrics

**User Story:** As an SRE, I want metrics on OData query processing, so that I can monitor query patterns, performance, and rejection rates.

#### Acceptance Criteria

1. WHEN an OData-enabled request completes processing (outcome determined), THE OData_Module SHALL increment the `genesis.odata.requests` counter metric by 1, tagged with `outcome` (values: `success`, `validation_error`, `parse_error`, `query_too_complex`, `not_translatable`), `http_method`, and `endpoint`.
2. WHEN OData query processing completes (from parsing through IQueryable materialization), THE OData_Module SHALL record the elapsed wall-clock time in the `genesis.odata.query.duration` histogram metric in milliseconds, tagged with `endpoint` and `outcome` (values: `success`, `error`).
3. THE OData_Module SHALL record the effective result count per request in the `genesis.odata.query.result_count` histogram metric, tagged with `endpoint`.
4. THE OData_Module SHALL record the computed Query_Complexity_Score per request in the `genesis.odata.query.complexity` histogram metric, tagged with `endpoint`.
5. WHILE `EnableTenantIsolation` is true AND `ITenantContext` is resolved, THE OData_Module SHALL include a `tenant_id` tag on all emitted metrics; IF `EnableTenantIsolation` is false OR `ITenantContext` is not resolved, THEN THE OData_Module SHALL omit the `tenant_id` tag.
6. THE OData_Module SHALL create all metrics as `static readonly` fields using `PervaxisMeter.CreateCounter<long>` and `PervaxisMeter.CreateHistogram<double>` with the unit parameter set to `"1"` for counters and `"ms"` for duration histograms.
7. IF metric emission fails for any reason, THEN THE OData_Module SHALL suppress the failure silently without affecting the request outcome or throwing an exception to the caller.

### Requirement 12: Observability — Tracing

**User Story:** As an SRE, I want distributed trace spans for OData query processing, so that I can understand query execution flow and diagnose performance issues.

#### Acceptance Criteria

1. WHEN an OData-enabled request is processed, THE OData_Module SHALL create a trace activity using `PervaxisActivitySource` with span name `odata.query` and ActivityKind `Internal`.
2. WHEN the `odata.query` trace activity is created, THE OData_Module SHALL set the following tags on the activity: `odata.query_options` (a comma-separated list of query options used, e.g., `"$filter,$orderby,$top"`), `odata.outcome` (set upon completion, using the same values as the metrics `outcome` tag), `odata.complexity_score` (the computed Query_Complexity_Score), `http.method` (the HTTP method of the request), and `http.route` (the matched route template).
3. WHEN `ITenantContext` is resolved, THE OData_Module SHALL set `tenant.id` (from `ITenantContext.TenantId.Value`) and `tenant.name` (from `ITenantContext.TenantName`) tags on the `odata.query` trace activity.
4. WHEN query validation completes, THE OData_Module SHALL create a child trace activity with span name `odata.validate` and ActivityKind `Internal`, including the tags `odata.complexity_score` and `odata.validation_result` (values: `passed`, `rejected`).
5. IF query processing fails, THEN THE `odata.query` trace activity SHALL set its status to `ActivityStatusCode.Error` with the error message as the status description.
6. IF `PervaxisActivitySource` has no registered listeners, THEN THE OData_Module SHALL skip all tracing operations without affecting request processing (null-safe activity pattern using `activity?.SetTag` and `activity?.SetStatus`).

### Requirement 13: Observability — Logging

**User Story:** As an SRE, I want structured logging for OData query processing events, so that I can troubleshoot query issues and monitor usage patterns.

#### Acceptance Criteria

1. WHEN an OData query is successfully processed and results are returned, THE OData_Module SHALL emit a structured log at Debug level containing the endpoint, HTTP method, query options used, result count, query duration in milliseconds, complexity score, and tenant ID (when `ITenantContext` is resolved).
2. WHEN a query is rejected by the Query_Validator, THE OData_Module SHALL emit a structured log at Warning level containing the endpoint, HTTP method, the validation error code, the failing query option and value, complexity score, and tenant ID (when `ITenantContext` is resolved).
3. WHEN a query fails due to a parse error in the OData expression, THE OData_Module SHALL emit a structured log at Warning level containing the endpoint, the raw query string, the parse error description, and tenant ID (when `ITenantContext` is resolved).
4. WHEN a query cannot be translated to the underlying IQueryable_Provider, THE OData_Module SHALL emit a structured log at Error level containing the endpoint, the query options, the translation error message, and tenant ID (when `ITenantContext` is resolved).
5. WHEN tenant isolation blocks a request due to missing tenant context, THE OData_Module SHALL emit a structured log at Warning level containing the endpoint and HTTP method.
6. THE OData_Module SHALL emit all structured logs using `ILogger<T>` with compile-time source-generated `LoggerMessage` methods, ensuring log field names are consistent across all criteria and queryable as discrete structured properties.

### Requirement 14: Forge Integration

**User Story:** As a platform engineer, I want the OData module to be selectable in Forge when generating REST service prints, so that generated services can opt into OData query support.

#### Acceptance Criteria

1. THE OData_Module SHALL be registered as an optional module in the Forge module catalog with the identifier `"OData"` and category `"REST Infrastructure"`.
2. WHEN a user selects the OData module in the Forge UI, THE generated service print SHALL include the `Pervaxis.Genesis.OData` NuGet package reference in the service project file.
3. WHEN a user selects the OData module in the Forge UI, THE generated service print SHALL include `AddGenesisOData` registration in `Program.cs` with configuration binding to the `Genesis:OData` section, and `UseGenesisOData` middleware registration in the application pipeline.
4. WHEN a user selects the OData module in the Forge UI, THE generated service print SHALL include a default `Genesis:OData` configuration section in `appsettings.json` containing: `MaxTop` set to `100`, `DefaultPageSize` set to `20`, `MaxExpandDepth` set to `2`, `MaxFilterDepth` set to `3`, `MaxOrderByProperties` set to `3`, `EnableTenantIsolation` set to `true`, `EnableCount` set to `true`, and `MaxQueryComplexityScore` set to `50`.
5. WHEN a user does not select the OData module in the Forge UI, THE generated service print SHALL not contain any OData-related code, packages, or configuration.
6. WHEN a user selects the OData module in the Forge UI, THE generated service print SHALL include a sample controller action decorated with `[ODataQueryable]` demonstrating the return of an `IQueryable<T>` with entity configuration.

### Requirement 15: Local Development Support

**User Story:** As a developer, I want to use the OData module locally without additional infrastructure dependencies, so that I can develop and test OData query behavior with standard local tooling.

#### Acceptance Criteria

1. WHEN the application starts with `UseLocalEmulator` set to true in ODataOptions, THE OData_Module SHALL configure relaxed query validation limits: `MaxTop` set to 1000, `MaxExpandDepth` set to 5, `MaxFilterDepth` set to 10, `MaxQueryComplexityScore` set to 200.
2. WHEN the application starts with `UseLocalEmulator` set to true, THE OData_Module SHALL log an Information-level message indicating relaxed query limits are active for local development.
3. THE OData_Module SHALL function with any Entity Framework Core provider including the in-memory provider (`Microsoft.EntityFrameworkCore.InMemory`) and SQLite, enabling local development without external database dependencies.
4. WHEN the application starts with `UseLocalEmulator` set to true, THE OData_Module SHALL enable detailed OData error responses that include the raw query expression, parsed AST, and translation details in the problem details `extensions` property.
5. WHILE `UseLocalEmulator` is false, THE OData_Module SHALL return sanitized error messages in problem details responses without exposing internal query parsing details or stack traces.
