# Pervaxis.Genesis.TransactionalLogging.AWS

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
