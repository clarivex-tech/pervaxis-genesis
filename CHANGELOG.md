# Changelog

All notable changes to Pervaxis Genesis will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.1] - 2026-04-27

### 🔒 Security Fixes

**Critical security improvements:**

- **[SECURITY]** Fixed cache key injection vulnerability in ElastiCache provider (CWE-74)
  - Sanitizes user-provided keys to prevent tenant isolation bypass
  - Replaces colons and control characters in cache keys
  - Prevents cross-tenant data access via crafted keys

- **[SECURITY]** Fixed path traversal vulnerability in S3 FileStorage provider (CWE-22)
  - Validates S3 keys to reject `../` and `..\` sequences
  - Prevents directory traversal attacks
  - Normalizes paths while preserving S3 prefix semantics

### 🧪 Testing

- Added 7 new security-focused unit tests
- All 397/397 tests passing
- Security validation for tenant isolation

### 📚 Documentation

- Created comprehensive `GENESIS_PROVIDERS.md` guide (1,800+ lines)
- Complete usage patterns for all 8 providers
- Configuration, DI, testing examples for each provider
- Best practices and security guidelines

### 🔧 Infrastructure

- Fixed GitHub Actions permissions for release creation
- Updated CI/CD workflows for production readiness

---

## [1.0.0] - 2026-04-26

### 🎉 Initial Release

Genesis v1.0.0 is the inaugural release of the Pervaxis Genesis AWS Provider Library - a production-ready collection of .NET 10 libraries providing standardized abstractions over AWS services.

### 📦 Providers Included (8 Total)

**All providers include multi-tenancy, observability, and resilience support.**

- **Pervaxis.Genesis.Caching.AWS** - ElastiCache Redis (40 tests)
- **Pervaxis.Genesis.Messaging.AWS** - SQS + SNS (50 tests)
- **Pervaxis.Genesis.FileStorage.AWS** - S3 (37 tests)
- **Pervaxis.Genesis.Search.AWS** - OpenSearch (53 tests)
- **Pervaxis.Genesis.Notifications.AWS** - SES + SNS (45 tests)
- **Pervaxis.Genesis.Workflow.AWS** - Step Functions (42 tests)
- **Pervaxis.Genesis.AIAssistance.AWS** - Bedrock (60 tests)
- **Pervaxis.Genesis.Reporting.AWS** - Metabase (63 tests)

### ✨ Features

#### **Multi-Tenancy Support**
- All 8 providers support optional `ITenantContext` injection
- Automatic tenant isolation (cache key prefixing, message attributes, S3 tags, etc.)
- Configurable via `EnableTenantIsolation` (default: true)

#### **Observability Integration**
- Distributed tracing on all 39 public interface methods
- OpenTelemetry integration via Pervaxis.Core.Observability
- Provider-specific span names and tags
- Zero overhead when tracing disabled

#### **Resilience Policies**
- Polly v8-based retry, circuit breaker, timeout
- **Retry**: 3 attempts, exponential backoff + jitter
- **Circuit Breaker**: 50% failure threshold, 60s break
- **Timeout**: 30s per operation
- AWS transient error detection
- Enabled by default

### 🔒 Security

- Zero vulnerabilities
- OpenTelemetry.Api upgraded 1.9.0 → 1.15.3
- No security suppressions
- IAM least privilege examples

### 📊 Quality Metrics

- **Projects**: 19 total
- **Tests**: 390/390 passing (100%)
- **Build**: 0 warnings, 0 errors
- **Coverage**: 90%+ target

### 🚀 Getting Started

```bash
dotnet add package Pervaxis.Genesis.Caching.AWS --version 1.0.0
```

```csharp
// Program.cs
builder.Services.AddGenesisCaching(
    builder.Configuration.GetSection("Genesis:Caching"));

// Usage
public class MyService(ICache cache)
{
    public async Task<User?> GetUserAsync(string userId) =>
        await cache.GetAsync<User>($"user:{userId}");
}
```

### 🎯 Use Cases

- Enterprise multi-tenant SaaS
- Cloud-native microservices
- Pervaxis.Forge code generation
- Event-driven architectures
- AI-powered applications

### 📄 License

Copyright © 2026 Clarivex Technologies Private Limited.

---

[1.0.1]: https://github.com/clarivex-tech/pervaxis-genesis/releases/tag/v1.0.1
[1.0.0]: https://github.com/clarivex-tech/pervaxis-genesis/releases/tag/v1.0.0
