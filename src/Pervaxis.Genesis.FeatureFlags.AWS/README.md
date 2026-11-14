# Pervaxis.Genesis.FeatureFlags.AWS

## Shared resource tenancy

Feature definitions remain shared in AppConfig/SSM configuration. Feature managers,
snapshots and tenant filters run in a DI scope using the trusted `ITenantContext`;
evaluation requires a resolved, valid tenant. The `Tenant` filter matches
`AllowedTenants` with exact tenant IDs. A flag intended for everyone can still be
enabled globally for all resolved tenants.

Resolve managers and snapshots within each tenant scope, including background jobs.
`EnableTenantIsolation` controls metric tagging only and cannot bypass evaluation
checks. Shared configuration is for application flag definitions, not tenant secrets.

AWS AppConfig feature flag provider for the Pervaxis Genesis platform.

## Overview

`Pervaxis.Genesis.FeatureFlags.AWS` implements feature flag management using [Microsoft.FeatureManagement](https://github.com/microsoft/FeatureManagement-Dotnet) backed by AWS Systems Manager AppConfig. It supports tenant-scoped filters, observability interceptors, and state tracking.

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.FeatureFlags.AWS" Version="1.0.0" />
```

> **GitHub Packages feed** — add the following to your `nuget.config`:
> ```xml
> <add key="github" value="https://nuget.pkg.github.com/clarivex-tech/index.json" />
> ```

## Registration

```csharp
builder.Services.AddGenesisFeatureFlags(
    builder.Configuration.GetSection("FeatureFlags"));
```

## Key Features

- AWS AppConfig integration for centralized flag management
- Tenant-scoped feature filters for multi-tenant workloads
- Built-in observability with metrics and tracing
- Feature flag state tracking and change detection

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
