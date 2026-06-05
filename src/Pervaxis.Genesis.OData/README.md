# Pervaxis.Genesis.OData

OData query support for REST APIs on the Pervaxis Genesis platform.

## Overview

`Pervaxis.Genesis.OData` provides lightweight OData query capabilities ($filter, $orderby, $top, $skip, $select, $count) for ASP.NET Core APIs. It includes query validation, complexity analysis, and entity-level configuration to prevent abuse while enabling flexible client-side querying.

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.OData" Version="1.0.0" />
```

> **GitHub Packages feed** — add the following to your `nuget.config`:
> ```xml
> <add key="github" value="https://nuget.pkg.github.com/clarivex-tech/index.json" />
> ```

## Registration

```csharp
builder.Services.AddGenesisOData(
    builder.Configuration.GetSection("OData"));
```

## Key Features

- `[ODataQueryable]` attribute for opt-in per-endpoint query support
- Entity-level configuration for allowed properties and max page sizes
- Query complexity analysis to prevent expensive queries
- Built-in observability with structured logging, metrics, and tracing
- Paginated results with `PageResult<T>` response wrapper

## Usage

```csharp
[HttpGet]
[ODataQueryable]
public async Task<IActionResult> GetProducts(ODataQueryOptions query)
{
    var results = await _productService.QueryAsync(query);
    return Ok(results);
}
```

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
