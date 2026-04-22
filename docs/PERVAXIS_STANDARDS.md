# Pervaxis Genesis — Development Standards

> Extracted from Pervaxis.Core standards to ensure consistency across the platform

---

## Code Standards Checklist

Before committing code:

- [ ] **File-scoped namespaces** — `namespace Pervaxis.Genesis.Caching;` (not block syntax)
- [ ] **Sealed classes** — Mark concrete classes `sealed` unless designed for inheritance
- [ ] **ConfigureAwait(false)** — On every `await` in library code
- [ ] **Null guards** — `ArgumentNullException.ThrowIfNull(parameter)` for all public method parameters
- [ ] **String guards** — `ArgumentException.ThrowIfNullOrWhiteSpace(stringParam)` for string parameters
- [ ] **UTC time** — Always `DateTime.UtcNow`, never `DateTime.Now`
- [ ] **Copyright header** — Every `.cs` file must have Clarivex copyright header
- [ ] **No TODOs** — Without a linked GitHub Issue
- [ ] **No commented-out code** — Delete it; git history preserves it
- [ ] **Tests written** — For all new logic (minimum 90% coverage on domain logic)

---

## Copyright Header

Every `.cs` file must start with:

```csharp
// Copyright © Clarivex Technologies. All rights reserved.
```

Use License Header Manager extension in Visual Studio/Rider to enforce this automatically.

---

## Project Structure Pattern

Each Genesis provider project follows this structure:

```
src/Pervaxis.Genesis.<Provider>/
├── Abstractions/          # Interfaces (ICache, ISearchClient, etc.)
├── Options/               # Configuration classes (<Provider>Options.cs)
├── Extensions/            # DI registration (<Provider>ServiceCollectionExtensions.cs)
├── Providers/             # AWS implementation classes
│   └── <AwsService>/      # E.g., ElastiCache/, OpenSearch/, S3/
├── README.md              # Installation and usage guide
└── Pervaxis.Genesis.<Provider>.csproj
```

---

## Extension Method Pattern

Every provider must expose an `AddPervaxis<Provider>()` extension method:

```csharp
// File: Extensions/<Provider>ServiceCollectionExtensions.cs
namespace Pervaxis.Genesis.<Provider>.Extensions;

/// <summary>
/// DI registration for Pervaxis <Provider>.
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

        // Register provider implementation
        services.AddSingleton<I<Provider>, <Provider>Provider>();

        return services;
    }
}
```

---

## Configuration Options Pattern

```csharp
namespace Pervaxis.Genesis.<Provider>.Options;

/// <summary>
/// Configuration options for <Provider>.
/// </summary>
public sealed class <Provider>Options
{
    /// <summary>
    /// Configuration section name: "Pervaxis:Genesis:<Provider>"
    /// </summary>
    public const string SectionName = "Pervaxis:Genesis:<Provider>";

    /// <summary>
    /// AWS region (e.g., "ap-south-1")
    /// </summary>
    [Required]
    public string Region { get; set; } = string.Empty;

    // Additional provider-specific options...
}
```

---

## Conventional Commits

All commits **must** follow [Conventional Commits](https://www.conventionalcommits.org):

```
feat(caching): add ElastiCache Redis support
fix(search): correct OpenSearch query builder null handling
docs(workflow): update Step Functions usage examples
test(messaging): add coverage for SQS batch publishing
chore(deps): bump AWSSDK.S3 to 3.7.400.18
breaking(abstractions): rename ICache.GetAsync signature
```

Format: `<type>(<scope>): <description>`

**Types:**
- `feat` — New feature (minor version bump)
- `fix` — Bug fix (patch version bump)
- `breaking` — Breaking change (major version bump)
- `docs` — Documentation only
- `test` — Adding/updating tests
- `chore` — Maintenance (deps, config)
- `refactor` — Code refactoring (no behavior change)
- `perf` — Performance improvement

---

## Branching Strategy

```
main          ← protected; PRs only; requires 1 reviewer + CI green
  └── develop ← integration branch
        └── feature/<ticket-id>-short-description
        └── fix/<ticket-id>-short-description
```

Example: `feature/GEN-42-add-elasticache-support`

---

## .NET 10 Specific Requirements

### Target Framework
```xml
<TargetFramework>net10.0</TargetFramework>
<LangVersion>preview</LangVersion>
```

### Nullable Reference Types
```xml
<Nullable>enable</Nullable>
```

All reference types must be properly annotated with `?` for nullable.

### Implicit Usings
```xml
<ImplicitUsings>enable</ImplicitUsings>
```

Common namespaces are auto-imported. Check `obj/Debug/net10.0/<Project>.GlobalUsings.g.cs` to see what's included.

### Warnings as Errors
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<AnalysisMode>All</AnalysisMode>
```

Zero warnings policy — fix all warnings before committing.

---

## AWS SDK Usage

### Service Client Registration

Use AWS SDK's built-in DI extensions:

```csharp
// Startup.cs or Program.cs
services.AddAWSService<IAmazonS3>();
services.AddAWSService<IAmazonSQS>();
services.AddAWSService<IAmazonOpenSearchService>();
```

### Async Best Practices

```csharp
// ✅ Correct
await client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);

// ❌ Wrong (library code should not capture context)
await client.GetObjectAsync(request, cancellationToken);
```

### Cancellation Token

Always accept and propagate `CancellationToken`:

```csharp
public async Task<CacheValue> GetAsync(string key, CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    
    var response = await _redis.StringGetAsync(key).ConfigureAwait(false);
    
    // Check cancellation between operations
    cancellationToken.ThrowIfCancellationRequested();
    
    return response;
}
```

---

## Documentation Requirements

### XML Documentation

All public APIs must have XML docs:

```csharp
/// <summary>
/// Retrieves a value from the cache by key.
/// </summary>
/// <param name="key">The cache key. Must not be null or whitespace.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>The cached value, or null if not found.</returns>
/// <exception cref="ArgumentException">Thrown when key is null or whitespace.</exception>
public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
```

### README.md

Every project must have a README.md with:
1. One-line description
2. Installation instructions
3. Configuration example (code + appsettings.json)
4. Usage example
5. AWS IAM permissions required

---

## Testing Requirements

### Coverage Target
- **90% minimum** for domain logic
- Exception handling paths must be tested
- Async paths must be tested with cancellation tokens

### Test Naming
```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
[Fact]
public async Task GetAsync_WhenKeyExists_ReturnsValue() { }

[Fact]
public async Task GetAsync_WhenKeyNotFound_ReturnsNull() { }

[Fact]
public async Task GetAsync_WhenKeyIsNull_ThrowsArgumentException() { }
```

---

## Performance Considerations

### Avoid Allocations in Hot Paths
```csharp
// ✅ Use ArrayPool for temporary buffers
var buffer = ArrayPool<byte>.Shared.Rent(size);
try
{
    // Use buffer
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

### Use ValueTask for Hot Paths
```csharp
// For frequently called methods that often complete synchronously
public ValueTask<CacheValue> GetAsync(string key, CancellationToken ct = default)
```

---

## Security Guidelines

### No Secrets in Code
- Never hardcode credentials, API keys, or connection strings
- Use AWS Secrets Manager or Parameter Store
- Environment-specific values go in `appsettings.{Environment}.json`

### Input Validation
- Validate all public method inputs
- Use `[Required]`, `[Range]`, etc. on Options classes
- Sanitize user input before passing to AWS APIs

### Least Privilege IAM
- Document minimum required IAM permissions in README
- Never use wildcard (`*`) permissions in examples

---

*Pervaxis Platform · Clarivex Technologies · https://clarivex.tech*
