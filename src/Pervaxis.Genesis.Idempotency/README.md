# Pervaxis.Genesis.Idempotency

Cloud-agnostic REST API idempotency framework for the Pervaxis Genesis platform.

## Overview

`Pervaxis.Genesis.Idempotency` provides middleware, action filters, and abstractions to ensure safe retries of non-idempotent HTTP operations (POST, PATCH, DELETE). It uses an `Idempotency-Key` header to detect duplicate requests and return cached responses.

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.Idempotency" Version="1.0.0" />
```

> **GitHub Packages feed** — add the following to your `nuget.config`:
> ```xml
> <add key="github" value="https://nuget.pkg.github.com/clarivex-tech/index.json" />
> ```

## Registration

```csharp
builder.Services.AddGenesisIdempotency(
    builder.Configuration.GetSection("Idempotency"));

app.UseIdempotency();
```

## Key Features

- `[Idempotent]` attribute for opt-in per-endpoint idempotency
- Global middleware for blanket idempotency enforcement
- Request fingerprinting to detect payload mismatches on key reuse
- Idempotency key validation (format, length, uniqueness)
- Built-in observability with structured logging, metrics, and tracing

## Usage

```csharp
[HttpPost]
[Idempotent]
public async Task<IActionResult> CreateOrder(CreateOrderRequest request)
{
    // If the same Idempotency-Key is received again,
    // the cached response is returned automatically.
    var order = await _orderService.CreateAsync(request);
    return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
}
```

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.
