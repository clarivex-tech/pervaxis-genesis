# Pervaxis.Genesis.Secrets.AWS

Read tenant secrets from shared AWS Secrets Manager infrastructure. The account,
SDK client and in-process cache are shared; secret names isolate tenant values.

## Registration and reads

```csharp
using Pervaxis.Genesis.Secrets;
using Pervaxis.Genesis.Secrets.AWS.Extensions;

builder.Services.AddGenesisSecretsManager(builder.Configuration);
// Host supplies a scoped ITenantContext resolved from authenticated execution.

// Within the resolved tenant scope:
var secrets = scope.ServiceProvider.GetRequiredService<ISecretProvider>();
var password = await secrets.GetSecretAsync("database/password", cancellationToken);
var credentials = await secrets.GetSecretAsync<Dictionary<string, string>>("database", cancellationToken);
var certificate = await secrets.GetBinarySecretAsync("certificates/client", cancellationToken);
```

```json
{
  "Genesis": {
    "Secrets": {
      "Region": "us-east-1",
      "EnableTenantIsolation": true,
      "EnableCaching": true,
      "CacheTtlSeconds": 300
    }
  }
}
```

For tenant acme, database/password resolves to tenant-acme/database/password.
Relative nested names are allowed; absolute paths, ARNs, traversal segments,
backslashes, encoded characters and empty segments are rejected. Missing or invalid
tenant context fails before cache or AWS access. Tenant reads never fall back to a
platform secret. EnableTenantIsolation=false is rejected.

The provider is Scoped. Keyed SDK clients and IMemoryCache are container-owned
Singletons. Cache keys include client identity and the fully resolved secret name;
successful reads expire after CacheTtlSeconds. Missing values and failures are not
cached. Binary values are copied before returning, so callers cannot mutate cached
bytes. JSON parsing and AWS errors propagate; only ResourceNotFound maps to null.
No secret values are logged.

## Platform startup credentials

Opt in separately only in trusted application composition:

```csharp
builder.Services.AddGenesisPlatformSecretsManager(builder.Configuration);
var platform = app.Services.GetRequiredService<IPlatformSecretProvider>();
var databasePassword = await platform.GetSecretAsync("database/password");
```

This resolves platform/database/password without tenant context. Platform access
is Singleton and shares the keyed SDK client/cache when both services are registered.
Tenant registration does not expose the platform interface; platform registration
does not expose the tenant interface. The host must keep platform access out of
tenant-controlled APIs; DI registration is not an authorization system.

## Compatibility and scope

These sources were reconstructed from surviving API documentation, not recovered
verbatim. The read method names, package identities and default tenant/platform
prefixes are retained. The former SecretScope overloads are intentionally replaced
by IPlatformSecretProvider. Configurable path templates and per-tenant KMS options
are not restored. Existing secret names must match the fixed prefixes, or be migrated
before rollout; no data is moved automatically.

Use the normal AWS credential chain and the SDK's retry behavior. Local emulators
can use inherited UseLocalEmulator/LocalEmulatorUrl settings with host-supplied test
credentials. Provision IAM permissions, secrets, encryption and rotation outside
this library. This implementation provides reads and basic application-level
isolation; it adds no secret writes, rotation or provisioning API.
