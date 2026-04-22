# Pervaxis Genesis — Dependency Management Guide

> Every dependency is a long-term commitment. This guide documents how to add, document, and maintain NuGet package dependencies in Genesis.

---

## Core Principle

**Every NuGet dependency requires explicit justification.**

Dependencies introduce:
- Security surface area (CVEs to monitor)
- Versioning constraints (compatibility matrix)
- Licensing obligations (legal review)
- Breaking change risk (major version upgrades)

Add dependencies deliberately, not casually.

---

## Approval Process

### 1. Before Adding a Dependency

Ask these questions:

1. **Can we avoid this dependency?**
   - Can we implement the feature ourselves in <100 lines?
   - Is there already a similar package in our dependency tree?

2. **Is this package well-maintained?**
   - Last release within 12 months?
   - GitHub stars > 500 (for community packages)?
   - Active issue triage?

3. **Is the license acceptable?**
   - MIT / Apache 2.0: ✅ Approved
   - BSD: ✅ Approved (check specific variant)
   - GPL / AGPL: ❌ Requires legal review
   - Proprietary: ❌ Requires legal review

4. **What's the dependency chain?**
   ```bash
   dotnet list package --include-transitive
   ```
   Avoid packages that pull in 10+ transitive dependencies.

### 2. Open a GitHub Issue

**Title:** `[Dependency] Add <PackageName> to <ProjectName>`

**Template:**
```markdown
## Package Details
- **Name:** <PackageName>
- **Version:** X.Y.Z
- **License:** MIT / Apache 2.0 / etc.
- **NuGet:** https://www.nuget.org/packages/<PackageName>
- **Repository:** https://github.com/org/repo

## Justification
[Why is this dependency necessary? What problem does it solve that we can't solve ourselves?]

## Alternatives Considered
- **Option 1:** [Alternative package] — Rejected because [reason]
- **Option 2:** [Implement ourselves] — Rejected because [reason]

## Impact
- **Target Project:** Pervaxis.Genesis.<Provider>
- **Transitive Dependencies:** [List major transitive deps]
- **Breaking Change Risk:** Low / Medium / High

## Approval
- [ ] License checked
- [ ] Maintenance status verified
- [ ] Team reviewed
```

### 3. Wait for Approval

A team member must approve the issue before proceeding.

---

## Adding the Dependency

### 1. Add PackageReference

```xml
<ItemGroup>
  <PackageReference Include="PackageName" Version="X.Y.Z" />
</ItemGroup>
```

**Version pinning rules:**
- ✅ **Exact version:** `Version="3.7.500"` (preferred)
- ✅ **Minimum version:** `Version="3.7.500" Condition="'$(TargetFramework)' == 'net10.0'"`
- ❌ **Floating version:** `Version="3.7.*"` (never use)

### 2. Add Justification Comment

Every `<ItemGroup>` with dependencies must have a comment block:

```xml
<!--
  Dependency justification:
  - AWSSDK.ElastiCacheCluster: AWS ElastiCache client for Redis caching. Apache 2.0 licensed.
    GitHub Issue: #42
  - StackExchange.Redis: Redis client library for ElastiCache communication. MIT licensed.
    GitHub Issue: #43
  - Microsoft.Extensions.DependencyInjection.Abstractions: IServiceCollection for DI extension.
  - Microsoft.Extensions.Logging.Abstractions: ILogger<T> for structured diagnostic logging.
  - Microsoft.Extensions.Options: IOptions<T> for config-driven options.
  All packages pinned per Pervaxis dependency management standards.
-->
<ItemGroup>
  <PackageReference Include="AWSSDK.ElastiCacheCluster" Version="3.7.400" />
  <PackageReference Include="StackExchange.Redis" Version="2.8.0" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
</ItemGroup>
```

### 3. Document in CHANGELOG.md

```markdown
## [1.1.0] - 2026-04-21

### Added
- feat(caching): add ElastiCache Redis support (#42)

### Dependencies
- Added `AWSSDK.ElastiCacheCluster` 3.7.400
- Added `StackExchange.Redis` 2.8.0
```

---

## Standard Dependencies

These dependencies are pre-approved for all Genesis projects:

### Microsoft.Extensions.*

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="9.0.0" />
```

**Justification:** Core .NET abstractions for DI, logging, and configuration.

### AWSSDK.*

All official AWS SDK packages are pre-approved:

```xml
<PackageReference Include="AWSSDK.Core" Version="3.7.400" />
<PackageReference Include="AWSSDK.S3" Version="3.7.400" />
<PackageReference Include="AWSSDK.SQS" Version="3.7.500" />
<PackageReference Include="AWSSDK.SimpleNotificationService" Version="3.7.400" />
<PackageReference Include="AWSSDK.OpenSearchService" Version="3.7.400" />
<PackageReference Include="AWSSDK.Bedrock" Version="3.7.400" />
<PackageReference Include="AWSSDK.StepFunctions" Version="3.7.400" />
<PackageReference Include="AWSSDK.SimpleEmail" Version="3.7.400" />
```

**Justification:** Official AWS service clients. Apache 2.0 licensed.

**Version strategy:** Pin to latest stable. Upgrade quarterly or when CVEs are patched.

### Test Frameworks

```xml
<PackageReference Include="xunit" Version="2.8.0" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.0" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
```

**Justification:** Standard .NET testing stack.

---

## AWS SDK Version Management

### Current AWS SDK Versions

As of 2026-04-21, target these versions:

| Package | Version | Notes |
|---------|---------|-------|
| `AWSSDK.Core` | 3.7.400.18 | Base SDK |
| `AWSSDK.S3` | 3.7.400.18 | S3 client |
| `AWSSDK.SQS` | 3.7.500.0 | SQS client |
| `AWSSDK.SimpleNotificationService` | 3.7.400.18 | SNS client |
| `AWSSDK.OpenSearchService` | 3.7.400.0 | OpenSearch client |
| `AWSSDK.Bedrock` | 3.7.400.0 | Bedrock AI |
| `AWSSDK.StepFunctions` | 3.7.400.0 | Step Functions |
| `AWSSDK.SimpleEmail` | 3.7.400.0 | SES client |

### Upgrade Strategy

**Quarterly upgrades:** Review AWS SDK releases every quarter (Jan, Apr, Jul, Oct).

**CVE-driven upgrades:** If a CVE affects an AWS SDK package, upgrade immediately.

**Check for updates:**
```bash
dotnet list package --outdated --include-prerelease
```

**Upgrade command:**
```bash
dotnet add package AWSSDK.S3 --version 3.7.401.0
```

---

## Avoiding Dependency Bloat

### Red Flags

Avoid packages that:
- Haven't been updated in >2 years
- Have <100 GitHub stars and <10 contributors
- Pull in >10 transitive dependencies
- Have open CVEs with no fix timeline

### Transitive Dependency Audit

Check what your dependencies depend on:

```bash
# List all dependencies (direct + transitive)
dotnet list package --include-transitive

# Generate dependency graph
dotnet msbuild /t:GenerateRestoreGraphFile /p:RestoreGraphOutputPath=graph.dgml
```

Open `graph.dgml` in Visual Studio to visualize the dependency tree.

---

## Removing a Dependency

If a dependency is no longer needed:

1. **Remove from .csproj**
   ```xml
   <!-- Remove this line -->
   <PackageReference Include="OldPackage" Version="1.0.0" />
   ```

2. **Update justification comment**
   Remove the package from the comment block.

3. **Document in CHANGELOG.md**
   ```markdown
   ### Removed
   - Removed `OldPackage` — no longer needed after refactoring (#56)
   ```

4. **Verify no broken references**
   ```bash
   dotnet build
   dotnet test
   ```

---

## Dependency Security

### NuGet Package Signing

All AWS SDK packages are signed by Amazon. Verify signature:

```bash
dotnet nuget verify AWSSDK.S3.3.7.400.nupkg
```

### CVE Monitoring

Check for known vulnerabilities:

```bash
dotnet list package --vulnerable --include-transitive
```

Run this in CI/CD pipelines — fail the build if vulnerabilities are found.

### GitHub Dependabot

Enable Dependabot in the repository:

```yaml
# .github/dependabot.yml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 5
```

---

## Version Conflicts

If two packages depend on different versions of the same transitive dependency:

### 1. Check the Conflict

```bash
dotnet restore --verbosity detailed
```

Look for warnings like:
```
NU1605: Detected package downgrade: PackageName from 2.0.0 to 1.5.0
```

### 2. Force a Specific Version

Add a direct `<PackageReference>` to force the higher version:

```xml
<ItemGroup>
  <!-- Force PackageName to 2.0.0 to resolve conflict -->
  <PackageReference Include="PackageName" Version="2.0.0" />
</ItemGroup>
```

### 3. Document the Override

```xml
<!--
  Version override:
  - PackageName: Forced to 2.0.0 to resolve conflict between PackageA (needs 2.0.0)
    and PackageB (needs 1.5.0). PackageB is compatible with 2.0.0 per testing.
    GitHub Issue: #67
-->
```

---

## Checklist for Adding a Dependency

- [ ] GitHub Issue opened and approved
- [ ] License verified (MIT / Apache 2.0 acceptable)
- [ ] Maintenance status checked (last release within 12 months)
- [ ] Version pinned explicitly (no floating versions)
- [ ] Justification comment added in `.csproj`
- [ ] Issue number referenced in comment
- [ ] Transitive dependencies reviewed
- [ ] CHANGELOG.md updated
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test`
- [ ] No vulnerabilities: `dotnet list package --vulnerable`

---

*Pervaxis Platform · Clarivex Technologies · Genesis Edition*
