# Cloud-Provider Separation Architecture

| Field | Value |
|-------|-------|
| **Status** | Accepted |
| **Date** | 2026-04-22 |
| **Authors** | Anand Jayaseelan, Claude Sonnet 4.5 |
| **Section** | Genesis Architecture |

---

## Context

Pervaxis Genesis provides cloud provider abstractions for common infrastructure services (caching, storage, messaging, etc.). We need an architecture that:

1. **Prevents bloat**: Microservices shouldn't pull unused cloud provider SDKs
2. **Enables multi-cloud**: Easy to switch from AWS to Azure or GCP
3. **Maintains consistency**: Same interfaces across all cloud providers
4. **Supports testing**: LocalStack, Azurite, GCP emulators for local development

---

## Decision

> **We will separate abstractions into Pervaxis.Core and implementations into cloud-specific packages (Pervaxis.Genesis.*.AWS, *.Azure, *.GCP) to enable lean, cloud-agnostic microservices.**

---

## Architecture

### Layer 1: Abstractions (Pervaxis.Core)

**Location**: `Pervaxis.Core.Abstractions.Genesis.Modules`

**Contains**:
- Interface definitions (`ICache`, `IFileStorage`, `IMessaging`, etc.)
- Shared types (`ProviderResult<T>`, `GenesisOptionsBase`)
- No cloud-specific code
- No SDK dependencies

**Example**:
```csharp
namespace Pervaxis.Core.Abstractions.Genesis.Modules;

public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);
}
```

### Layer 2: Cloud-Specific Implementations

**Naming Convention**: `Pervaxis.Genesis.{Module}.{CloudProvider}`

**Examples**:
- `Pervaxis.Genesis.Caching.AWS` (ElastiCache Redis)
- `Pervaxis.Genesis.Caching.Azure` (Azure Cache for Redis)
- `Pervaxis.Genesis.Caching.GCP` (Memorystore)

**Contains**:
- Implementation of Core interfaces
- Cloud provider SDK dependencies (AWSSDK.*, Azure.*, Google.Cloud.*)
- Provider-specific options extending `GenesisOptionsBase`
- DI registration extensions

**Example**:
```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;
using AWSSDK.ElastiCache;

namespace Pervaxis.Genesis.Caching.AWS;

public class ElastiCacheProvider : ICache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        // AWS-specific implementation using StackExchange.Redis
        // Supports ElastiCache configuration endpoint discovery
    }
}
```

### Layer 3: Application Integration

**Microservice Configuration**:
```csharp
// Startup.cs - AWS microservice
builder.Services.AddGenesisCaching(configuration.GetSection("Caching"));
// Pulls: Pervaxis.Genesis.Caching.AWS + AWSSDK.ElastiCache
// Does NOT pull: Azure.*, Google.Cloud.*

// Startup.cs - Azure microservice (future)
builder.Services.AddGenesisCachingAzure(configuration.GetSection("Caching"));
// Pulls: Pervaxis.Genesis.Caching.Azure + Azure.Cache
// Does NOT pull: AWSSDK.*, Google.Cloud.*
```

---

## Rationale

### Options Considered

| Option | Pros | Cons | Decision |
|--------|------|------|----------|
| **Monolithic Genesis package** | Simple single install | Forces all cloud SDKs on every microservice (100+ MB bloat) | ❌ Rejected |
| **Runtime provider selection** | Single package, switch via config | Still includes all SDKs, complex factory pattern | ❌ Rejected |
| **Separate packages per cloud** | Lean dependencies, clear boundaries | More NuGet packages to manage | ✅ **Selected** |

### Key Factors

1. **Deployment Size**: AWS microservice deploys ~50MB less without Azure SDKs
2. **Startup Time**: Fewer assemblies to load = faster cold starts
3. **Security**: Reduces attack surface (no unused dependencies)
4. **Maintainability**: Each cloud provider package is independent
5. **Future-Proof**: Easy to add GCP, Alibaba Cloud, Oracle Cloud

---

## Consequences

### Positive

✅ **Lean Microservices**
- AWS services pull only AWSSDK.* packages
- No Azure/GCP bloat in deployment packages

✅ **Easy Cloud Migration**
- Change NuGet package reference
- Update DI registration
- No code changes in business logic

✅ **Independent Versioning**
- AWS provider can be v2.0 while Azure is v1.0
- Breaking changes don't affect other cloud providers

✅ **Clear Dependencies**
- Static analysis tools can verify cloud provider compliance
- Architectural boundaries enforced at compile time

### Negative

⚠️ **More NuGet Packages**
- 8 modules × 3 clouds = 24 packages eventually
- Mitigation: Use semantic versioning, synchronized releases

⚠️ **DI Registration Variance**
- Each cloud provider may have unique options
- Mitigation: All extend `GenesisOptionsBase` for consistency

### Risks

🔴 **Interface Breaking Changes**
- Changing `ICache` affects all cloud providers
- Mitigation: Use semantic versioning, ADRs for breaking changes

🟡 **Feature Parity**
- Azure may support features AWS doesn't (or vice versa)
- Mitigation: Interface defines common subset, provider-specific extensions allowed

---

## Implementation

### Package Structure

```
Pervaxis.Core.Abstractions
  └─ Genesis/
      ├─ IGenesisModule.cs
      ├─ ProviderResult.cs
      ├─ GenesisOptionsBase.cs
      └─ Modules/
          ├─ ICache.cs
          ├─ IFileStorage.cs
          ├─ IMessaging.cs
          ├─ ISearch.cs
          ├─ INotification.cs
          ├─ IWorkflow.cs
          ├─ IAIAssistance.cs
          └─ IReporting.cs

Pervaxis.Genesis.Base
  └─ Configuration/   # Template loader, validation utilities

Pervaxis.Genesis.{Module}.AWS
  ├─ Options/         # AWS-specific options
  ├─ Providers/       # AWS SDK implementation
  └─ Extensions/      # DI registration

Future:
Pervaxis.Genesis.{Module}.Azure
Pervaxis.Genesis.{Module}.GCP
```

### NuGet Package Metadata

| Property | Value |
|----------|-------|
| **PackageId** | `Pervaxis.Genesis.{Module}.{Cloud}` |
| **Version** | SemVer 2.0 (synchronized across modules) |
| **Dependencies** | Pervaxis.Core.Abstractions, Cloud SDK |
| **Tags** | pervaxis, genesis, {cloud}, {module} |
| **License** | Proprietary |

### Migration Path

1. **Phase 1 (Current)**: AWS implementations only
   - Pervaxis.Genesis.*.AWS packages
   - Reference implementations for future clouds

2. **Phase 2 (Future)**: Add Azure support
   - Pervaxis.Genesis.*.Azure packages
   - Same interfaces, Azure SDK implementations

3. **Phase 3 (Future)**: Add GCP support
   - Pervaxis.Genesis.*.GCP packages
   - Same interfaces, GCP client library implementations

---

## Testing Strategy

### Unit Tests
- Test interface implementations with mocked cloud SDKs
- Each cloud provider has isolated test suite

### Integration Tests
- AWS: LocalStack
- Azure: Azurite, Azure Storage Emulator
- GCP: GCP Emulator suite

### Contract Tests
- Verify all cloud providers satisfy interface contracts
- Common test suite runs against all implementations

---

## Real-World Example

### Before (Monolithic)
```bash
# Microservice deployment
Pervaxis.Genesis.dll          (15 MB)
AWSSDK.*.dll                  (25 MB)
Azure.*.dll                   (30 MB) # UNUSED!
Google.Cloud.*.dll            (35 MB) # UNUSED!
Total:                        105 MB
```

### After (Separated)
```bash
# AWS Microservice
Pervaxis.Core.Abstractions.dll (1 MB)
Pervaxis.Genesis.Caching.AWS.dll (500 KB)
AWSSDK.ElastiCache.dll         (5 MB)
Total:                         6.5 MB  # 94% reduction!

# Azure Microservice (future)
Pervaxis.Core.Abstractions.dll (1 MB)
Pervaxis.Genesis.Caching.Azure.dll (500 KB)
Azure.Cache.dll                (4 MB)
Total:                         5.5 MB
```

---

## Related

- **Genesis Spec**: `GNS-REQ-001` - Genesis Provider Architecture
- **Core Abstractions**: `Pervaxis.Core.Abstractions/Genesis/`
- **Implementation Guide**: `.claude/CLAUDE.md`
- **Developer Guide**: `docs/onboarding/DEVELOPER_GUIDE.md`

---

## Appendix: Interface Coverage

| Module | AWS Service | Azure Service | GCP Service | Status |
|--------|-------------|---------------|-------------|--------|
| Caching | ElastiCache Redis | Azure Cache for Redis | Memorystore | ✅ AWS Impl |
| FileStorage | S3 | Blob Storage | Cloud Storage | 🔄 Planned |
| Messaging | SQS/SNS | Service Bus | Pub/Sub | 🔄 Planned |
| Search | OpenSearch | Cognitive Search | Cloud Search | 🔄 Planned |
| Notification | SES/SNS | Communication | Firebase Cloud Messaging | 🔄 Planned |
| Workflow | Step Functions | Logic Apps | Workflows | 🔄 Planned |
| AIAssistance | Bedrock | OpenAI | Vertex AI | 🔄 Planned |
| Reporting | Metabase (EC2) | Power BI | Looker | 🔄 Planned |

---

_Pervaxis Platform · Clarivex Technologies · Genesis Edition_  
_Architecture Decision Record_
