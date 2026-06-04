# Clarivolt — Feature Flag Module
**Genesis Library · Horizontal Platform Layer**
Engineering Specification v1.0 · June 2025

---

## 1. Purpose & Scope

This specification defines the implementation of the Feature Flag module inside the **Genesis** library — Clarivolt's horizontal infrastructure layer. Vertical domain modules consume flags without any knowledge of where or how flags are stored.

> **Key Principle:** Genesis owns the flag infrastructure. Forge wires it into every generated print. Domain (vertical) code only calls `IFeatureManager` — never reads config directly.

---

## 2. Architecture Placement

| Layer | Responsibility | Owns Flag Logic? |
|---|---|---|
| **Genesis Library** | `IFeatureManager` registration, AWS AppConfig provider, appsettings bridge | YES |
| **Forge Engine** | Registers Genesis Feature Flag module in every generated print via `Program.cs` | NO — delegates to Genesis |
| **Vertical / Domain Code** | Injects `IFeatureManager`, calls `IsEnabledAsync()` | NO — consumer only |

---

## 3. Technology Decision

Use `Microsoft.FeatureManagement` (first-party, .NET 10 native) backed by **AWS AppConfig** in production. No third-party SDK required.

| Environment | Flag Source | Notes |
|---|---|---|
| Local / Dev | `appsettings.json` | Instant, no AWS dependency |
| Staging | `appsettings.staging.json` + AppConfig override | Mirrors prod behavior |
| Production | AWS AppConfig (live polling) | No redeploy for flag changes |

---

## 4. Genesis Module Implementation

### 4.1 NuGet Packages

```
Microsoft.FeatureManagement.AspNetCore
Amazon.Extensions.Configuration.SystemsManager
```

### 4.2 IFeatureFlagModule Interface

```csharp
namespace Genesis.FeatureFlags;

public interface IFeatureFlagModule
{
    void Register(IServiceCollection services, IConfiguration config);
}
```

### 4.3 FeatureFlagModule Implementation

```csharp
namespace Genesis.FeatureFlags;

public class FeatureFlagModule : IFeatureFlagModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddFeatureManagement()
                .AddFeatureFilter<PercentageFilter>()
                .AddFeatureFilter<TimeWindowFilter>()
                .AddFeatureFilter<TenantFilter>();   // custom — see §4.5
    }
}
```

### 4.4 AWS AppConfig Provider

Add to `Program.cs` configuration builder in the Forge-generated print:

```csharp
builder.Configuration
    .AddSystemsManager(source =>
    {
        source.Path        = $"/clarivolt/{env}/feature-flags";
        source.ReloadAfter = TimeSpan.FromSeconds(30);
        source.Optional    = true;   // fallback to appsettings if unavailable
    });
```

### 4.5 Custom TenantFilter

Enables per-tenant flag targeting — critical for Clarivolt's multi-tenant architecture.

```csharp
[FilterAlias("Tenant")]
public class TenantFilter : IFeatureFilter
{
    private readonly IHttpContextAccessor _ctx;

    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        var tenantId = _ctx.HttpContext?.Items["TenantId"]?.ToString();
        var allowed  = context.Parameters.GetSection("AllowedTenants")
                               .Get<string[]>() ?? [];
        return Task.FromResult(allowed.Contains(tenantId));
    }
}
```

---

## 5. Forge Print Wiring

Forge automatically injects the following into every generated service's `Program.cs`:

```csharp
// ── Genesis: Feature Flags ──────────────────────────────────────────
new Genesis.FeatureFlags.FeatureFlagModule()
    .Register(builder.Services, builder.Configuration);
```

No manual wiring required per service. Every print gets feature flags out of the box.

---

## 6. appsettings.json Structure

### 6.1 Local / Dev (empty registry — no domain flags here)

```json
{
  "FeatureManagement": {
  }
}
```

### 6.2 Tenant-scoped flag example

```json
"YourFeatureFlag": {
  "EnabledFor": [
    {
      "Name": "Tenant",
      "Parameters": {
        "AllowedTenants": [ "tenant-001", "tenant-007" ]
      }
    }
  ]
}
```

### 6.3 Percentage rollout example

```json
"YourFeatureFlag": {
  "EnabledFor": [
    {
      "Name": "Percentage",
      "Parameters": {
        "Value": 20
      }
    }
  ]
}
```

### 6.4 Time window example

```json
"YourFeatureFlag": {
  "EnabledFor": [
    {
      "Name": "TimeWindow",
      "Parameters": {
        "Start": "2025-07-01T00:00:00",
        "End":   "2025-07-31T23:59:59"
      }
    }
  ]
}
```

---

## 7. Vertical Domain Consumption

Domain modules inject `IFeatureManager` and call `IsEnabledAsync()`. No other knowledge of flag infrastructure is permitted in vertical code.

```csharp
public class SomeVerticalService(IFeatureManager features)
{
    public async Task<Result> ProcessAsync(Request request)
    {
        if (await features.IsEnabledAsync("YourFeatureFlag"))
            return await RunNewBehaviourAsync(request);

        return await RunCurrentBehaviourAsync(request);
    }
}
```

> **Hard Rule:** Vertical code MUST NOT reference AWS AppConfig, appsettings, or any flag provider directly. Only `IFeatureManager` is permitted in domain modules.

---

## 8. AWS AppConfig Setup

One-time setup per environment by the DevOps engineer:

| Key | Value |
|---|---|
| Application | `clarivolt` |
| Environment | `dev` \| `staging` \| `prod` |
| Configuration Profile | `feature-flags` |
| Parameter path (SSM) | `/clarivolt/{env}/feature-flags` |
| Polling interval | `30 seconds` |

**IAM Permissions Required:**
```
appconfig:GetConfiguration
appconfig:StartConfigurationSession
ssm:GetParametersByPath
```

---

## 9. Acceptance Criteria

1. Genesis `FeatureFlagModule` registers cleanly with no errors on startup.
2. `IFeatureManager` resolves via DI in any vertical service.
3. Flags read from `appsettings.json` in local/dev environment.
4. Flags read from AWS AppConfig in staging and production.
5. Flag changes in AppConfig reflect within 30 seconds — no redeploy.
6. `TenantFilter` correctly isolates flag state per tenant.
7. Forge-generated prints include Genesis feature flag wiring automatically.
8. Unit tests cover: flag enabled, flag disabled, tenant-scoped flag.
9. No vertical module references AppConfig or appsettings directly.

---

## 10. Out of Scope (Post-MVP)

- LaunchDarkly / Flagsmith integration
- A/B testing framework
- Non-engineer flag management UI
- gRPC flag propagation across microservices
