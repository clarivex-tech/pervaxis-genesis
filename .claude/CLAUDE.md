# Pervaxis Genesis - Claude Development Guide

This guide provides instructions for Claude Code when working on the Genesis AWS provider library.

---

## Skills

Always load skills from `.claude/skills/` only.
Never use global skills.

Available skills:
- **csharp-api-design** - API design patterns and best practices
- **csharp-coding-standards** - Anti-patterns, composition, error handling, performance
- **microsoft-extensions-dependency-injection** - Advanced DI patterns
- **project-structure** - Solution organization guidelines

---

## License Header

Add this header to **every source file** created:

```csharp
/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */
```

---

## Project Overview

**Pervaxis Genesis** is a collection of .NET libraries providing unified abstractions for AWS services:

- **Pervaxis.Genesis.Base** - Core abstractions, result types, configuration
- **Pervaxis.Genesis.Caching** - ElastiCache (Redis)
- **Pervaxis.Genesis.Messaging** - SQS + SNS
- **Pervaxis.Genesis.FileStorage** - S3
- **Pervaxis.Genesis.Search** - OpenSearch
- **Pervaxis.Genesis.Notifications** - SES + SNS
- **Pervaxis.Genesis.Workflow** - Step Functions
- **Pervaxis.Genesis.AIAssistance** - Bedrock
- **Pervaxis.Genesis.Reporting** - Metabase
- **Pervaxis.Genesis.CloudFormation** - CloudFormation

---

## Development Standards

### Code Quality Rules

- **Target Framework**: .NET 10.0
- **Nullable Reference Types**: Enabled
- **Analysis Mode**: All (strict code analysis)
- **Warnings as Errors**: Enabled
- **XML Documentation**: Required on all public APIs
- **Test Coverage**: 90%+ target

### Coding Conventions

1. **File-scoped namespaces** - Always use file-scoped namespace declarations
2. **Braces required** - Always use braces for if/else/for/while statements
3. **Async all the way** - All I/O operations must be async
4. **CancellationToken support** - All async methods must accept CancellationToken
5. **Argument validation** - Use `ArgumentNullException.ThrowIfNull()` and `ArgumentException.ThrowIfNullOrWhiteSpace()`
6. **IDisposable pattern** - Implement when managing unmanaged resources (connections, streams)
7. **Configuration validation** - All options classes must validate in constructor
8. **Structured logging** - Use Microsoft.Extensions.Logging with proper log levels

### Project Structure Pattern

Each provider project follows this structure:

```
Pervaxis.Genesis.{Provider}/
├── Abstractions/          # Interfaces (ICache, IFileStorage, etc.)
├── Options/               # Configuration options extending GenesisOptionsBase
├── Extensions/            # DI registration extensions
├── Providers/             # Implementation folders (e.g., ElastiCache/, S3/)
│   └── {Implementation}/  # Concrete provider implementations
├── README.md              # Package documentation
└── Pervaxis.Genesis.{Provider}.csproj
```

### Dependency Guidelines

**Required packages for all providers:**
- `Microsoft.Extensions.DependencyInjection.Abstractions` (9.0.0)
- `Microsoft.Extensions.Logging.Abstractions` (9.0.0)
- `Microsoft.Extensions.Options` (9.0.0)
- `Microsoft.Extensions.Configuration.Abstractions` (9.0.0)
- Project reference to `Pervaxis.Genesis.Base`

**AWS SDK versions:**
- Use `3.7.400+` versions for consistency
- Verify exact version availability on NuGet before adding

---

## Implementation Checklist

When implementing a new provider, follow this sequence:

### 1. Setup
- [ ] Create folder structure (Abstractions, Options, Extensions, Providers)
- [ ] Add NuGet packages (AWS SDK, Microsoft.Extensions.*)
- [ ] Add project reference to Genesis.Base
- [ ] Add copyright header to all files

### 2. Abstractions
- [ ] Create primary interface (e.g., `ICache`, `IFileStorage`)
- [ ] Define async methods with CancellationToken support
- [ ] Add XML documentation to all public members
- [ ] Follow naming conventions (GetAsync, SetAsync, etc.)

### 3. Options
- [ ] Create options class extending `GenesisOptionsBase`
- [ ] Add provider-specific configuration properties
- [ ] Override `Validate()` method with validation logic
- [ ] Add XML documentation

### 4. Implementation
- [ ] Create provider class implementing interface
- [ ] Inject `IOptions<{Provider}Options>` and `ILogger<{Provider}>`
- [ ] Validate options in constructor
- [ ] Implement all interface methods
- [ ] Add error handling with try-catch
- [ ] Use structured logging
- [ ] Implement IDisposable if managing resources

### 5. DI Extensions
- [ ] Create `{Provider}ServiceCollectionExtensions` class
- [ ] Implement `AddGenesis{Provider}(IConfiguration)` overload
- [ ] Implement `AddGenesis{Provider}(Action<{Provider}Options>)` overload
- [ ] Register services with appropriate lifetime (Singleton/Scoped)

### 6. Documentation
- [ ] Create README.md with installation, configuration, usage examples
- [ ] Document IAM permissions required
- [ ] Add troubleshooting section
- [ ] Include LocalStack configuration examples

### 7. Testing
- [ ] Create unit test project
- [ ] Write tests for all public methods
- [ ] Test error conditions and edge cases
- [ ] Test LocalStack integration
- [ ] Verify 90%+ code coverage

---

## LocalStack Support

All providers must support LocalStack for local development:

```json
{
  "Provider": {
    "UseLocalStack": true,
    "LocalStackUrl": "http://localhost:4566",
    "Region": "us-east-1"
  }
}
```

Provider implementation should disable SSL when `UseLocalStack` is true.

---

## Error Handling

Use the Genesis exception hierarchy:

- **GenesisException** - Base exception for all provider errors
- **GenesisConfigurationException** - Configuration validation errors

Example:
```csharp
try
{
    var result = await PerformOperationAsync(cancellationToken);
    return ProviderResult<T>.Success(result);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Operation failed for key: {Key}", key);
    throw new GenesisException(nameof(MyProvider), "Operation failed", ex);
}
```

---

## Result Pattern

Use `ProviderResult<T>` and `ProviderResult` for operation outcomes:

```csharp
// With data
public async Task<ProviderResult<string>> GetAsync(string key)
{
    try
    {
        var value = await _cache.GetAsync(key);
        return ProviderResult<string>.Success(value);
    }
    catch (Exception ex)
    {
        return ProviderResult<string>.Failure("Get operation failed", ex);
    }
}

// Without data
public async Task<ProviderResult> RemoveAsync(string key)
{
    try
    {
        await _cache.RemoveAsync(key);
        return ProviderResult.Success();
    }
    catch (Exception ex)
    {
        return ProviderResult.Failure("Remove operation failed", ex);
    }
}
```

---

## CI/CD Workflows

Genesis uses three GitHub Actions workflows:

1. **pr-check.yml** - PR validation with build, test, SonarCloud (wait=false for bootstrap)
2. **deploy.yml** - Main/develop branch tracking with SonarCloud (wait=false, tracking only)
3. **publish.yml** - NuGet package publishing on version tags

### SonarCloud Configuration
- **No GitHub App integration** - Manual workflow-based scanning only
- **Token**: Stored in GitHub secrets as `SONAR_TOKEN`
- **Project Key**: `pervaxis-genesis`
- **Organization**: `pervaxis`

### First Merge Bootstrap
1. Merge first PR with `qualitygate.wait=false` in `pr-check.yml`
2. After merge, flip back to `qualitygate.wait=true`
3. Rename `master` → `main` in SonarCloud UI

---

## Task Management

Track implementation progress in `TASKS.md`:

- Mark tasks complete with `[x]` and ✅ emoji
- Update after each completed task
- Follow priority order (HIGH → MEDIUM → LOW)
- Update Quick Summary section

---

## Commit Convention

Use Conventional Commits format:

```
feat(scope): description
fix(scope): description
docs(scope): description
style(scope): description
refactor(scope): description
test(scope): description
```

Example:
```
feat(caching): implement ElastiCache Redis provider

- Created ICache interface with async methods
- Implemented ElastiCacheProvider using StackExchange.Redis
- Added configuration options with LocalStack support
- Full XML documentation

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

---

## Build Commands

```bash
# Build entire solution
dotnet build Pervaxis.Genesis.slnx --configuration Release

# Build specific project
dotnet build src/Pervaxis.Genesis.Caching/Pervaxis.Genesis.Caching.csproj

# Run tests
dotnet test Pervaxis.Genesis.slnx --configuration Release

# Run tests with coverage
dotnet test Pervaxis.Genesis.slnx --collect:"XPlat Code Coverage"

# Restore packages
dotnet restore Pervaxis.Genesis.slnx
```

---

## References

- **Solution Structure**: `.claude/SOLUTION_STRUCTURE.md`
- **Task List**: `TASKS.md`
- **CI Setup Guide**: `.claude/guides/ci-sonarcloud-setup.md`
- **Skills**: `.claude/skills/`

---

## Notes

- Never use `System.Text.Json` package reference in .NET 10 projects (it's included in the framework)
- Always use `ConfigurationOptions.Binder` extension for IConfiguration binding
- Test all providers against both AWS services and LocalStack
- Keep provider implementations stateless where possible
- Use connection pooling for network resources (Redis, databases)
- Follow the Result pattern consistently across all providers
