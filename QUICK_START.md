# Pervaxis Genesis - Quick Start Guide

> **For resuming work in future sessions**

---

## 📍 Current Status

✅ **Solution structure created** (18 projects)  
✅ **Documentation complete** (7 guides)  
✅ **Build verified** (0 warnings, 0 errors)  
🔄 **Next: Implement providers** (see TASKS.md)

---

## 📂 Key Files to Review

| File | Purpose | Read First? |
|------|---------|-------------|
| **TASKS.md** | Complete task list with implementation order | ⭐ YES |
| **docs/SOLUTION_SETUP.md** | What was created, templates, roadmap | ⭐ YES |
| **docs/PERVAXIS_STANDARDS.md** | Code standards and conventions | ⭐ YES |
| **docs/PROJECT_STRUCTURE_GUIDE.md** | How to structure projects | When implementing |
| **docs/DEPENDENCY_MANAGEMENT.md** | NuGet package guidelines | When adding packages |
| **CHANGELOG.md** | Track changes here | After each task |

---

## 🚀 Start Next Session

### Step 1: Navigate to Solution
```bash
cd C:\Anand\Clarivex\Pervaxis\Code\Genesis
```

### Step 2: Verify Build
```bash
dotnet build Pervaxis.Genesis.slnx
```
Should show: `Build succeeded. 0 Warning(s), 0 Error(s)`

### Step 3: Open Task List
```bash
# Read TASKS.md to see what's next
code TASKS.md
```

### Step 4: Pick Next Task
Start with **Phase 1: Task 1.1** (Pervaxis.Genesis.Base)

---

## 📋 Recommended Implementation Order

### Priority 1 (Start Here)
1. **Pervaxis.Genesis.Base** - Foundation project
2. **Pervaxis.Genesis.Caching** - Most commonly used
3. **Pervaxis.Genesis.Messaging** - Core messaging (SQS + SNS)

### Priority 2
4. **Pervaxis.Genesis.FileStorage** - S3 storage
5. **Pervaxis.Genesis.Search** - OpenSearch
6. **Pervaxis.Genesis.Notifications** - Email (SES)

### Priority 3
7. **Pervaxis.Genesis.Workflow** - Step Functions
8. **Pervaxis.Genesis.AIAssistance** - Bedrock
9. **Pervaxis.Genesis.Reporting** - Metabase

---

## 🔨 Implementation Template

For each provider, follow this pattern:

### 1. Create Folder Structure
```bash
cd src/Pervaxis.Genesis.<Provider>
mkdir Abstractions Options Extensions Providers
mkdir Providers/<AwsService>
```

### 2. Add Dependencies
```bash
dotnet add package AWSSDK.<Service> --version 3.7.xxx
dotnet add package Microsoft.Extensions.DependencyInjection.Abstractions --version 9.0.0
dotnet add package Microsoft.Extensions.Logging.Abstractions --version 9.0.0
dotnet add package Microsoft.Extensions.Options --version 9.0.0
dotnet add reference ../Pervaxis.Genesis.Base
```

### 3. Create Core Files
- `Abstractions/I<Provider>.cs` - Interface
- `Options/<Provider>Options.cs` - Configuration
- `Extensions/<Provider>ServiceCollectionExtensions.cs` - DI
- `Providers/<AwsService>/<AwsService><Provider>.cs` - Implementation
- `README.md` - Documentation

### 4. Add Copyright Header
```csharp
// Copyright © Clarivex Technologies. All rights reserved.
```

### 5. Write Tests
```bash
cd tests/Pervaxis.Genesis.<Provider>.Tests
# Add test files
dotnet test
```

### 6. Update CHANGELOG.md
Document what you added.

---

## 📚 Code Templates

### Interface Template
```csharp
// Copyright © Clarivex Technologies. All rights reserved.

namespace Pervaxis.Genesis.<Provider>.Abstractions;

/// <summary>
/// Defines contract for <Provider> operations.
/// </summary>
public interface I<Provider>
{
    /// <summary>
    /// Does something async.
    /// </summary>
    Task<Result> DoSomethingAsync(string param, CancellationToken cancellationToken = default);
}
```

### Options Template
```csharp
// Copyright © Clarivex Technologies. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace Pervaxis.Genesis.<Provider>.Options;

/// <summary>
/// Configuration options for <Provider>.
/// </summary>
public sealed class <Provider>Options
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Pervaxis:Genesis:<Provider>";

    /// <summary>
    /// AWS region.
    /// </summary>
    [Required]
    public string Region { get; set; } = string.Empty;
}
```

### Extension Template
```csharp
// Copyright © Clarivex Technologies. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Pervaxis.Genesis.<Provider>.Extensions;

/// <summary>
/// DI registration for <Provider>.
/// </summary>
public static class <Provider>ServiceCollectionExtensions
{
    /// <summary>
    /// Registers I<Provider> backed by AWS <Service>.
    /// </summary>
    public static IServiceCollection AddPervaxis<Provider>(
        this IServiceCollection services,
        Action<<Provider>Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<<Provider>Options>()
            .BindConfiguration(<Provider>Options.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddSingleton<I<Provider>, <AwsService><Provider>>();

        return services;
    }
}
```

---

## ✅ Standards Checklist

Before committing code:

- [ ] File-scoped namespaces (`namespace Pervaxis.Genesis.Caching;`)
- [ ] Sealed classes (unless designed for inheritance)
- [ ] `ConfigureAwait(false)` on all awaits
- [ ] `ArgumentNullException.ThrowIfNull()` for null checks
- [ ] `DateTime.UtcNow` (never `DateTime.Now`)
- [ ] Copyright header on every .cs file
- [ ] XML comments on all public APIs
- [ ] No TODOs without GitHub issue
- [ ] Tests written (90% coverage target)
- [ ] CHANGELOG.md updated

---

## 🧪 Testing Commands

```bash
# Run all tests
dotnet test Pervaxis.Genesis.slnx

# Run specific project tests
dotnet test tests/Pervaxis.Genesis.Caching.Tests

# Run with coverage
dotnet test Pervaxis.Genesis.slnx --collect:"XPlat Code Coverage"

# Generate coverage report
reportgenerator -reports:"./coverage/**/coverage.cobertura.xml" -targetdir:"./coverage/report"
```

---

## 📦 Package Management

```bash
# Check for outdated packages
dotnet list package --outdated

# Check for vulnerabilities
dotnet list package --vulnerable --include-transitive

# Update all packages in project
dotnet list package --outdated | Select-String ">" | ForEach-Object {
    $package = ($_ -split " ")[0]
    dotnet add package $package
}
```

---

## 🐛 Troubleshooting

### Build Fails with XML Documentation Errors
- Make sure all public types have XML comments
- Test projects should have `<GenerateDocumentationFile>false</GenerateDocumentationFile>`

### Package Not Found
- Check `nuget.config` is in solution root
- Run `dotnet restore Pervaxis.Genesis.slnx`

### LocalStack Connection Issues
- Ensure Docker Desktop is running
- Start LocalStack: `docker run --rm -p 4566:4566 localstack/localstack`
- Set endpoint URL in tests: `http://localhost:4566`

---

## 📞 Getting Help

- **Documentation:** `docs/` folder
- **Standards:** `docs/PERVAXIS_STANDARDS.md`
- **Task List:** `TASKS.md`
- **Setup Details:** `docs/SOLUTION_SETUP.md`
- **Pervaxis Core:** `C:\Anand\Clarivex\Pervaxis\Code\Core\docs\`

---

## 🎯 Today's Goal

Pick one provider and complete it end-to-end:
1. Folder structure
2. Dependencies
3. Implementation
4. Tests (90% coverage)
5. Documentation
6. Update CHANGELOG.md
7. Check off task in TASKS.md

---

*Start with `TASKS.md` → Pick a task → Follow templates → Test → Document → Repeat!*

*Pervaxis Platform · Clarivex Technologies · Genesis Edition*
