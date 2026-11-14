# Pervaxis.Genesis.Search.AWS

AWS OpenSearch search provider for the Pervaxis Genesis platform — full-text search, document indexing, and query capabilities with native AWS integration.

## Overview

`Pervaxis.Genesis.Search.AWS` implements the `ISearch` abstraction from `Pervaxis.Core.Abstractions` using OpenSearch.Client for AWS OpenSearch. It provides:

- **Document indexing** with caller-supplied logical IDs and provider tenant stamps
- **Full-text search** with query string syntax
- **Bulk indexing** for efficient batch operations
- **Document deletion** by tenant-scoped document ID
- **Shared indexes** with mandatory tenant filters and tenant-scoped document IDs
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
| `IndexPrefix` | `string` | `""` | Shared environment prefix for all indices (e.g., "prod-") |
| `EnableTenantIsolation` | `bool` | `true` | Legacy option; isolation is mandatory regardless of its value |
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

All tenants share indexes. Use prefixes for environments:

```csharp
// Configuration
options.IndexPrefix = "prod-";

// Indexes to "prod-products", with tenant stamp and tenant-scoped document ID
await _search.IndexAsync("products", "1", product);

// Searches "prod-products" with a mandatory tenant filter
await _search.SearchAsync<Product>("products", "laptop");
```

Resolve `ISearch` in a DI scope with a resolved `ITenantContext`. The provider is
Scoped; its keyed OpenSearch client and connection settings are Singleton and
owned by the container. Background workers must create a scope and resolve the
tenant before calling the provider. Every operation requires a valid tenant,
including empty bulk calls. The legacy `EnableTenantIsolation` flag cannot disable
isolation. Changing or clearing the tenant during a provider scope is rejected.

Index and bulk writes add the reserved top-level `tenantId` field without changing
business field names. Caller documents containing this field, case variants or
dotted variants are rejected. Logical ID `42` becomes physical ID `acme:42`;
delete uses the same transformation. Caller IDs are always logical IDs, even if
they look like another tenant's physical ID. Searches wrap the caller's query in
a Boolean query with a mandatory exact term filter on `tenantId.keyword`.
Only concrete lowercase index names are accepted; wildcards, lists and paths
are rejected.

### Required mapping

Provision shared indexes or infrastructure-managed templates with this mapping:

```json
{
  "mappings": {
    "properties": {
      "tenantId": {
        "type": "text",
        "fields": {
          "keyword": { "type": "keyword", "ignore_above": 256 }
        }
      }
    }
  }
}
```

The keyword subfield must preserve the exact tenant ID with no normalizer.
Default dynamic string mappings commonly provide this shape, but explicitly
provision it rather than depending on cluster defaults. Missing this subfield
returns no tenant documents. Never map it as analyzed text.

### Compatibility and deployment

This changes index names, physical IDs and DI lifetimes. Existing tenant-prefixed
indexes are not read or migrated automatically. Populate the shared indexes from
an authoritative source with verified ownership, tenant stamps and new IDs before
switching. Migration tooling and reindex APIs are outside this change.

Isolation is enforced by this provider. Direct OpenSearch access, external writes,
or ingest pipelines that change tenant stamps can bypass its conventions. The
platform must control shared-index mappings and writer credentials.

This change only covers index, search, delete and bulk index. Ingest pipeline
management, reindexing and other new search capabilities remain outside scope.

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
