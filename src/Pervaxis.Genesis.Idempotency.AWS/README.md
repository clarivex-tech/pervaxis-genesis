# Pervaxis.Genesis.Idempotency.AWS

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

builder.Services.AddGenesisIdempotencyAws(
    builder.Configuration.GetSection("Idempotency:Aws"));
```

## Key Features

- DynamoDB-backed idempotency record storage with TTL
- In-memory fallback store for local development and testing
- Conditional writes for safe concurrent request handling
- Automatic table creation in development environments

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
