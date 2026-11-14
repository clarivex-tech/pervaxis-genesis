# Changelog

All notable changes to Pervaxis Genesis will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Messaging (`Pervaxis.Genesis.Messaging.AWS`): shared-resource tenant enforcement

- SQS/SNS providers are Scoped and use keyed, container-owned Singleton AWS clients.
- Single/batch publication requires a valid, stable tenant, including empty batches;
  the reserved tenant attribute is stamped from execution context. Disabling isolation
  is rejected in every environment.
- Tenant receives expose only matching messages; acknowledgement requires a receipt
  received for that tenant and queue by the same provider scope.
- Explicit `ISharedQueueConsumer` registration processes shared queues outside tenant
  requests, with separate tenant and async DI scopes per message. Handler failures,
  context mismatches and cancellation leave messages unacknowledged.
- Invalid envelopes/payloads are quarantined before acknowledgement. Missing/failing
  DLQs preserve the source message.
- **Breaking:** shared-topic subscriptions must be provisioned by the platform;
  tenant `SubscribeAsync` is disabled. Provider lifetimes, receive/acknowledgement
  ownership and worker registration requirements have changed. See the
  [Messaging guide](src/Pervaxis.Genesis.Messaging.AWS/README.md).

### Search (`Pervaxis.Genesis.Search.AWS`): shared-index tenant isolation

- All tenants share each logical index. Index and bulk writes stamp a reserved
  `tenantId`; document IDs are namespaced by tenant for writes and deletes.
- Searches always include an exact tenant filter on `tenantId.keyword`.
- Missing, invalid or changed tenant contexts fail closed, including empty bulk
  calls. The legacy isolation flag cannot disable enforcement.
- Providers are Scoped; keyed OpenSearch clients and settings are Singleton.
- **Breaking:** index names, document IDs and DI lifetimes change. Existing data
  is not migrated automatically. Provision the exact keyword mapping and follow
  the [deployment notes](src/Pervaxis.Genesis.Search.AWS/README.md).
- Ingest pipeline management and reindexing remain outside this change.

### FileStorage (`Pervaxis.Genesis.FileStorage.AWS`): tenant isolation hardening

See [ADR-0001](docs/architecture/ADR-0001-filestorage-tenant-isolation.md) for the full model.

**Security**
- `IFileStorage` and `TenantKeyResolver` are now Scoped (were Singleton, which captured the first request's tenant).
- All keys are built by `TenantKeyResolver` as `{KeyPrefix}tenants/{tenantId}/{key}`; an unresolved tenant fails closed.
- Every upload (single-part and multipart) is stamped with `pervaxis-tenant-id`; `Download`, `Exists` and `GetMetadata` reject objects with a missing or foreign stamp.
- Callers can no longer set `pervaxis-tenant-id`, `TenantId` or `TenantName` metadata (`RESERVED_METADATA_KEY`).
- `List` stays inside the tenant prefix on every page and throws if S3 returns a foreign key.
- Presigned URLs: 60-minute default, configurable maximum capped at 24 hours; over-limit requests throw.
- The S3 client is a keyed Singleton shared across scopes and is no longer disposed per request.

**Breaking changes**
- Objects written by v3.4.x (`{KeyPrefix}/tenant-{id}/...`, unstamped) are no longer visible. They are not migrated automatically; see ADR-0001 "Existing objects".
- `UploadAsync` returns the caller's key without `KeyPrefix`.
- Invalid keys throw `GenesisException` with an error code instead of `ArgumentException`.
- Singletons and background services must create a DI scope to use `IFileStorage`.
- `MaxPresignedUrlExpiryMinutes` above 1440 now fails options validation (was 10080).

**Not included:** Copy/Move and batch delete (deferred); isolation remains app-enforced, not IAM-enforced.

---

## [1.1.0] - 2026-06-08

### ✨ New Module: Input Sanitization (`Pervaxis.Genesis.Sanitization`)

Server-side input sanitization to prevent stored/reflected XSS at the service layer.

**Core Features:**
- `ISanitizer` interface with `StripAll`, `SanitizeHtml`, and profile-based `Sanitize` methods
- Three built-in profiles: PlainText (strip all HTML), SafeHtml (allow safe formatting), Markdown (allow rendered Markdown HTML)
- Custom profile support via configuration (`AllowCustomProfiles`)
- `[Sanitize]` attribute for declarative model binding sanitization (runs before validation)
- FluentValidation extensions: `.Sanitized()` (transform) and `.MustBeSanitized()` (reject)
- Global sanitization middleware (opt-in, off by default) for POST/PUT/PATCH JSON bodies

**Observability:**
- `genesis.sanitization.operations` counter (tagged: profile, source)
- `genesis.sanitization.threats_detected` counter (tagged: profile, source)
- `genesis.sanitization.duration_ms` histogram (tagged: profile)
- Source-generated structured logging (Warning on threat detection, Debug on clean input)

**Security:**
- Uses HtmlSanitizer (Ganss.Xss) — whitelist-based, battle-tested
- Handles OWASP XSS bypass vectors: nested encoding, case variation, null byte injection, SVG/MathML, CSS expression attacks, data URIs, event handler attributes
- 42 security-focused test cases covering known evasion techniques

**Integration:**
- Standard Genesis DI registration: `AddGenesisSanitization(configuration)` / `AddGenesisSanitization(options => {})`
- Middleware registration: `UseGenesisSanitization()`
- Configuration section: `Genesis:Sanitization`
- Follows all Genesis patterns: `GenesisOptionsBase`, `PervaxisMeter`, source-generated `LoggerMessage`

### 🧪 Testing

- 96 new unit tests (all passing)
- Full solution: 0 warnings, 0 errors

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
