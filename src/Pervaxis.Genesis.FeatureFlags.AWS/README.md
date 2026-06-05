# Pervaxis.Genesis.FeatureFlags.AWS

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
