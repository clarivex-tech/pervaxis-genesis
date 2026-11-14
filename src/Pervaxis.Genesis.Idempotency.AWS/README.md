# Pervaxis.Genesis.Idempotency.AWS

## Shared resource tenancy

The public `IIdempotencyStore` is Scoped and validates every operation against the
trusted `ITenantContext`. Explicit tenant arguments and completed record keys must
match that context. Missing, invalid or foreign tenants are rejected before storage.
The shared backend and AWS client remain Singletons; records use tenant-prefixed
keys so two tenants can use the same idempotency key independently.

The legacy `EnableTenantIsolation` setting cannot disable these checks. The HTTP
filter returns 403 when the tenant context is unavailable or invalid. Resolve the
store in a tenant scope for background work as well.

AWS DynamoDB idempotency store for the Pervaxis Genesis platform.

## Overview

`Pervaxis.Genesis.Idempotency.AWS` provides the AWS-specific `IIdempotencyStore` implementation backed by DynamoDB. It stores idempotency records with automatic TTL expiration and includes an in-memory fallback store for local development.

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.Idempotency.AWS" Version="1.0.0" />
```

> **GitHub Packages feed** — add the following to your `nuget.config`:
> ```xml
> <add key="github" value="https://nuget.pkg.github.com/clarivex-tech/index.json" />
> ```

## Registration

```csharp
builder.Services.AddGenesisIdempotency(
    builder.Configuration.GetSection("Idempotency"));
```

## Key Features

- DynamoDB-backed idempotency record storage with TTL
- In-memory fallback store for local development and testing
- Conditional writes for safe concurrent request handling
- Automatic table creation in development environments

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
