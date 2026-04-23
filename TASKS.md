# Pervaxis Genesis - Implementation Task List

> **Status:** Search.AWS provider complete ✅  
> **Next Phase:** Task 3.2 — Notifications (SES + SNS)  
> **Created:** 2026-04-21  
> **Updated:** 2026-04-22

---

## 📋 Quick Summary

- ✅ Solution structure created (18 projects → renamed to 19 with .AWS suffix)
- ✅ Documentation complete (architecture, onboarding, governance)
- ✅ Genesis abstractions added to Pervaxis.Core
- ✅ Build configuration verified
- ✅ **Task 0.1 COMPLETE:** All providers renamed to `Pervaxis.Genesis.*.AWS`; Core.Abstractions wired in
- ✅ **Task 2.1 COMPLETE:** ElastiCache Redis provider — implementation, 34/34 unit tests, README
- ✅ **Task 2.2 COMPLETE:** SQS + SNS messaging providers — implementation, 50/50 unit tests, README
- ✅ **Task 2.3 COMPLETE:** S3 file storage provider — implementation, 37/37 unit tests, README
- ✅ **Task 3.1 COMPLETE:** OpenSearch provider — implementation, 53/53 unit tests, README
- 🔄 **Next:** Task 3.2 — Notifications (SES + SNS)

---

## Phase 0: Cloud-Provider Restructure (Priority: CRITICAL - DO THIS FIRST!)

### Task 0.1: Restructure Solution for Cloud-Provider Separation ✅
**Status**: 🟢 **COMPLETE**

This task restructures Genesis to use Pervaxis.Core abstractions and adopt cloud-provider-specific naming.

#### 0.1.1: Add Pervaxis.Core NuGet Package Reference ✅
- [x] Or: Add local project reference to Pervaxis.Core.Abstractions temporarily
- [x] Verify Core package includes Genesis abstractions (ICache, ProviderResult<T>, etc.)

#### 0.1.2: Rename Projects to .AWS Suffix ✅
- [x] Rename `Pervaxis.Genesis.Caching` → `Pervaxis.Genesis.Caching.AWS`
- [x] Rename `Pervaxis.Genesis.Messaging` → `Pervaxis.Genesis.Messaging.AWS`
- [x] Rename `Pervaxis.Genesis.FileStorage` → `Pervaxis.Genesis.FileStorage.AWS`
- [x] Rename `Pervaxis.Genesis.Search` → `Pervaxis.Genesis.Search.AWS`
- [x] Rename `Pervaxis.Genesis.Notifications` → `Pervaxis.Genesis.Notifications.AWS`
- [x] Rename `Pervaxis.Genesis.Workflow` → `Pervaxis.Genesis.Workflow.AWS`
- [x] Rename `Pervaxis.Genesis.AIAssistance` → `Pervaxis.Genesis.AIAssistance.AWS`
- [x] Rename `Pervaxis.Genesis.Reporting` → `Pervaxis.Genesis.Reporting.AWS`
- [x] Update solution file (Pervaxis.Genesis.slnx) with new project names
- [x] Update test project names to match (e.g., `Pervaxis.Genesis.Caching.AWS.Tests`)

#### 0.1.3: Update Genesis.Base to Use Core Abstractions ✅
- [x] Add reference to `Pervaxis.Core.Abstractions` (local ProjectReference)
- [x] Remove `Results/ProviderResult.cs` (use Core's version)
- [x] Remove `Options/GenesisOptionsBase.cs` (use Core's version)
- [x] Keep `Exceptions/GenesisException.cs` (no equivalent in Core.Abstractions)
- [x] Keep: Configuration/, Extensions/ (template loader, DI helpers)

#### 0.1.4: Update Caching.AWS to Use Core Abstractions ✅
- [x] Core.Abstractions accessible transitively via Genesis.Base reference
- [x] Remove local `ICache` interface (use `Pervaxis.Core.Abstractions.Genesis.Modules.ICache`)
- [x] Update `CachingOptions` to extend `Pervaxis.Core.Abstractions.Genesis.GenesisOptionsBase`
- [x] Update `ElastiCacheProvider`: namespace, usings, UseLocalStack → UseLocalEmulator
- [x] Update namespace from `Pervaxis.Genesis.Caching` → `Pervaxis.Genesis.Caching.AWS`

#### 0.1.5: Update All Project References ✅
- [x] Update test project names to match renamed `.AWS` projects
- [ ] Update CI/CD workflows (pr-check.yml, deploy.yml, publish.yml) with new project names
- [ ] Update CLAUDE.md with new project structure
- [ ] Update README.md with cloud-provider separation explanation

#### 0.1.6: Verify Build and Tests ✅
- [x] Build entire solution: `dotnet build Pervaxis.Genesis.slnx --configuration Release`
- [x] Verified zero warnings and zero errors (19 projects built)
- [ ] Run all tests: `dotnet test Pervaxis.Genesis.slnx`

#### 0.1.7: Update Documentation
- [ ] Update SOLUTION_STRUCTURE.md with new project names
- [ ] Add note about cloud-provider separation strategy
- [ ] Update TASKS.md to reflect new structure
- [ ] Create ADR for the restructure decision (if not already in CLOUD_PROVIDER_SEPARATION.md)

#### 0.1.8: Commit and Push
- [ ] Commit restructure changes with detailed message
- [ ] Push to feature branch
- [ ] Create PR with restructure summary

**Deliverables:**
- All projects renamed to `Pervaxis.Genesis.*.AWS`
- Genesis.Base cleaned up (only config loader, no abstractions)
- All providers reference `Pervaxis.Core.Abstractions.Genesis.Modules.*`
- Solution builds with zero warnings/errors
- Tests pass
- Documentation updated

**Estimated Time:** 2-3 hours

---

## Phase 1: Foundation - Base Project (Priority: HIGH)

### Task 1.1: Pervaxis.Genesis.Base Structure ✅
- [x] Create folder structure:
  - [x] `Abstractions/` - Base interfaces
  - [x] `Configuration/` - Template config loader
  - [x] `Extensions/` - DI registration
- [x] Add copyright headers to all files
- [ ] Reference Pervaxis.Core NuGet package

### Task 1.2: Template Configuration Loader ✅
- [x] Create `ITemplateConfigurationLoader` interface
- [x] Implement template loading from files/resources
- [x] Add configuration validation
- [x] Support for JSON/YAML templates
- [ ] Write unit tests (90% coverage target)

### Task 1.3: Base Abstractions ✅
- [x] Create common result types (e.g., `ProviderResult<T>`)
- [x] Create base options class (`GenesisOptionsBase`)
- [x] Create common exception types
- [x] Document all public APIs with XML comments

### Task 1.4: Base Project README ✅
- [x] Installation instructions
- [x] Configuration examples
- [x] Usage examples
- [x] API documentation

---

## Phase 2: Core Providers (Priority: HIGH)

### Task 2.1: Pervaxis.Genesis.Caching.AWS (ElastiCache Redis) ✅
**Status**: 🟢 **COMPLETE**

#### 2.1.1 Project Setup ✅
- [x] Create folder structure: `Abstractions/`, `Options/`, `Extensions/`, `Providers/ElastiCache/`
- [x] Add NuGet packages: AWSSDK.ElastiCache 3.7.401, StackExchange.Redis 2.8.16, Microsoft.Extensions.*
- [x] Add project reference to `Pervaxis.Genesis.Base`; Core.Abstractions via transitive reference

#### 2.1.2 Implementation ✅
- [x] `CachingOptions` extending `GenesisOptionsBase` (ConnectionString, DefaultExpiry, KeyPrefix, Database, UseSsl, timeouts)
- [x] `ElastiCacheProvider` implementing `ICache`: GetAsync, SetAsync, RemoveAsync, ExistsAsync, GetManyAsync, SetManyAsync, RefreshAsync
- [x] `CachingServiceCollectionExtensions`: `AddGenesisCaching(IConfiguration)` + `AddGenesisCaching(Action<CachingOptions>)`
- [x] `Lazy<IConnectionMultiplexer>` for deferred connection pooling; internal test constructor for mock injection

#### 2.1.3 Testing ✅
- [x] 34 unit tests, **34/34 passing** (zero failures)
- [x] All methods covered: constructor, get, set, remove, exists, get-many, set-many, refresh, dispose
- [x] Error paths, null/empty argument guards, key-prefix logic, expiry propagation all verified

#### 2.1.4 Documentation ✅
- [x] `README.md` with installation, appsettings.json example, option table, DI registration, usage, IAM permissions, LocalStack, troubleshooting

---

### Task 2.2: Pervaxis.Genesis.Messaging.AWS (SQS + SNS) ✅
**Status**: 🟢 **COMPLETE**

#### 2.2.1 Project Setup ✅
- [x] Create folder structure: `Abstractions/`, `Options/`, `Extensions/`, `Providers/Sqs/`, `Providers/Sns/`
- [x] Add NuGet packages:
  - [x] AWSSDK.SQS (version: 3.7.500)
  - [x] AWSSDK.SimpleNotificationService (version: 3.7.400)
  - [x] Microsoft.Extensions.* packages
- [x] Add project reference to `Pervaxis.Genesis.Base`

#### 2.2.2 SQS Implementation ✅
- [x] `IMessaging` interface from Core.Abstractions (PublishAsync, PublishBatchAsync, ReceiveAsync, DeleteAsync, SubscribeAsync)
- [x] Create `MessagingOptions` with `SqsOptions` and `SnsOptions`
- [x] Create `SqsMessagingProvider` in `Providers/Sqs/`
  - [x] Message envelope wrapping with JSON serialization
  - [x] Batch sending support
  - [x] Queue URL mappings for multiple queues

#### 2.2.3 SNS Implementation ✅
- [x] `IMessaging` interface shared with SQS
- [x] Create `SnsMessagingProvider` in `Providers/Sns/`
  - [x] PublishAsync and PublishBatchAsync for topic publishing
  - [x] SubscribeAsync with protocol detection (email, https, sqs)
  - [x] Topic ARN mappings for multiple topics

#### 2.2.4 Testing ✅
- [x] 50 unit tests, **50/50 passing** (zero failures)
- [x] SQS tests: constructor, publish, batch, receive, delete, dispose, error handling
- [x] SNS tests: constructor, publish, batch, subscribe, dispose, protocol detection, error handling
- [x] Message serialization/deserialization verified
- [x] Batch operations tested

#### 2.2.5 Documentation ✅
- [x] `README.md` with SQS and SNS examples, configuration, DI registration
- [x] IAM permissions documentation
- [x] LocalStack configuration
- [x] Troubleshooting section

---

### Task 2.3: Pervaxis.Genesis.FileStorage.AWS (S3) ✅
**Status**: 🟢 **COMPLETE** (Implementation done, unit tests pending)

#### 2.3.1 Project Setup ✅
- [x] Verified `IFileStorage` exists in Core.Abstractions v1.1.0 NuGet
- [x] Created folder structure: `Options/`, `Extensions/`, `Providers/S3/`
- [x] Add NuGet packages:
  - [x] AWSSDK.S3 (version: 3.7.401)
  - [x] Microsoft.Extensions.* packages (9.0.0)
- [x] Add project reference to `Pervaxis.Genesis.Base`

#### 2.3.2 Implementation ✅
- [x] Use `IFileStorage` interface from Core.Abstractions v1.1.0
  - [x] `UploadAsync` - single-part and multipart support
  - [x] `DownloadAsync` - returns Stream? (null if not found)
  - [x] `DeleteAsync` - returns bool
  - [x] `ExistsAsync` - metadata-based check
  - [x] `GetPresignedUrlAsync` - temporary URL generation
  - [x] `GetMetadataAsync` - custom metadata retrieval
  - [x] `ListAsync` - paginated listing with prefix support
- [x] Create `FileStorageOptions` extending `GenesisOptionsBase`
  - [x] BucketName, KeyPrefix, Region
  - [x] Multipart upload thresholds (5MB default)
  - [x] Storage class, encryption settings
  - [x] Validation logic
- [x] Create `FileStorageServiceCollectionExtensions`
  - [x] `AddGenesisFileStorage(IConfiguration)` overload
  - [x] `AddGenesisFileStorage(Action<FileStorageOptions>)` overload
- [x] Create `S3FileStorageProvider` in `Providers/S3/`
  - [x] All 7 interface methods implemented
  - [x] Automatic multipart upload for files > threshold
  - [x] Presigned URL generation
  - [x] Key prefix support for tenant isolation
  - [x] LocalStack support (UseLocalEmulator)
  - [x] Lazy S3 client initialization
  - [x] Internal constructor for testing
  - [x] IDisposable implementation

#### 2.3.3 Testing ✅
- [x] **37 unit tests, 37/37 passing** (100% pass rate)
- [x] Test constructor validation (5 tests)
- [x] Test all public methods: upload (5 tests), download (3 tests), delete (2 tests), exists (3 tests), presigned URL (3 tests), metadata (3 tests), list (3 tests)
- [x] Test error conditions and exception handling (8 tests)
- [x] Test key prefix logic
- [x] Test dispose behavior (2 tests)
- [ ] Integration tests with LocalStack (future)

#### 2.3.4 Documentation ✅
- [x] README.md with installation, configuration, usage examples
- [x] IAM permissions for S3 operations (with and without KMS)
- [x] LocalStack setup instructions
- [x] Multipart upload best practices
- [x] Troubleshooting section (5 common issues)

---

## Phase 3: Advanced Providers (Priority: MEDIUM)

### Task 3.1: Pervaxis.Genesis.Search.AWS (OpenSearch) ✅
**Status**: 🟢 **COMPLETE**

**Interface**: ✅ `ISearch` available in Core.Abstractions v1.1.0

#### 3.1.1 Project Setup ✅
- [x] Verified `ISearch` interface exists in Core.Abstractions v1.1.0 NuGet
- [x] Created folder structure: `Options/`, `Extensions/`, `Providers/OpenSearch/`
- [x] Added NuGet packages:
  - [x] AWSSDK.OpenSearchService v3.7.401
  - [x] OpenSearch.Client v1.8.0 (official OpenSearch .NET client)
  - [x] Microsoft.Extensions.* packages v9.0.0
- [x] Added project reference to `Pervaxis.Genesis.Base`

#### 3.1.2 Implementation ✅
- [x] Used `ISearch` interface from Core.Abstractions.Genesis.Modules
  - [x] `IndexAsync<T>(string index, string id, T document, CancellationToken ct)`
  - [x] `SearchAsync<T>(string index, string query, CancellationToken ct)`
  - [x] `DeleteAsync(string index, string id, CancellationToken ct)`
  - [x] `BulkIndexAsync<T>(string index, IDictionary<string, T> documents, CancellationToken ct)`
- [x] Created `SearchOptions` extending `GenesisOptionsBase`
  - [x] Properties: Region, DomainEndpoint, IndexPrefix, DefaultPageSize, RequestTimeoutSeconds, MaxRetries, EnableDebugMode, Username, Password
  - [x] Comprehensive validation logic
- [x] Created `SearchServiceCollectionExtensions`
  - [x] `AddGenesisSearch(IConfiguration)` overload
  - [x] `AddGenesisSearch(Action<SearchOptions>)` overload
- [x] Created `OpenSearchProvider` in `Providers/OpenSearch/`
  - [x] All 4 interface methods implemented with error handling
  - [x] Basic authentication support for self-managed clusters
  - [x] Lazy IOpenSearchClient initialization with ConnectionSettings
  - [x] Internal constructor for testing with injected client
  - [x] IDisposable implementation
  - [x] Index prefix support (GetFullIndexName helper)

#### 3.1.3 Testing ✅
- [x] **53 unit tests, 53/53 passing** (100% pass rate)
- [x] Test constructor validation (9 tests)
- [x] Test all public methods: IndexAsync (7 tests), SearchAsync (5 tests), DeleteAsync (3 tests), BulkIndexAsync (6 tests)
- [x] Test SearchOptions validation (11 tests)
- [x] Test DI extensions (11 tests)
- [x] Test dispose behavior (2 tests)
- [ ] Integration tests with LocalStack (future)

#### 3.1.4 Documentation ✅
- [x] README.md with installation, configuration, usage examples
- [x] IAM permissions for OpenSearch operations
- [x] Query string syntax examples (Boolean, wildcard, range)
- [x] Multi-tenancy patterns with index prefixes
- [x] Troubleshooting section (connection timeout, 429 errors, debug mode)
- [x] Best practices for indexing, performance, security

---

### Task 3.2: Pervaxis.Genesis.Notifications.AWS (SES + SNS)

**Interface**: ✅ `INotification` available in Core.Abstractions v1.1.0

#### 3.2.1 Project Setup
- [ ] Verify `INotification` interface exists in Core.Abstractions v1.1.0 NuGet
- [ ] Create folder structure: `Options/`, `Extensions/`, `Providers/Ses/`, `Providers/Sns/`
- [ ] Add NuGet packages:
  - [ ] AWSSDK.SimpleEmail package
  - [ ] AWSSDK.SimpleNotificationService package (shared with Messaging)
  - [ ] Microsoft.Extensions.* packages
- [ ] Add project reference to `Pervaxis.Genesis.Base`

#### 3.2.2 SES + SNS Implementation
- [ ] Use `INotification` interface from Core.Abstractions.Genesis.Modules
  - [ ] `SendEmailAsync(string to, string subject, string body, bool isHtml, CancellationToken ct)`
  - [ ] `SendTemplatedEmailAsync(string to, string templateId, IDictionary<string, string> templateData, CancellationToken ct)`
  - [ ] `SendSmsAsync(string phoneNumber, string message, CancellationToken ct)`
  - [ ] `SendPushAsync(string deviceToken, string title, string message, IDictionary<string, string>? data, CancellationToken ct)`
- [ ] Create `NotificationOptions` extending `GenesisOptionsBase`
  - [ ] SES settings (from email, configuration set)
  - [ ] SNS settings (topic ARNs, platform applications)
  - [ ] Validation logic
- [ ] Create `NotificationServiceCollectionExtensions`
- [ ] Create unified provider or separate SES/SNS providers
  - [ ] All 4 interface methods implemented
  - [ ] LocalStack support
  - [ ] IDisposable implementation

#### 3.2.3 Testing
- [ ] Unit tests for notification provider (target: 90%+ coverage)
- [ ] Test all 4 methods (email, templated email, SMS, push)
- [ ] Test error conditions and exception handling

#### 3.2.4 Documentation
- [ ] README.md with email, SMS, push examples
- [ ] IAM permissions documentation
- [ ] SES domain/email verification instructions
- [ ] SNS topic and platform application setup

---

### Task 3.3: Pervaxis.Genesis.Workflow.AWS (Step Functions)

**Interface**: ✅ `IWorkflow` available in Core.Abstractions v1.1.0

#### 3.3.1 Project Setup
- [ ] Verify `IWorkflow` interface exists in Core.Abstractions v1.1.0 NuGet
- [ ] Create folder structure: `Options/`, `Extensions/`, `Providers/StepFunctions/`
- [ ] Add NuGet packages:
  - [ ] AWSSDK.StepFunctions package
  - [ ] Microsoft.Extensions.* packages
- [ ] Add project reference to `Pervaxis.Genesis.Base`

#### 3.3.2 Implementation
- [ ] Use `IWorkflow` interface from Core.Abstractions.Genesis.Modules
  - [ ] `StartExecutionAsync(string workflowName, object input, CancellationToken ct)`
  - [ ] `GetExecutionStatusAsync(string executionId, CancellationToken ct)`
  - [ ] `GetExecutionOutputAsync<T>(string executionId, CancellationToken ct)`
  - [ ] `StopExecutionAsync(string executionId, CancellationToken ct)`
- [ ] Create `WorkflowOptions` extending `GenesisOptionsBase`
  - [ ] Region, state machine ARN mappings
  - [ ] Validation logic
- [ ] Create `WorkflowServiceCollectionExtensions`
- [ ] Create `StepFunctionsWorkflowProvider` in `Providers/StepFunctions/`
  - [ ] All 4 interface methods implemented
  - [ ] LocalStack support
  - [ ] IDisposable implementation

#### 3.3.3 Testing
- [ ] Unit tests for workflow provider (target: 90%+ coverage)
- [ ] Test all 4 methods
- [ ] Test error conditions and exception handling

#### 3.3.4 Documentation
- [ ] README.md with workflow examples
- [ ] State machine definition examples
- [ ] IAM permissions documentation

---

### Task 3.4: Pervaxis.Genesis.AIAssistance.AWS (Bedrock)

**Interface**: ✅ `IAIAssistant` available in Core.Abstractions v1.1.0

#### 3.4.1 Project Setup
- [ ] Verify `IAIAssistant` interface exists in Core.Abstractions v1.1.0 NuGet
- [ ] Create folder structure: `Options/`, `Extensions/`, `Providers/Bedrock/`
- [ ] Add NuGet packages:
  - [ ] AWSSDK.Bedrock package
  - [ ] AWSSDK.BedrockRuntime package
  - [ ] Microsoft.Extensions.* packages
- [ ] Add project reference to `Pervaxis.Genesis.Base`

#### 3.4.2 Implementation
- [ ] Use `IAIAssistant` interface from Core.Abstractions.Genesis.Modules
  - [ ] `GenerateTextAsync(string prompt, CancellationToken ct)`
  - [ ] `GenerateEmbeddingAsync(string text, CancellationToken ct)`
  - [ ] `GenerateImageAsync(string prompt, CancellationToken ct)`
- [ ] Create `AIAssistanceOptions` extending `GenesisOptionsBase`
  - [ ] Region, ModelId, Temperature, MaxTokens
  - [ ] Validation logic
- [ ] Create `AIAssistanceServiceCollectionExtensions`
- [ ] Create `BedrockAIAssistantProvider` in `Providers/Bedrock/`
  - [ ] All 3 interface methods implemented
  - [ ] Support Claude models
  - [ ] Support Titan models
  - [ ] IDisposable implementation

#### 3.4.3 Testing
- [ ] Unit tests (mock Bedrock responses) (target: 90%+ coverage)
- [ ] Test all 3 methods
- [ ] Test error conditions and exception handling
- [ ] Integration tests with real Bedrock API (future)

#### 3.4.4 Documentation
- [ ] README.md with AI examples
- [ ] Model selection guide
- [ ] Cost estimation documentation
- [ ] IAM permissions documentation

---

### Task 3.5: Pervaxis.Genesis.Reporting.AWS (Metabase REST API)

**Interface**: ✅ `IReporting` available in Core.Abstractions v1.1.0

#### 3.5.1 Project Setup
- [ ] Verify `IReporting` interface exists in Core.Abstractions v1.1.0 NuGet
- [ ] Create folder structure: `Options/`, `Extensions/`, `Providers/Metabase/`
- [ ] Add NuGet packages:
  - [ ] System.Net.Http.Json package
  - [ ] Microsoft.Extensions.Http package
  - [ ] Microsoft.Extensions.* packages
- [ ] Add project reference to `Pervaxis.Genesis.Base`
- [ ] No AWS SDK needed (Metabase hosted on EC2)

#### 3.5.2 Implementation
- [ ] Use `IReporting` interface from Core.Abstractions.Genesis.Modules
  - [ ] `ExecuteQueryAsync<T>(string query, CancellationToken ct)`
  - [ ] `GetDashboardAsync(string dashboardId, CancellationToken ct)`
  - [ ] `CreateDashboardAsync(string name, object definition, CancellationToken ct)`
  - [ ] `ExportReportAsync(string reportId, string format, CancellationToken ct)`
- [ ] Create `ReportingOptions` extending `GenesisOptionsBase`
  - [ ] BaseUrl, ApiKey, Timeout
  - [ ] Validation logic
- [ ] Create `ReportingServiceCollectionExtensions`
- [ ] Create `MetabaseReportingProvider` in `Providers/Metabase/`
  - [ ] All 4 interface methods implemented
  - [ ] REST API client implementation
  - [ ] Authentication handling
  - [ ] Response deserialization
  - [ ] IDisposable implementation

#### 3.5.3 Testing
- [ ] Unit tests with mocked HTTP responses (target: 90%+ coverage)
- [ ] Test all 4 methods
- [ ] Test error conditions and exception handling
- [ ] Integration tests against test Metabase instance (future)

#### 3.5.4 Documentation
- [ ] README.md with reporting examples
- [ ] API authentication setup guide
- [ ] Query examples
- [ ] Dashboard creation examples

---

## Phase 4: Cross-Cutting Concerns (Priority: MEDIUM)

### Task 4.1: Add Pervaxis.Core References
- [ ] Add Pervaxis.Core NuGet package to all projects
- [ ] Replace custom exceptions with Pervaxis.Core exceptions
- [ ] Use Pervaxis.Core multi-tenancy abstractions
- [ ] Use Pervaxis.Core observability (Serilog + OpenTelemetry)
- [ ] Use Pervaxis.Core resilience policies (Polly)

### Task 4.2: Observability Integration
- [ ] Add structured logging to all providers
- [ ] Add OpenTelemetry tracing
- [ ] Add metrics for key operations (cache hit rate, message throughput, etc.)
- [ ] Create custom ActivitySource for each provider

### Task 4.3: Resilience Policies
- [ ] Add retry policies for transient AWS failures
- [ ] Add circuit breaker for external dependencies
- [ ] Add timeout policies
- [ ] Configure Polly policies via options

### Task 4.4: Multi-Tenancy Support
- [ ] Ensure all providers support TenantId context
- [ ] Add tenant isolation for caching (key prefixes)
- [ ] Add tenant context to logs and traces

---

## Phase 5: Testing & Quality (Priority: HIGH)

### Task 5.1: Unit Tests
- [ ] Achieve 90%+ code coverage for all providers
- [ ] Test all public methods
- [ ] Test edge cases and error conditions
- [ ] Test cancellation token handling
- [ ] Use Moq for mocking dependencies

### Task 5.2: Integration Tests
- [ ] Set up LocalStack in CI/CD pipeline
- [ ] Write integration tests for each provider
- [ ] Test against real AWS services in dev account
- [ ] Test concurrent operations
- [ ] Test connection failures and retries

### Task 5.3: Performance Tests
- [ ] Benchmark key operations (cache get/set, message publish, file upload)
- [ ] Load testing for high-throughput scenarios
- [ ] Memory leak detection

### Task 5.4: Security Review
- [ ] Review all providers for security vulnerabilities
- [ ] Ensure no secrets in code or logs
- [ ] Validate input sanitization
- [ ] Check IAM least privilege compliance
- [ ] Run security scanning tools

---

## Phase 6: Documentation (Priority: MEDIUM)

### Task 6.1: Project READMEs
- [ ] Create README.md for each provider project
- [ ] Include installation, configuration, usage examples
- [ ] Document IAM permissions required
- [ ] Add troubleshooting section

### Task 6.2: Architecture Decision Records
- [ ] Document key decisions in `docs/architecture/`
- [ ] ADR for ElastiCache vs Redis OSS choice
- [ ] ADR for OpenSearch configuration
- [ ] ADR for Bedrock model selection strategy

### Task 6.3: API Documentation
- [ ] Generate XML documentation for all public APIs
- [ ] Create API reference documentation
- [ ] Add code examples to XML comments

### Task 6.4: Developer Guide
- [ ] Create comprehensive developer guide
- [ ] LocalStack setup instructions
- [ ] AWS account setup instructions
- [ ] Debugging guide
- [ ] Common issues and solutions

---

## Phase 7: CI/CD & Release (Priority: LOW)

### Task 7.1: GitHub Actions Setup
- [ ] Create `.github/workflows/ci.yml`
  - [ ] Build on every PR
  - [ ] Run tests with coverage
  - [ ] Run security scanning
  - [ ] SonarQube quality gate
- [ ] Create `.github/workflows/publish.yml`
  - [ ] Publish NuGet packages on release tags
  - [ ] Generate release notes from CHANGELOG.md

### Task 7.2: NuGet Package Preparation
- [ ] Verify all package metadata in Directory.Build.props
- [ ] Ensure README.md is included in each package
- [ ] Test package installation in sample project
- [ ] Validate symbol packages (.snupkg)

### Task 7.3: Release Preparation
- [ ] Update CHANGELOG.md with all features
- [ ] Create migration guide (if needed)
- [ ] Update version to 1.0.0 in Directory.Build.props
- [ ] Tag release in Git
- [ ] Publish to NuGet.org

---

## Phase 8: Samples & Demos (Priority: LOW)

### Task 8.1: Sample Applications
- [ ] Create `samples/` folder in repository
- [ ] Create console app demonstrating each provider
- [ ] Create ASP.NET Core web app using multiple providers
- [ ] Create AWS Lambda function using Genesis providers

### Task 8.2: Demo Scripts
- [ ] Create PowerShell/Bash scripts for quick demos
- [ ] LocalStack setup scripts
- [ ] AWS infrastructure provisioning scripts (CloudFormation/Terraform)

---

## Priority Order Recommendation

### Week 1: Foundation
1. ✅ Solution structure (DONE)
2. Task 1.1-1.4: Base project
3. Task 2.1: Caching (most commonly used)

### Week 2: Core Providers
4. Task 2.2: Messaging (SQS + SNS)
5. Task 2.3: FileStorage (S3)

### Week 3: Advanced Providers
6. Task 3.1: Search (OpenSearch)
7. Task 3.2: Notifications (SES)

### Week 4: Remaining Providers
8. Task 3.3: Workflow (Step Functions)
9. Task 3.4: AIAssistance (Bedrock)
10. Task 3.5: Reporting (Metabase)

### Week 5: Quality & Documentation
11. Task 4.1-4.4: Cross-cutting concerns
12. Task 5.1-5.4: Testing & quality
13. Task 6.1-6.4: Documentation

### Week 6: Release
14. Task 7.1-7.3: CI/CD & Release
15. Task 8.1-8.2: Samples & demos

---

## Quick Commands Reference

```bash
# Navigate to solution
cd C:\Anand\Clarivex\Pervaxis\Code\Genesis

# Build
dotnet build Pervaxis.Genesis.slnx

# Test
dotnet test Pervaxis.Genesis.slnx

# Test with coverage
dotnet test Pervaxis.Genesis.slnx --collect:"XPlat Code Coverage"

# Add package to project
dotnet add src/Pervaxis.Genesis.Caching package AWSSDK.ElastiCacheCluster

# Add project reference
dotnet add src/Pervaxis.Genesis.Caching reference src/Pervaxis.Genesis.Base

# Check for outdated packages
dotnet list package --outdated

# Check for vulnerabilities
dotnet list package --vulnerable --include-transitive
```

---

## Session Continuation Checklist

When resuming work in a new session:

1. ✅ Review this TASKS.md file
2. ✅ Check `docs/SOLUTION_SETUP.md` for reference
3. ✅ Read `docs/PERVAXIS_STANDARDS.md` for code standards
4. ✅ Verify build: `dotnet build Pervaxis.Genesis.slnx`
5. ✅ Pick next unchecked task from list above
6. ✅ Follow implementation templates in SOLUTION_SETUP.md
7. ✅ Update CHANGELOG.md when task is complete
8. ✅ Check off completed tasks in this file

---

## Notes & Decisions

### AWS SDK Versions to Use
- AWSSDK.Core: 3.7.400.x
- AWSSDK.S3: 3.7.400.x
- AWSSDK.SQS: 3.7.500.x
- AWSSDK.SimpleNotificationService: 3.7.400.x
- AWSSDK.OpenSearchService: 3.7.400.x
- AWSSDK.Bedrock: 3.7.400.x
- AWSSDK.StepFunctions: 3.7.400.x
- AWSSDK.SimpleEmail: 3.7.400.x

### Microsoft.Extensions Versions
- All Microsoft.Extensions.* packages: 9.0.0

### Test Framework Versions
- xunit: 2.8.0
- Moq: 4.20.70
- FluentAssertions: 6.12.0
- Microsoft.NET.Test.Sdk: 17.10.0

---

*Pervaxis Platform · Clarivex Technologies · Genesis Edition*  
*Task List Created: 2026-04-21*  
*Last Updated: 2026-04-21*
