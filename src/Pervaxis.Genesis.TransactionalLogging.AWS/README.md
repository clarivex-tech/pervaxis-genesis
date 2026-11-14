# Pervaxis.Genesis.TransactionalLogging.AWS

## Shared resource tenancy

Stores and the transaction service use the trusted scoped `ITenantContext`.
DynamoDB keys and S3 overflow paths always contain that tenant; explicit tenant
arguments and transaction contexts must match it. The legacy
`EnableTenantIsolation` setting cannot select a global partition. AWS clients remain
shared Singletons owned by the container; storage services are Scoped.

HTTP requests without a resolved tenant continue without writing a shared/global
log. Register tenant resolution before transaction logging. Persistence is awaited
within the request scope; AWS persistence failures retain the existing fail-open
behavior. Explicit transaction operations reject missing or conflicting tenants.

Existing query requests are tenant constrained, including transaction-ID lookups.
DynamoDB result mapping and the public transaction service read methods remain
unimplemented placeholders; this change does not complete the reporting/read API.

AWS-backed transactional logging for the Pervaxis Genesis platform.

## Overview

`Pervaxis.Genesis.TransactionalLogging.AWS` captures structured transaction logs for all cross-cutting operations (caching, messaging, file storage, search, workflow, notifications, reporting, AI assistance). It uses DynamoDB as the hot store with S3 overflow for large payloads, and provides middleware-driven automatic logging with parameter sanitization.

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.TransactionalLogging.AWS" Version="1.0.0" />
```

> **GitHub Packages feed** — add the following to your `nuget.config`:
> ```xml
> <add key="github" value="https://nuget.pkg.github.com/clarivex-tech/index.json" />
> ```

## Registration

```csharp
builder.Services.AddGenesisTransactionalLogging(
    builder.Configuration.GetSection("TransactionalLogging"));
```

## Key Features

- Automatic transaction context tracking via middleware
- Interceptors for all Genesis provider operations (cache, messaging, file storage, etc.)
- DynamoDB hot store with TTL-based expiration
- S3 overflow for payloads exceeding DynamoDB item limits
- Sensitive parameter sanitization (PII, credentials, tokens)
- `[SuppressTransactionLog]` attribute to opt out specific endpoints
- Built-in observability with structured logging, metrics, and tracing

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
