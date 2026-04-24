# Core Abstractions Compliance Guide

## Overview

Pervaxis Genesis is a **platform project** that provides AWS-specific implementations of abstractions defined in **Pervaxis.Core**. This document outlines strict compliance requirements for maintaining architectural integrity.

---

## Architecture Principle

**RULE: All abstractions MUST be defined in Pervaxis.Core.Abstractions first, then referenced via NuGet.**

```
┌─────────────────────────────────────────┐
│   Pervaxis.Core.Abstractions (NuGet)   │
│   - Genesis Module Interfaces          │
│   - ICache, IMessaging, IFileStorage   │
│   - GenesisOptionsBase, ProviderResult │
└─────────────────────────────────────────┘
                    ▲
                    │ (NuGet reference)
                    │
┌─────────────────────────────────────────┐
│      Pervaxis.Genesis.Base              │
│      - Configuration loader             │
│      - DI helpers                       │
│      - GenesisException                 │
└─────────────────────────────────────────┘
                    ▲
                    │ (Project reference)
                    │
┌─────────────────────────────────────────┐
│  Pervaxis.Genesis.*.AWS (Providers)     │
│  - Caching.AWS → ICache                 │
│  - Messaging.AWS → IMessaging           │
│  - FileStorage.AWS → IFileStorage       │
└─────────────────────────────────────────┘
```

---

## Compliance Rules

### ❌ NEVER DO THIS

```csharp
// ❌ DO NOT create local interfaces in Genesis projects
namespace Pervaxis.Genesis.FileStorage.AWS.Abstractions
{
    public interface IFileStorage { } // WRONG!
}
```

### ✅ ALWAYS DO THIS

```csharp
// ✅ ALWAYS reference from Core.Abstractions
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class S3FileStorageProvider : IFileStorage
{
    // Implementation using Core abstraction
}
```

---

## Process for Adding New Providers

### Step 1: Check Core.Abstractions

Before implementing a new Genesis provider, **verify the interface exists in Pervaxis.Core.Abstractions**:

```bash
# Check NuGet package for the interface
dotnet list package Pervaxis.Core.Abstractions --include-transitive
```

Expected namespace: `Pervaxis.Core.Abstractions.Genesis.Modules`

### Step 2: Add Interface to Core (if missing)

**If the interface does NOT exist:**

1. **STOP Genesis development**
2. Switch to **Pervaxis.Core repository**
3. Add the interface to `src/Pervaxis.Core.Abstractions/Genesis/Modules/`
4. Follow Core project conventions:
   - Async all the way (every method has `CancellationToken`)
   - Nullable reference types enabled
   - XML documentation on all public APIs
   - Return `Task<T>` or `Task<T?>` appropriately
5. Update Core CHANGELOG.md
6. Publish new Core.Abstractions NuGet package
7. Update Core version in Genesis `Directory.Build.props`

### Step 3: Reference in Genesis

Only after Core.Abstractions NuGet is updated:

```xml
<ItemGroup>
  <!-- Pervaxis.Genesis.Base already references Core.Abstractions transitively -->
  <ProjectReference Include="..\Pervaxis.Genesis.Base\Pervaxis.Genesis.Base.csproj" />
</ItemGroup>
```

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class AwsProvider : IModuleInterface
{
    // Implementation
}
```

---

## Interface Checklist for Core.Abstractions

When adding a new Genesis module interface to Core:

### File Location
```
Pervaxis.Core/
└── src/Pervaxis.Core.Abstractions/
    └── Genesis/
        └── Modules/
            ├── ICache.cs
            ├── IMessaging.cs
            ├── IFileStorage.cs      ← Add new interfaces here
            ├── ISearch.cs
            ├── INotification.cs
            └── ...
```

### Interface Template

```csharp
/*
 * Copyright header for Pervaxis.Core
 */

namespace Pervaxis.Core.Abstractions.Genesis.Modules;

/// <summary>
/// Abstraction for [module purpose] operations.
/// Implemented by cloud-specific providers (AWS, Azure, GCP).
/// </summary>
public interface IModuleName
{
    /// <summary>
    /// [Operation description]
    /// </summary>
    /// <param name="param">[Param description]</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>[Return description]</returns>
    Task<Result> OperationAsync(
        string param,
        CancellationToken cancellationToken = default);

    // More methods...
}
```

### Design Guidelines

1. **Async-first**: All I/O methods return `Task<T>`
2. **CancellationToken**: Always last parameter with `= default`
3. **Nullable**: Use `T?` for optional returns (e.g., `Task<Stream?>`)
4. **Cloud-agnostic**: No AWS/Azure/GCP-specific types in interface
5. **Simple types**: Prefer primitives, streams, dictionaries over custom DTOs
6. **XML docs**: Required on all public members

---

## Current Interface Status

### ✅ Available in Core.Abstractions

| Interface | Namespace | Status | Genesis Provider |
|---|---|---|---|
| `ICache` | `Pervaxis.Core.Abstractions.Genesis.Modules` | ✅ Available | `Pervaxis.Genesis.Caching.AWS` |
| `IMessaging` | `Pervaxis.Core.Abstractions.Genesis.Modules` | ✅ Available | `Pervaxis.Genesis.Messaging.AWS` |

### ❌ Missing from Core.Abstractions

| Interface | Needed By | Action Required |
|---|---|---|
| `IFileStorage` | `Pervaxis.Genesis.FileStorage.AWS` | Add to Core.Abstractions v1.1.0 |
| `ISearch` | `Pervaxis.Genesis.Search.AWS` | Add to Core.Abstractions v1.1.0 |
| `INotification` | `Pervaxis.Genesis.Notifications.AWS` | Add to Core.Abstractions v1.1.0 |
| `IWorkflow` | `Pervaxis.Genesis.Workflow.AWS` | Add to Core.Abstractions v1.1.0 |
| `IAIAssistant` | `Pervaxis.Genesis.AIAssistance.AWS` | Add to Core.Abstractions v1.1.0 |
| `IReporting` | `Pervaxis.Genesis.Reporting.AWS` | Add to Core.Abstractions v1.1.0 |

---

## What If I Accidentally Create Local Interfaces?

### Immediate Actions

1. **Delete local interface files** (e.g., `Abstractions/IFileStorage.cs`)
2. **Revert project changes** (remove local namespace references)
3. **Document the interface** in this guide's "Missing" table
4. **Notify team** to prioritize Core.Abstractions update
5. **Do NOT proceed** with implementation until Core is updated

### Git Commit Message

```
revert(filestorage): remove local IFileStorage interface

- Local interfaces violate Genesis architectural compliance
- IFileStorage must be added to Pervaxis.Core.Abstractions first
- Documented in .claude/guides/CORE_ABSTRACTIONS_COMPLIANCE.md
- Blocked on Core.Abstractions v1.1.0 update

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

---

## Version Management

### Core.Abstractions Versioning

Genesis depends on specific Core.Abstractions versions:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <CoreAbstractionsVersion>1.0.0</CoreAbstractionsVersion>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Pervaxis.Core.Abstractions" Version="$(CoreAbstractionsVersion)" />
</ItemGroup>
```

### Update Process

1. Core team publishes new Core.Abstractions NuGet (e.g., 1.1.0 with IFileStorage)
2. Update `<CoreAbstractionsVersion>` in Genesis `Directory.Build.props`
3. Run `dotnet restore Pervaxis.Genesis.slnx`
4. Verify build succeeds
5. Proceed with Genesis provider implementation

---

## FAQs

### Q: Why can't I just create a local interface and move it later?

**A:** This violates platform architectural principles:
- Creates technical debt and duplication
- Breaks consistency across Genesis providers
- Makes future refactoring complex (namespace changes, breaking consumers)
- Compromises the "single source of truth" for abstractions

### Q: What if I need a quick prototype?

**A:** Use a feature branch in **Pervaxis.Core** first:
1. Add interface to Core.Abstractions (feature branch)
2. Use local Core project reference in Genesis (temporary)
3. Once validated, merge Core PR and publish NuGet
4. Switch Genesis back to NuGet reference

### Q: How long does Core update take?

**A:** Typical timeline:
- Add interface to Core: 30 minutes
- Core PR review: 1-2 hours
- NuGet publish: 15 minutes
- Genesis reference update: 5 minutes

**Total: 2-3 hours** (same day, not blocking)

### Q: Can I create internal interfaces for provider-specific logic?

**A:** Yes, internal interfaces are fine:

```csharp
// ✅ OK: Internal interface for AWS-specific logic
namespace Pervaxis.Genesis.FileStorage.AWS.Providers.S3;

internal interface IS3ClientFactory
{
    IAmazonS3 CreateClient(FileStorageOptions options);
}
```

But **public abstractions** must live in Core.Abstractions.

---

## Enforcement

### Code Review Checklist

- [ ] No `Abstractions/` folder in Genesis provider projects
- [ ] All interfaces referenced from `Pervaxis.Core.Abstractions.Genesis.Modules`
- [ ] No local duplicates of Core types (GenesisOptionsBase, ProviderResult, etc.)
- [ ] Project reference is to Genesis.Base only (Core comes transitively)

### CI/CD Checks

Consider adding:
```bash
# Fail build if local Abstractions folder exists in provider projects
find src/Pervaxis.Genesis.*.AWS -type d -name "Abstractions" | grep -q . && exit 1
```

---

## Summary

**Before implementing any Genesis provider:**

1. ✅ Check if interface exists in Core.Abstractions NuGet
2. ❌ If missing: **STOP** → Add to Core first
3. ✅ Once Core updated: Reference via NuGet and implement
4. 📝 Update this guide if you discover missing interfaces

**Remember: Genesis is a platform project. Architectural compliance is non-negotiable.**

---

*Document created: 2026-04-22*  
*Last updated: 2026-04-22*  
*Maintainer: Pervaxis Platform Team*
