# Pervaxis.Genesis.Search.AWS

AWS OpenSearch search provider for the Pervaxis Genesis platform — full-text search, document indexing, and query capabilities with native AWS integration.

## Overview

`Pervaxis.Genesis.Search.AWS` implements the `ISearch` abstraction from `Pervaxis.Core.Abstractions` using OpenSearch.Client for AWS OpenSearch. It provides:

- **Document indexing** with automatic ID and type management
- **Full-text search** with query string syntax
- **Bulk indexing** for efficient batch operations
- **Document deletion** with soft-delete support
- **Index prefix support** for multi-tenancy
- **Basic authentication** for self-managed OpenSearch clusters
- **Configurable timeouts and retries**

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.Search.AWS" Version="1.0.0" />
```

## Configuration

### appsettings.json

```json
{
  "Search": {
    "Region": "us-east-1",
    "DomainEndpoint": "https://my-domain.us-east-1.es.amazonaws.com",
    "IndexPrefix": "prod-",
    "DefaultPageSize": 10,
    "RequestTimeoutSeconds": 30,
    "MaxRetries": 3,
    "EnableDebugMode": false
  }
}
```

### Option properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Region` | `string` | **(required)** | AWS region (e.g., "us-east-1", "ap-south-1") |
| `DomainEndpoint` | `string` | **(required)** | OpenSearch domain endpoint URL |
| `IndexPrefix` | `string` | `""` | Optional prefix for all indices (e.g., "prod-", "tenant-123-") |
| `DefaultPageSize` | `int` | `10` | Default number of search results to return |
| `RequestTimeoutSeconds` | `int` | `30` | Request timeout in seconds |
| `MaxRetries` | `int` | `3` | Maximum number of retries for failed requests |
| `EnableDebugMode` | `bool` | `false` | Enable detailed logging |
| `Username` | `string?` | `null` | Username for basic authentication (optional) |
| `Password` | `string?` | `null` | Password for basic authentication (optional) |

### Basic Authentication (Self-Managed OpenSearch)

For self-managed OpenSearch clusters with basic authentication:

```json
{
  "Search": {
    "Region": "us-east-1",
    "DomainEndpoint": "https://my-opensearch-cluster.example.com:9200",
    "Username": "admin",
    "Password": "strong-password"
  }
}
```

## Registration

### Using IConfiguration

```csharp
using Pervaxis.Genesis.Search.AWS.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register OpenSearch
builder.Services.AddGenesisSearch(
    builder.Configuration.GetSection("Search"));
```

### Using Action Delegate

```csharp
using Pervaxis.Genesis.Search.AWS.Extensions;
using Pervaxis.Genesis.Search.AWS.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGenesisSearch(options =>
{
    options.Region = "us-east-1";
    options.DomainEndpoint = "https://my-domain.us-east-1.es.amazonaws.com";
    options.IndexPrefix = "prod-";
    options.DefaultPageSize = 20;
});
```

## Usage

### Indexing Documents

```csharp
using Pervaxis.Core.Abstractions.Genesis.Modules;

public class ProductService
{
    private readonly ISearch _search;

    public ProductService(ISearch search)
    {
        _search = search;
    }

    public async Task IndexProductAsync(Product product)
    {
        var indexed = await _search.IndexAsync(
            index: "products",
            id: product.Id.ToString(),
            document: product);

        Console.WriteLine($"Product indexed: {indexed}");
    }
}
```

### Searching Documents

```csharp
public async Task<IEnumerable<Product>> SearchProductsAsync(string query)
{
    var results = await _search.SearchAsync<Product>(
        index: "products",
        query: query);

    return results;
}
```

### Query String Syntax

OpenSearch supports rich query string syntax:

```csharp
// Simple term search
var results = await _search.SearchAsync<Product>("products", "laptop");

// Field-specific search
var results = await _search.SearchAsync<Product>("products", "name:laptop");

// Boolean operators
var results = await _search.SearchAsync<Product>("products", "laptop AND available:true");

// Wildcard search
var results = await _search.SearchAsync<Product>("products", "lap*");

// Range queries
var results = await _search.SearchAsync<Product>("products", "price:[100 TO 500]");
```

### Bulk Indexing

```csharp
public async Task BulkIndexProductsAsync(List<Product> products)
{
    var documents = products.ToDictionary(
        p => p.Id.ToString(),
        p => p);

    var successCount = await _search.BulkIndexAsync(
        index: "products",
        documents: documents);

    Console.WriteLine($"{successCount}/{products.Count} products indexed");
}
```

### Deleting Documents

```csharp
public async Task DeleteProductAsync(string productId)
{
    var deleted = await _search.DeleteAsync(
        index: "products",
        id: productId);

    Console.WriteLine($"Product deleted: {deleted}");
}
```

## Index Prefix & Multi-Tenancy

Use index prefixes to isolate data by tenant or environment:

```csharp
// Configuration
options.IndexPrefix = "tenant-123-";

// Indexes to "tenant-123-products"
await _search.IndexAsync("products", "1", product);

// Searches "tenant-123-products"
await _search.SearchAsync<Product>("products", "laptop");
```

## IAM Permissions

Your application needs these IAM permissions for AWS OpenSearch:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "es:ESHttpGet",
        "es:ESHttpHead",
        "es:ESHttpPost",
        "es:ESHttpPut",
        "es:ESHttpDelete"
      ],
      "Resource": "arn:aws:es:region:account-id:domain/domain-name/*"
    }
  ]
}
```

## Error Handling

All methods throw `GenesisException` on failure:

```csharp
using Pervaxis.Genesis.Base.Exceptions;

try
{
    await _search.IndexAsync("products", "1", product);
}
catch (GenesisException ex)
{
    _logger.LogError(ex, "Failed to index product: {Message}", ex.Message);
}
```

## Best Practices

### Index Naming
- Use lowercase index names
- Use hyphens for word separation (e.g., "product-catalog")
- Apply consistent prefixes for multi-tenancy

### Performance
- Use bulk operations for batch indexing (>10 documents)
- Configure appropriate `DefaultPageSize` based on your use case
- Enable `EnableDebugMode` only for development
- Tune `RequestTimeoutSeconds` based on your query complexity

### Security
- Never log credentials
- Use AWS IAM roles instead of basic authentication when possible
- Restrict IAM permissions to specific indices
- Enable VPC access for production OpenSearch domains

## Troubleshooting

### Connection Timeout

Increase `RequestTimeoutSeconds`:
```json
{
  "Search": {
    "RequestTimeoutSeconds": 60
  }
}
```

### Too Many Requests (429)

Increase `MaxRetries` or implement exponential backoff:
```json
{
  "Search": {
    "MaxRetries": 5
  }
}
```

### Debug Mode

Enable debug logging to see detailed request/response information:
```json
{
  "Search": {
    "EnableDebugMode": true
  }
}
```

## Limitations

- AWS managed OpenSearch only supports AWS Signature Version 4 authentication
- Self-managed clusters require basic authentication (Username/Password)
- Query string syntax is documented at [OpenSearch Query String Syntax](https://opensearch.org/docs/latest/query-dsl/full-text/query-string/)
- Maximum document size is 100MB by default (OpenSearch limit)

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
