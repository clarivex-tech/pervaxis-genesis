# Pervaxis Genesis - Implementation Task List

> **Status:** Initial structure complete ✅  
> **Next Phase:** Provider implementation  
> **Created:** 2026-04-21

---

## 📋 Quick Summary

- ✅ Solution structure created (18 projects)
- ✅ Documentation complete (7 guides)
- ✅ Build configuration verified
- 🔄 **Next:** Implement providers one by one

---

## Phase 1: Foundation - Base Project (Priority: HIGH)

### Task 1.1: Pervaxis.Genesis.Base Structure
- [ ] Create folder structure:
  - [ ] `Abstractions/` - Base interfaces
  - [ ] `Configuration/` - Template config loader
  - [ ] `Extensions/` - DI registration
- [ ] Add copyright headers to all files
- [ ] Reference Pervaxis.Core NuGet package

### Task 1.2: Template Configuration Loader
- [ ] Create `ITemplateConfigurationLoader` interface
- [ ] Implement template loading from files/resources
- [ ] Add configuration validation
- [ ] Support for JSON/YAML templates
- [ ] Write unit tests (90% coverage target)

### Task 1.3: Base Abstractions
- [ ] Create common result types (e.g., `ProviderResult<T>`)
- [ ] Create base options class (`GenesisOptionsBase`)
- [ ] Create common exception types
- [ ] Document all public APIs with XML comments

### Task 1.4: Base Project README
- [ ] Installation instructions
- [ ] Configuration examples
- [ ] Usage examples
- [ ] API documentation

---

## Phase 2: Core Providers (Priority: HIGH)

### Task 2.1: Pervaxis.Genesis.Caching (ElastiCache Redis)

#### 2.1.1 Project Setup
- [ ] Create folder structure: `Abstractions/`, `Options/`, `Extensions/`, `Providers/ElastiCache/`
- [ ] Add NuGet packages:
  - [ ] AWSSDK.ElastiCacheCluster (version: 3.7.400+)
  - [ ] StackExchange.Redis (version: 2.8.0+)
  - [ ] Microsoft.Extensions.* (DI, Logging, Options)
- [ ] Add project reference to `Pervaxis.Genesis.Base`
- [ ] Document dependencies with justification comments

#### 2.1.2 Implementation
- [ ] Create `ICache` interface in `Abstractions/`
  - [ ] `GetAsync<T>(string key, CancellationToken ct)`
  - [ ] `SetAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct)`
  - [ ] `RemoveAsync(string key, CancellationToken ct)`
  - [ ] `ExistsAsync(string key, CancellationToken ct)`
- [ ] Create `CachingOptions` in `Options/`
  - [ ] Region, ConnectionString, DefaultExpiry, KeyPrefix
- [ ] Create `CachingServiceCollectionExtensions` in `Extensions/`
  - [ ] `AddPervaxisCaching()` method
- [ ] Create `ElastiCacheProvider` in `Providers/ElastiCache/`
  - [ ] Implement `ICache` interface
  - [ ] Use StackExchange.Redis for Redis operations
  - [ ] Add proper logging
  - [ ] Handle connection failures gracefully

#### 2.1.3 Testing
- [ ] Unit tests for `ElastiCacheProvider`
- [ ] Integration tests with LocalStack
- [ ] Test edge cases (null values, expired keys, connection failures)
- [ ] Verify 90%+ code coverage

#### 2.1.4 Documentation
- [ ] Create README.md with examples
- [ ] Document IAM permissions required
- [ ] Add configuration examples (code + appsettings.json)

---

### Task 2.2: Pervaxis.Genesis.Messaging (SQS + SNS)

#### 2.2.1 Project Setup
- [ ] Create folder structure: `Abstractions/`, `Options/`, `Extensions/`, `Providers/Sqs/`, `Providers/Sns/`
- [ ] Add NuGet packages:
  - [ ] AWSSDK.SQS (version: 3.7.500+)
  - [ ] AWSSDK.SimpleNotificationService (version: 3.7.400+)
  - [ ] Microsoft.Extensions.* packages
- [ ] Add project reference to `Pervaxis.Genesis.Base`

#### 2.2.2 SQS Implementation
- [ ] Create `IMessagePublisher` interface
  - [ ] `PublishAsync<T>(T message, CancellationToken ct)`
  - [ ] `PublishBatchAsync<T>(IEnumerable<T> messages, CancellationToken ct)`
- [ ] Create `MessagingOptions` with `SqsOptions`
- [ ] Create `SqsMessagePublisher` in `Providers/Sqs/`
  - [ ] Message envelope wrapping
  - [ ] Batch sending support
  - [ ] Dead-letter queue handling

#### 2.2.3 SNS Implementation
- [ ] Create `INotificationPublisher` interface
  - [ ] `PublishAsync(string topic, string message, CancellationToken ct)`
  - [ ] `SubscribeAsync(string topic, string endpoint, CancellationToken ct)`
- [ ] Create `SnsNotificationPublisher` in `Providers/Sns/`

#### 2.2.4 Testing
- [ ] Unit tests for SQS publisher
- [ ] Unit tests for SNS publisher
- [ ] Integration tests with LocalStack
- [ ] Test message envelope serialization
- [ ] Test batch operations

#### 2.2.5 Documentation
- [ ] README.md with SQS and SNS examples
- [ ] IAM permissions documentation
- [ ] Message envelope format documentation

---

### Task 2.3: Pervaxis.Genesis.FileStorage (S3)

#### 2.3.1 Project Setup
- [ ] Create folder structure: `Abstractions/`, `Options/`, `Extensions/`, `Providers/S3/`
- [ ] Add NuGet packages:
  - [ ] AWSSDK.S3 (version: 3.7.400+)
  - [ ] Microsoft.Extensions.* packages
- [ ] Add project reference to `Pervaxis.Genesis.Base`

#### 2.3.2 Implementation
- [ ] Create `IFileStorage` interface
  - [ ] `UploadAsync(string key, Stream content, CancellationToken ct)`
  - [ ] `DownloadAsync(string key, CancellationToken ct)`
  - [ ] `DeleteAsync(string key, CancellationToken ct)`
  - [ ] `ExistsAsync(string key, CancellationToken ct)`
  - [ ] `GetPresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct)`
- [ ] Create `FileStorageOptions`
  - [ ] Region, BucketName, DefaultExpiry
- [ ] Create `FileStorageServiceCollectionExtensions`
- [ ] Create `S3FileStorageProvider` in `Providers/S3/`
  - [ ] Implement all interface methods
  - [ ] Support for multipart uploads (large files)
  - [ ] Presigned URL generation

#### 2.3.3 Testing
- [ ] Unit tests for S3 provider
- [ ] Integration tests with LocalStack
- [ ] Test large file uploads
- [ ] Test presigned URLs

#### 2.3.4 Documentation
- [ ] README.md with upload/download examples
- [ ] IAM permissions for S3 operations
- [ ] Best practices for large files

---

## Phase 3: Advanced Providers (Priority: MEDIUM)

### Task 3.1: Pervaxis.Genesis.Search (OpenSearch)

#### 3.1.1 Project Setup
- [ ] Create folder structure
- [ ] Add AWSSDK.OpenSearchService package
- [ ] Add OpenSearch .NET client (if needed)

#### 3.1.2 Implementation
- [ ] Create `ISearchClient` interface
  - [ ] `IndexAsync<T>(string index, T document, CancellationToken ct)`
  - [ ] `SearchAsync<T>(string index, string query, CancellationToken ct)`
  - [ ] `DeleteAsync(string index, string id, CancellationToken ct)`
  - [ ] `BulkIndexAsync<T>(string index, IEnumerable<T> documents, CancellationToken ct)`
- [ ] Create `SearchOptions`
- [ ] Create `OpenSearchClient` provider

#### 3.1.3 Testing & Documentation
- [ ] Unit and integration tests
- [ ] README.md with search examples
- [ ] IAM permissions documentation

---

### Task 3.2: Pervaxis.Genesis.Notifications (SES + SNS)

#### 3.2.1 Project Setup
- [ ] Create folder structure
- [ ] Add AWSSDK.SimpleEmail package
- [ ] Add AWSSDK.SimpleNotificationService package (shared with Messaging)

#### 3.2.2 SES Implementation
- [ ] Create `IEmailService` interface
  - [ ] `SendEmailAsync(EmailMessage message, CancellationToken ct)`
  - [ ] `SendTemplatedEmailAsync(string template, object data, CancellationToken ct)`
- [ ] Create `NotificationOptions` with `SesOptions`
- [ ] Create `SesEmailService` provider

#### 3.2.3 SNS Integration
- [ ] Create `IPushNotificationService` interface
- [ ] Implement push notification sending via SNS

#### 3.2.4 Testing & Documentation
- [ ] Unit and integration tests
- [ ] README.md with email examples
- [ ] IAM permissions documentation

---

### Task 3.3: Pervaxis.Genesis.Workflow (Step Functions)

#### 3.3.1 Project Setup
- [ ] Create folder structure
- [ ] Add AWSSDK.StepFunctions package

#### 3.3.2 Implementation
- [ ] Create `IWorkflowExecutor` interface
  - [ ] `StartExecutionAsync(string stateMachine, object input, CancellationToken ct)`
  - [ ] `GetExecutionStatusAsync(string executionArn, CancellationToken ct)`
  - [ ] `StopExecutionAsync(string executionArn, CancellationToken ct)`
- [ ] Create `WorkflowOptions`
- [ ] Create `StepFunctionsWorkflowExecutor` provider

#### 3.3.3 Testing & Documentation
- [ ] Unit and integration tests
- [ ] README.md with workflow examples
- [ ] State machine definition examples
- [ ] IAM permissions documentation

---

### Task 3.4: Pervaxis.Genesis.AIAssistance (Bedrock)

#### 3.4.1 Project Setup
- [ ] Create folder structure
- [ ] Add AWSSDK.Bedrock package
- [ ] Add AWSSDK.BedrockRuntime package

#### 3.4.2 Implementation
- [ ] Create `IAIAssistant` interface
  - [ ] `GenerateTextAsync(string prompt, CancellationToken ct)`
  - [ ] `GenerateImageAsync(string prompt, CancellationToken ct)`
  - [ ] `EmbedAsync(string text, CancellationToken ct)`
- [ ] Create `AIAssistanceOptions`
  - [ ] Region, ModelId, Temperature, MaxTokens
- [ ] Create `BedrockAIAssistant` provider
  - [ ] Support Claude models
  - [ ] Support Titan models

#### 3.4.3 Testing & Documentation
- [ ] Unit tests (mock Bedrock responses)
- [ ] Integration tests with real Bedrock API
- [ ] README.md with AI examples
- [ ] Model selection guide
- [ ] Cost estimation documentation
- [ ] IAM permissions documentation

---

### Task 3.5: Pervaxis.Genesis.Reporting (Metabase REST API)

#### 3.5.1 Project Setup
- [ ] Create folder structure
- [ ] Add HTTP client dependencies
- [ ] No AWS SDK needed (Metabase hosted on EC2)

#### 3.5.2 Implementation
- [ ] Create `IReportingClient` interface
  - [ ] `GetReportAsync(int reportId, CancellationToken ct)`
  - [ ] `ExecuteQueryAsync(string query, CancellationToken ct)`
  - [ ] `CreateDashboardAsync(object dashboard, CancellationToken ct)`
  - [ ] `GetDashboardAsync(int dashboardId, CancellationToken ct)`
- [ ] Create `ReportingOptions`
  - [ ] BaseUrl, ApiKey, Timeout
- [ ] Create `MetabaseReportingClient` provider
  - [ ] REST API client implementation
  - [ ] Authentication handling
  - [ ] Response deserialization

#### 3.5.3 Testing & Documentation
- [ ] Unit tests with mocked HTTP responses
- [ ] Integration tests against test Metabase instance
- [ ] README.md with reporting examples
- [ ] API authentication setup guide

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
