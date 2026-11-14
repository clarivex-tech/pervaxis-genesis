# Pervaxis.Genesis.Parameters.AWS

Read tenant configuration from shared AWS Systems Manager Parameter Store.
The SDK client and cache are shared; trusted hierarchy prefixes isolate values.

## Registration and reads

```csharp
using Pervaxis.Genesis.Parameters;
using Pervaxis.Genesis.Parameters.AWS.Extensions;

builder.Services.AddGenesisParameterStore(builder.Configuration);
// Host supplies a scoped ITenantContext resolved from authenticated execution.

var parameters = scope.ServiceProvider.GetRequiredService<IParameterProvider>();
var apiUrl = await parameters.GetParameterAsync("api/url", cancellationToken);
var settings = await parameters.GetParameterAsync<Dictionary<string, string>>("settings", cancellationToken);
var email = await parameters.GetParametersByPathAsync("/email", recursive: true, cancellationToken);
```

```json
{
  "Genesis": {
    "Parameters": {
      "Region": "us-east-1",
      "EnableTenantIsolation": true,
      "EnableCaching": true,
      "CacheTtlSeconds": 300,
      "WithDecryption": true,
      "MaxResults": 10
    }
  }
}
```

For tenant acme, api/url resolves to /tenant/acme/api/url. Path /email reads only
/tenant/acme/email/; / reads the current tenant root. Results use names relative to
the tenant root (for example email/host). Recursive defaults to true; MaxResults is
the AWS page size, not a total result cap. All pages retain the tenant prefix,
decryption and recursion settings. Out-of-path responses fail rather than returning
partial or foreign results. Empty pages with continuation tokens are handled.

Single names must be relative; hierarchy reads permit a leading/trailing slash.
Traversal, absolute single-value paths, ARNs, encoded characters, backslashes and
empty internal segments are rejected. Missing or invalid tenant context fails
before AWS or cache access; EnableTenantIsolation=false is rejected. There is no
platform fallback.

The provider is Scoped. Keyed SDK clients and IMemoryCache are container-owned
Singletons. Successful single-value reads cache by client identity, full parameter
name and decryption setting for CacheTtlSeconds. Path reads are not cached.
ParameterNotFound maps to null; JSON reads return default(T) for missing values.
Other AWS/parsing errors and cancellation propagate. Parameter values are not logged.

## Platform startup configuration

```csharp
builder.Services.AddGenesisPlatformParameterStore(builder.Configuration);
var platform = app.Services.GetRequiredService<IPlatformParameterProvider>();
var apiUrl = await platform.GetParameterAsync("api/url");
```

Explicit platform registration reads /platform/api/url without tenant context.
The platform provider is Singleton and shares the SDK client/cache when both
registrations are present. Tenant registration does not expose platform access,
and platform registration does not expose tenant access. Only trusted application
composition should register/use the platform interface; never bind it to
tenant-controlled endpoints.

## Compatibility and scope

These sources were reconstructed from surviving API documentation, not recovered
verbatim. Package identities, read method names and default hierarchy prefixes are
retained. Former ParameterScope overloads are replaced by the separate
IPlatformParameterProvider; configurable path templates are not restored.
Existing parameter paths must match the fixed hierarchy before rollout; no data
migration is performed automatically.

Use normal AWS credentials and SDK retries. Local emulators can use inherited
UseLocalEmulator/LocalEmulatorUrl settings with host-supplied test credentials.
Provision parameters, IAM policies and encryption outside this library.
This package implements reads and basic application-level tenant isolation;
writes, provisioning and advanced access policies remain outside this phase.
