# Pervaxis.Genesis.Base

**Core abstractions and foundational types for the Pervaxis Genesis AWS provider library collection.**

[![NuGet](https://img.shields.io/nuget/v/Pervaxis.Genesis.Base.svg)](https://www.nuget.org/packages/Pervaxis.Genesis.Base/)
[![License](https://img.shields.io/badge/license-Proprietary-blue.svg)](../../LICENSE)

---

## Overview

`Pervaxis.Genesis.Base` provides the foundational abstractions, result types, configuration options, and utilities shared across all Genesis AWS provider implementations. This package is a required dependency for all other Genesis provider libraries.

**Key Features:**
- 🎯 **Result Pattern** - Consistent success/failure handling with `ProviderResult<T>`
- ⚙️ **Base Configuration** - Common options for AWS region, retry policies, timeouts, LocalStack support
- 📄 **Template Loading** - JSON and YAML template configuration loader
- 🔧 **DI Extensions** - Dependency injection registration helpers
- 🚨 **Exception Hierarchy** - Structured exception types for Genesis providers

---

## Installation

Install via the .NET CLI:

```bash
dotnet add package Pervaxis.Genesis.Base
```

Or via Package Manager Console:

```powershell
Install-Package Pervaxis.Genesis.Base
```

Or add directly to your `.csproj`:

```xml
<PackageReference Include="Pervaxis.Genesis.Base" Version="1.0.0" />
```

---

## Quick Start

### 1. Register Base Services

```csharp
using Pervaxis.Genesis.Base.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register Genesis base services
builder.Services.AddGenesisBase();

var app = builder.Build();
```

### 2. Use Provider Results

```csharp
using Pervaxis.Genesis.Base.Results;

public class MyService
{
    public async Task<ProviderResult<string>> GetDataAsync(string id)
    {
        try
        {
            var data = await FetchDataAsync(id);
            return ProviderResult<string>.Success(data);
        }
        catch (Exception ex)
        {
            return ProviderResult<string>.Failure("Failed to fetch data", ex);
        }
    }
}

// Usage
var result = await myService.GetDataAsync("123");

if (result.IsSuccess)
{
    Console.WriteLine($"Data: {result.Data}");
}
else
{
    Console.WriteLine($"Error: {result.Error}");
}
```

### 3. Load Templates

```csharp
using Pervaxis.Genesis.Base.Abstractions;

public class CloudFormationService
{
    private readonly ITemplateConfigurationLoader _loader;

    public CloudFormationService(ITemplateConfigurationLoader loader)
    {
        _loader = loader;
    }

    public async Task<IDictionary<string, object>> LoadTemplateAsync(string path)
    {
        // Supports .json and .yaml/.yml files
        var template = await _loader.LoadFromFileAsync(path);
        
        if (_loader.ValidateTemplate(template))
        {
            return template;
        }
        
        throw new InvalidOperationException("Invalid template");
    }
}
```

---

## Configuration

### Base Options Pattern

All Genesis providers inherit from `GenesisOptionsBase`, providing consistent configuration:

```csharp
using Pervaxis.Genesis.Base.Options;

public class MyProviderOptions : GenesisOptionsBase
{
    public string CustomSetting { get; set; } = "default";
}

// appsettings.json
{
  "MyProvider": {
    "Region": "us-west-2",
    "EnableDetailedLogging": true,
    "TimeoutSeconds": 60,
    "MaxRetryAttempts": 5,
    "UseLocalStack": false
  }
}

// Program.cs
builder.Services.Configure<MyProviderOptions>(
    builder.Configuration.GetSection("MyProvider"));
```

### LocalStack Support

For local development with LocalStack:

```json
{
  "MyProvider": {
    "Region": "us-east-1",
    "UseLocalStack": true,
    "LocalStackUrl": "http://localhost:4566"
  }
}
```

---

## API Reference

### ProviderResult\<T\>

Represents the result of a provider operation with success/failure status and optional data.

**Static Methods:**
- `Success(T data)` - Creates a successful result
- `Failure(string error)` - Creates a failed result with error message
- `Failure(string error, Exception exception)` - Creates a failed result with exception

**Properties:**
- `bool IsSuccess` - True if operation succeeded
- `bool IsFailure` - True if operation failed
- `T? Data` - Result data (only on success)
- `string? Error` - Error message (only on failure)
- `Exception? Exception` - Exception (only on failure)

**Example:**
```csharp
var result = ProviderResult<User>.Success(user);
// or
var result = ProviderResult<User>.Failure("User not found");
```

### ProviderResult

Non-generic version for operations without return data.

**Static Methods:**
- `Success()` - Creates a successful result
- `Failure(string error)` - Creates a failed result
- `Failure(string error, Exception exception)` - Creates a failed result with exception

**Example:**
```csharp
var result = ProviderResult.Success();
// or
var result = ProviderResult.Failure("Operation failed");
```

---

### GenesisOptionsBase

Base configuration options for all Genesis providers.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Region` | `string` | `"us-east-1"` | AWS region for the provider |
| `EnableDetailedLogging` | `bool` | `false` | Enable detailed logging |
| `TimeoutSeconds` | `int` | `30` | Timeout for provider operations |
| `MaxRetryAttempts` | `int` | `3` | Maximum retry attempts for failed operations |
| `UseLocalStack` | `bool` | `false` | Use LocalStack for local development |
| `LocalStackUrl` | `string?` | `null` | LocalStack service URL (e.g., http://localhost:4566) |

**Methods:**
- `bool Validate()` - Validates the options configuration

**Example:**
```csharp
public class CachingOptions : GenesisOptionsBase
{
    public string ConnectionString { get; set; } = string.Empty;
    
    public override bool Validate()
    {
        if (!base.Validate())
        {
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return false;
        }
        
        return true;
    }
}
```

---

### ITemplateConfigurationLoader

Defines methods for loading and parsing template configuration files (JSON/YAML).

**Methods:**

```csharp
Task<IDictionary<string, object>> LoadFromFileAsync(
    string filePath, 
    CancellationToken cancellationToken = default);

Task<IDictionary<string, object>> LoadFromStringAsync(
    string content, 
    string format, 
    CancellationToken cancellationToken = default);

Task<IDictionary<string, object>> LoadFromResourceAsync(
    string resourceName, 
    CancellationToken cancellationToken = default);

bool ValidateTemplate(IDictionary<string, object> template);
```

**Example:**
```csharp
// From file
var template = await loader.LoadFromFileAsync("template.yaml");

// From string
var jsonContent = File.ReadAllText("template.json");
var template = await loader.LoadFromStringAsync(jsonContent, "json");

// From embedded resource
var template = await loader.LoadFromResourceAsync("MyApp.Templates.stack.yaml");
```

**Supported Formats:**
- `json` - JSON format
- `yaml` / `yml` - YAML format

---

### Exceptions

#### GenesisException

Base exception for all Genesis provider errors.

```csharp
public class GenesisException : Exception
{
    public string? ProviderName { get; }
    
    public GenesisException(string message);
    public GenesisException(string message, Exception innerException);
    public GenesisException(string providerName, string message);
    public GenesisException(string providerName, string message, Exception innerException);
}
```

#### GenesisConfigurationException

Exception thrown when Genesis provider configuration is invalid.

```csharp
public sealed class GenesisConfigurationException : GenesisException
{
    public GenesisConfigurationException(string message);
    public GenesisConfigurationException(string message, Exception innerException);
    public GenesisConfigurationException(string providerName, string message);
}
```

**Example:**
```csharp
try
{
    await provider.InitializeAsync();
}
catch (GenesisConfigurationException ex)
{
    _logger.LogError(ex, "Configuration error in provider: {Provider}", ex.ProviderName);
}
catch (GenesisException ex)
{
    _logger.LogError(ex, "Provider error: {Provider}", ex.ProviderName);
}
```

---

## Extension Methods

### GenesisServiceCollectionExtensions

```csharp
public static IServiceCollection AddGenesisBase(this IServiceCollection services)
```

Registers the following services:
- `ITemplateConfigurationLoader` → `TemplateConfigurationLoader` (Singleton)

**Example:**
```csharp
services.AddGenesisBase();
```

---

## Advanced Usage

### Custom Provider Implementation

```csharp
using Pervasis.Genesis.Base.Options;
using Pervasis.Genesis.Base.Results;
using Pervasis.Genesis.Base.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

public class MyProviderOptions : GenesisOptionsBase
{
    public string ApiKey { get; set; } = string.Empty;
}

public interface IMyProvider
{
    Task<ProviderResult<string>> ProcessAsync(string input, CancellationToken ct);
}

public class MyProvider : IMyProvider
{
    private readonly MyProviderOptions _options;
    private readonly ILogger<MyProvider> _logger;

    public MyProvider(
        IOptions<MyProviderOptions> options,
        ILogger<MyProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!_options.Validate())
        {
            throw new GenesisConfigurationException(
                nameof(MyProvider), 
                "Invalid configuration");
        }
    }

    public async Task<ProviderResult<string>> ProcessAsync(
        string input, 
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Processing input: {Input}", input);
            
            // Your implementation here
            var result = await DoWorkAsync(input, ct);
            
            return ProviderResult<string>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process input");
            return ProviderResult<string>.Failure("Processing failed", ex);
        }
    }
    
    private Task<string> DoWorkAsync(string input, CancellationToken ct)
    {
        // Implementation
        return Task.FromResult($"Processed: {input}");
    }
}

// Registration
services.Configure<MyProviderOptions>(configuration.GetSection("MyProvider"));
services.AddSingleton<IMyProvider, MyProvider>();
```

---

## Dependencies

- **Microsoft.Extensions.DependencyInjection.Abstractions** (9.0.0)
- **Microsoft.Extensions.Logging.Abstractions** (9.0.0)
- **Microsoft.Extensions.Options** (9.0.0)
- **YamlDotNet** (16.2.1)

---

## Related Packages

The Genesis provider ecosystem includes:

- **Pervaxis.Genesis.Caching** - ElastiCache Redis caching
- **Pervaxis.Genesis.Messaging** - SQS + SNS messaging
- **Pervaxis.Genesis.FileStorage** - S3 file storage
- **Pervaxis.Genesis.Search** - OpenSearch
- **Pervaxis.Genesis.Notifications** - SES email + SNS push notifications
- **Pervaxis.Genesis.Workflow** - Step Functions workflow orchestration
- **Pervaxis.Genesis.AIAssistance** - Bedrock AI models
- **Pervaxis.Genesis.Reporting** - Metabase reporting
- **Pervaxis.Genesis.CloudFormation** - CloudFormation stack management

---

## Support

For issues, questions, or contributions:
- 📧 Email: support@clarivex.tech
- 🌐 Website: https://clarivex.tech/pervaxis
- 📚 Documentation: https://docs.clarivex.tech/genesis

---

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.

This software is proprietary and confidential. Unauthorized copying, distribution, or use is strictly prohibited.
