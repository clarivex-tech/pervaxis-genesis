# Pervaxis Genesis — Project Structure Guide

> Standard structure for all Genesis AWS provider packages

---

## Overview

Pervaxis.Genesis follows a consistent project structure across all AWS provider implementations. This guide documents the folder organization, naming conventions, and file patterns used throughout the solution.

---

## Solution Structure

```
Pervaxis.Genesis/
├── src/
│   ├── Pervaxis.Genesis.Base/              # Template config loader, base abstractions
│   ├── Pervaxis.Genesis.Caching/           # ElastiCache Redis
│   ├── Pervaxis.Genesis.Messaging/         # SQS + SNS
│   ├── Pervaxis.Genesis.Search/            # OpenSearch
│   ├── Pervaxis.Genesis.Workflow/          # Step Functions
│   ├── Pervaxis.Genesis.AIAssistance/      # Bedrock
│   ├── Pervaxis.Genesis.FileStorage/       # S3
│   ├── Pervaxis.Genesis.Notifications/     # SES + SNS
│   └── Pervaxis.Genesis.Reporting/         # Metabase REST API
│
├── tests/
│   ├── Pervaxis.Genesis.Base.Tests/
│   ├── Pervaxis.Genesis.Caching.Tests/
│   └── ... (one test project per provider)
│
├── docs/
│   ├── architecture/                       # ADRs
│   ├── PERVAXIS_STANDARDS.md
│   ├── PROJECT_STRUCTURE_GUIDE.md
│   └── DEPENDENCY_MANAGEMENT.md
│
├── Directory.Build.props                   # Shared build settings
├── global.json                             # .NET SDK version
├── Pervaxis.Genesis.sln
├── README.md
└── CHANGELOG.md
```

---

## Individual Project Structure

Each provider project follows this pattern:

```
src/Pervaxis.Genesis.<Provider>/
├── Abstractions/
│   ├── I<Provider>.cs                      # Main contract interface
│   ├── I<Provider>Client.cs                # Client interface (if needed)
│   └── <Provider>Result.cs                 # Result/response types
│
├── Options/
│   └── <Provider>Options.cs                # Configuration class
│
├── Extensions/
│   └── <Provider>ServiceCollectionExtensions.cs  # DI registration
│
├── Providers/
│   └── <AwsService>/                       # E.g., ElastiCache/, S3/, OpenSearch/
│       ├── <AwsService><Provider>.cs       # Implementation class
│       └── <AwsService>Exception.cs        # Custom exceptions (if needed)
│
├── Pervaxis.Genesis.<Provider>.csproj
└── README.md
```

---

## File Naming Conventions

### Interface Files
- **Pattern:** `I<Provider>.cs`
- **Examples:**
  - `ICache.cs`
  - `ISearchClient.cs`
  - `IWorkflowExecutor.cs`

### Implementation Files
- **Pattern:** `<AwsService><Provider>.cs`
- **Examples:**
  - `ElastiCacheProvider.cs`
  - `OpenSearchClient.cs`
  - `StepFunctionsWorkflowExecutor.cs`

### Options Files
- **Pattern:** `<Provider>Options.cs`
- **Examples:**
  - `CachingOptions.cs`
  - `SearchOptions.cs`
  - `WorkflowOptions.cs`

### Extension Files
- **Pattern:** `<Provider>ServiceCollectionExtensions.cs`
- **Examples:**
  - `CachingServiceCollectionExtensions.cs`
  - `SearchServiceCollectionExtensions.cs`

---

## Namespace Conventions

All namespaces follow the pattern: `Pervaxis.Genesis.<Provider>.<Subfolder>`

```csharp
// Abstractions
namespace Pervaxis.Genesis.Caching.Abstractions;

// Options
namespace Pervaxis.Genesis.Caching.Options;

// Extensions
namespace Pervaxis.Genesis.Caching.Extensions;

// Providers
namespace Pervaxis.Genesis.Caching.Providers.ElastiCache;
```

---

## Example: Caching Project Structure

```
src/Pervaxis.Genesis.Caching/
├── Abstractions/
│   ├── ICache.cs                           # Main caching interface
│   └── CacheEntry.cs                       # Cache entry model
│
├── Options/
│   └── CachingOptions.cs                   # Redis connection settings
│
├── Extensions/
│   └── CachingServiceCollectionExtensions.cs
│
├── Providers/
│   └── ElastiCache/
│       ├── ElastiCacheProvider.cs          # Redis implementation
│       └── ElastiCacheException.cs         # Custom exception
│
├── Pervaxis.Genesis.Caching.csproj
└── README.md
```

---

## Example: Messaging Project Structure

```
src/Pervaxis.Genesis.Messaging/
├── Abstractions/
│   ├── IMessagePublisher.cs                # SQS publisher
│   ├── INotificationPublisher.cs           # SNS publisher
│   └── MessageEnvelope.cs                  # Message wrapper
│
├── Options/
│   ├── MessagingOptions.cs                 # SQS + SNS settings
│   └── SqsOptions.cs                       # SQS-specific options
│
├── Extensions/
│   └── MessagingServiceCollectionExtensions.cs
│
├── Providers/
│   ├── Sqs/
│   │   └── SqsMessagePublisher.cs
│   └── Sns/
│       └── SnsNotificationPublisher.cs
│
├── Pervaxis.Genesis.Messaging.csproj
└── README.md
```

---

## .csproj File Template

Every provider project follows this template:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>Pervaxis.Genesis.<Provider></AssemblyName>
    <RootNamespace>Pervaxis.Genesis.<Provider></RootNamespace>
    <Description>[One-line description]. Implements AWS [Service] integration for the Pervaxis Genesis platform.</Description>
  </PropertyGroup>

  <!--
    Dependency justification:
    - AWSSDK.<Service>: AWS [Service] client. Apache 2.0 licensed.
    - Microsoft.Extensions.DependencyInjection.Abstractions: IServiceCollection for DI extension.
    - Microsoft.Extensions.Logging.Abstractions: ILogger<T> for structured diagnostic logging.
    - Microsoft.Extensions.Options: IOptions<T> for config-driven options.
    All packages pinned per Pervaxis dependency management standards.
  -->
  <ItemGroup>
    <PackageReference Include="AWSSDK.<Service>" Version="X.Y.Z" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Pervaxis.Genesis.Base\Pervaxis.Genesis.Base.csproj" />
  </ItemGroup>

</Project>
```

---

## README.md Template

Every project must have a README.md following this structure:

```markdown
# Pervaxis.Genesis.<Provider>

[One-line description of what this provider does]

## Installation

\`\`\`bash
dotnet add package Pervaxis.Genesis.<Provider>
\`\`\`

## Configuration

\`\`\`csharp
builder.Services.AddPervaxis<Provider>(options =>
{
    options.Region = "ap-south-1";
    // Provider-specific options
});
\`\`\`

Or via `appsettings.json`:

\`\`\`json
{
  "Pervaxis": {
    "Genesis": {
      "<Provider>": {
        "Region": "ap-south-1"
      }
    }
  }
}
\`\`\`

## Usage

\`\`\`csharp
public class MyService(I<Provider> provider)
{
    public async Task DoWorkAsync(CancellationToken ct)
    {
        // Usage example
    }
}
\`\`\`

## AWS IAM Requirements

\`\`\`json
{
  "Effect": "Allow",
  "Action": ["service:Action"],
  "Resource": "arn:aws:service:region:account:resource/*"
}
\`\`\`

---
*Pervaxis Platform · Clarivex Technologies · Genesis Edition*
```

---

## Test Project Structure

Each test project mirrors its source project:

```
tests/Pervaxis.Genesis.Caching.Tests/
├── Providers/
│   └── ElastiCache/
│       └── ElastiCacheProviderTests.cs
│
├── Extensions/
│   └── CachingServiceCollectionExtensionsTests.cs
│
└── Pervaxis.Genesis.Caching.Tests.csproj
```

### Test File Naming
- **Pattern:** `<ClassName>Tests.cs`
- **Examples:**
  - `ElastiCacheProviderTests.cs`
  - `SqsMessagePublisherTests.cs`

---

## Configuration Section Naming

All configuration follows the pattern: `Pervaxis:Genesis:<Provider>`

```json
{
  "Pervaxis": {
    "Genesis": {
      "Caching": { },
      "Messaging": { },
      "Search": { },
      "Workflow": { },
      "AIAssistance": { },
      "FileStorage": { },
      "Notifications": { },
      "Reporting": { }
    }
  }
}
```

---

## Checklist for New Provider

When adding a new provider to Genesis:

- [ ] Create folder: `src/Pervaxis.Genesis.<Provider>/`
- [ ] Create subfolders: `Abstractions/`, `Options/`, `Extensions/`, `Providers/<AwsService>/`
- [ ] Create interface: `Abstractions/I<Provider>.cs`
- [ ] Create options: `Options/<Provider>Options.cs`
- [ ] Create extension: `Extensions/<Provider>ServiceCollectionExtensions.cs`
- [ ] Create implementation: `Providers/<AwsService>/<AwsService><Provider>.cs`
- [ ] Create `.csproj` following template
- [ ] Create `README.md` following template
- [ ] Create test project: `tests/Pervaxis.Genesis.<Provider>.Tests/`
- [ ] Add project to `Pervaxis.Genesis.sln`
- [ ] Update `CHANGELOG.md` with new provider

---

*Pervaxis Platform · Clarivex Technologies · Genesis Edition*
