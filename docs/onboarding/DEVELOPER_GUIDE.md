# Pervaxis Genesis — Developer Onboarding Guide

> **Cloud-Provider Agnostic AWS Implementations**
>
> This guide gets a new engineer from zero to running tests and contributing their first Genesis provider implementation in under an hour.

---

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| .NET SDK | 10.x | https://dotnet.microsoft.com/download |
| Git | Latest | https://git-scm.com |
| AWS CLI v2 | Latest | https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html |
| Docker Desktop | Latest | https://www.docker.com/products/docker-desktop (for LocalStack) |
| IDE | VS 2022 / Rider / VS Code | Your choice |

### IDE Extensions

| Extension | Purpose |
|-----------|---------|
| [License Header Manager](https://marketplace.visualstudio.com/items?itemName=StefanWenig.LicenseHeaderManager) | Enforces Clarivex copyright header on all source files |
| C# Dev Kit (VS Code) or Rider | Language support |
| SonarLint | Local SonarQube analysis |

---

## Repository Setup

```bash
# 1. Clone
git clone https://github.com/clarivex-tech/pervaxis-genesis.git
cd pervaxis-genesis

# 2. Restore dependencies
dotnet restore Pervaxis.Genesis.slnx

# 3. Build
dotnet build Pervaxis.Genesis.slnx

# 4. Run tests
dotnet test Pervaxis.Genesis.slnx
```

All four steps should complete with zero errors and zero warnings. If they don't, stop and ask in `#platform-engineering` before proceeding.

---

## Solution Structure

```
Pervaxis.Genesis/
├── src/
│   ├── Pervaxis.Genesis.Base/              # Config loader, validation utilities
│   ├── Pervaxis.Genesis.Caching.AWS/       # AWS ElastiCache Redis
│   ├── Pervaxis.Genesis.Messaging.AWS/     # AWS SQS + SNS
│   ├── Pervaxis.Genesis.FileStorage.AWS/   # AWS S3
│   ├── Pervaxis.Genesis.Search.AWS/        # AWS OpenSearch
│   ├── Pervaxis.Genesis.Notifications.AWS/ # AWS SES + SNS
│   ├── Pervaxis.Genesis.Workflow.AWS/      # AWS Step Functions
│   ├── Pervaxis.Genesis.AIAssistance.AWS/  # AWS Bedrock
│   └── Pervaxis.Genesis.Reporting.AWS/     # Metabase API
├── tests/                                   # Unit & integration tests
├── docs/                                    # Documentation
│   ├── architecture/                        # ADRs
│   ├── governance/                          # RFCs
│   └── onboarding/                          # Developer guides
└── .claude/                                 # Claude Code configuration
    ├── CLAUDE.md                            # Development guide
    ├── guides/                              # Setup guides
    └── skills/                              # Coding standards
```

---

## Architecture Principle: Cloud-Provider Separation

### The Pattern

```
Pervaxis.Core.Abstractions.Genesis.Modules
  └─ ICache, IFileStorage, IMessaging (interfaces only)
  
Pervaxis.Genesis.*.AWS
  └─ AWS implementations (ElastiCache, S3, SQS)
  
Future:
  └─ Pervaxis.Genesis.*.Azure (Azure implementations)
  └─ Pervaxis.Genesis.*.GCP (GCP implementations)
```

### Why This Matters

- **Lean Dependencies**: Microservices only pull AWS packages, no Azure/GCP bloat
- **Easy Cloud Migration**: Swap `Genesis.Caching.AWS` → `Genesis.Caching.Azure` in DI only
- **Clear Boundaries**: Abstractions in Core, implementations in Genesis

---

## LocalStack Setup

Genesis providers support LocalStack for local development:

```bash
# 1. Start LocalStack
docker run -d \
  --name localstack \
  -p 4566:4566 \
  -e SERVICES=s3,sqs,sns,elasticache,opensearch,ses,stepfunctions,bedrock \
  localstack/localstack

# 2. Configure appsettings.Development.json
{
  "Caching": {
    "UseLocalEmulator": true,
    "LocalEmulatorUrl": "http://localhost:4566",
    "Region": "us-east-1"
  }
}

# 3. Run tests against LocalStack
dotnet test --filter Category=Integration
```

---

## Adding a New Provider

Follow the implementation checklist in `.claude/CLAUDE.md`:

### 1. Project Setup
```bash
# Create folder structure
mkdir -p src/Pervaxis.Genesis.{Provider}.AWS/{Abstractions,Options,Extensions,Providers}

# Add NuGet packages
dotnet add src/Pervaxis.Genesis.{Provider}.AWS package AWSSDK.{Service}
dotnet add src/Pervaxis.Genesis.{Provider}.AWS package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/Pervaxis.Genesis.{Provider}.AWS package Microsoft.Extensions.Logging.Abstractions

# Add Genesis.Base reference
dotnet add src/Pervaxis.Genesis.{Provider}.AWS reference src/Pervaxis.Genesis.Base
```

### 2. Implement Interface from Core
```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class MyAwsProvider : I{Provider}
{
    // Implement interface methods
    // Use AWS SDK
    // Handle LocalStack mode
}
```

### 3. Create Options
```csharp
using Pervaxis.Core.Abstractions.Genesis;

public class MyProviderOptions : GenesisOptionsBase
{
    public string ConnectionString { get; set; } = string.Empty;
    
    public override bool Validate()
    {
        if (!base.Validate()) return false;
        if (string.IsNullOrWhiteSpace(ConnectionString)) return false;
        return true;
    }
}
```

### 4. Add DI Extensions
```csharp
public static IServiceCollection AddGenesisMyProvider(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<MyProviderOptions>(configuration);
    services.TryAddSingleton<I{Provider}, MyAwsProvider>();
    return services;
}
```

### 5. Write Tests
- Unit tests for all public methods
- Integration tests with LocalStack
- 90%+ code coverage target

### 6. Document
- Create README.md in provider project
- List IAM permissions required
- Add usage examples

---

## Coding Standards

All standards from Pervaxis.Core apply to Genesis:

### 1. Copyright Header (Required)
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

### 2. File-Scoped Namespaces
```csharp
// ✅ Good
namespace Pervaxis.Genesis.Caching.AWS;

public class ElastiCacheProvider { }

// ❌ Bad
namespace Pervaxis.Genesis.Caching.AWS
{
    public class ElastiCacheProvider { }
}
```

### 3. Async All The Way
```csharp
// ✅ Good
public async Task<T> GetAsync(string key, CancellationToken ct = default)
{
    return await _cache.GetAsync(key, ct);
}

// ❌ Bad - blocking
public T Get(string key)
{
    return _cache.GetAsync(key).Result; // NEVER DO THIS
}
```

### 4. Argument Validation
```csharp
public MyProvider(IOptions<MyOptions> options, ILogger<MyProvider> logger)
{
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(logger);
    
    _options = options.Value;
    _logger = logger;
    
    if (!_options.Validate())
    {
        throw new GenesisConfigurationException(nameof(MyProvider), "Invalid configuration");
    }
}
```

### 5. Result Pattern
```csharp
using Pervaxis.Core.Abstractions.Genesis;

public async Task<ProviderResult<string>> GetAsync(string key)
{
    try
    {
        var value = await FetchAsync(key);
        return ProviderResult<string>.Success(value);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get key: {Key}", key);
        return ProviderResult<string>.Failure("Get operation failed", ex);
    }
}
```

---

## Testing Strategy

### Unit Tests
- Test all public methods
- Mock AWS SDK clients with Moq
- Test error conditions and edge cases
- Verify CancellationToken propagation

```csharp
[Fact]
public async Task GetAsync_WhenKeyExists_ReturnsValue()
{
    // Arrange
    var mockClient = new Mock<IAmazonS3>();
    mockClient.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
              .ReturnsAsync(new GetObjectResponse { /* ... */ });
    
    var provider = new S3Provider(mockClient.Object, _options, _logger);
    
    // Act
    var result = await provider.GetAsync("test-key");
    
    // Assert
    Assert.True(result.IsSuccess);
    Assert.NotNull(result.Data);
}
```

### Integration Tests
- Use LocalStack for AWS services
- Tag with `[Trait("Category", "Integration")]`
- Test against real AWS APIs in dev environment

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task GetAsync_WithLocalStack_ReturnsValue()
{
    // Arrange - LocalStack must be running
    var options = Options.Create(new CachingOptions
    {
        UseLocalEmulator = true,
        LocalEmulatorUrl = new Uri("http://localhost:4566"),
        Region = "us-east-1"
    });
    
    var provider = new ElastiCacheProvider(options, _logger);
    
    // Act & Assert
    await provider.SetAsync("test", "value");
    var result = await provider.GetAsync<string>("test");
    
    Assert.Equal("value", result);
}
```

---

## CI/CD Pipeline

### PR Workflow (`pr-check.yml`)
1. Build all projects
2. Run unit tests with coverage
3. SonarCloud analysis (quality gate)
4. Upload test results

### Deploy Workflow (`deploy.yml`)
1. Build on push to main/develop
2. Run all tests
3. SonarCloud tracking (no gate)
4. Store artifacts

### Publish Workflow (`publish.yml`)
1. Triggered on version tags (`v1.0.0`)
2. Build Release configuration
3. Run tests (must pass)
4. Pack NuGet packages
5. Publish to GitHub Packages
6. Create GitHub Release

---

## Common Tasks

### Build Single Provider
```bash
dotnet build src/Pervaxis.Genesis.Caching.AWS/Pervaxis.Genesis.Caching.AWS.csproj
```

### Run Tests for Single Provider
```bash
dotnet test tests/Pervaxis.Genesis.Caching.AWS.Tests/
```

### Check Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
# Coverage reports in TestResults/*/coverage.cobertura.xml
```

### Format Code
```bash
dotnet format Pervaxis.Genesis.slnx
```

### Check for Vulnerabilities
```bash
dotnet list package --vulnerable --include-transitive
```

---

## Troubleshooting

### Build Fails with CA Errors
- Check `.editorconfig` rules
- Ensure copyright header is present
- Run `dotnet format` to auto-fix style issues

### Tests Fail Locally
- Ensure LocalStack is running (`docker ps`)
- Check LocalStack logs (`docker logs localstack`)
- Verify AWS credentials are NOT set (LocalStack doesn't need them)

### SonarCloud Quality Gate Fails
- Review issues in SonarCloud UI
- Check `.claude/guides/ci-sonarcloud-setup.md` for bootstrap process
- Ensure coverage is ≥80% on new code

---

## Getting Help

| Question | Where to Ask |
|----------|--------------|
| Genesis provider implementation | `#genesis-dev` Slack |
| Pervaxis Core abstractions | `#platform-engineering` Slack |
| CI/CD pipeline issues | `#devops` Slack |
| Architecture decisions | Create ADR in `docs/architecture/` |

---

## Quick Reference

### Solution Build
```bash
dotnet build Pervaxis.Genesis.slnx --configuration Release
```

### Run All Tests
```bash
dotnet test Pervaxis.Genesis.slnx
```

### Create New Provider
```bash
# Use the implementation checklist in .claude/CLAUDE.md
# Follow the pattern in existing providers (Caching.AWS)
```

### Commit Format
```
feat(caching): add cache invalidation support
fix(messaging): handle SQS message deduplication
docs(base): update configuration examples
test(search): add OpenSearch integration tests
```

---

_Pervaxis Platform · Clarivex Technologies · Genesis Edition_  
_Last Updated: 2026-04-22_
