# Pervaxis.Genesis.Caching.AWS

AWS ElastiCache (Redis) caching provider for the Pervaxis Genesis platform.

## Overview

`Pervaxis.Genesis.Caching.AWS` implements the `ICache` abstraction from `Pervaxis.Core.Abstractions` using [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/) as the Redis client. It supports AWS ElastiCache clusters and local Redis instances (including LocalStack).

## Installation

```xml
<PackageReference Include="Pervaxis.Genesis.Caching.AWS" Version="1.0.0" />
```

> **GitHub Packages feed** — add the following to your `nuget.config`:
> ```xml
> <add key="github" value="https://nuget.pkg.github.com/clarivex-tech/index.json" />
> ```

## Configuration

### appsettings.json

```json
{
  "Caching": {
    "ConnectionString": "my-cluster.abc123.cache.amazonaws.com:6379",
    "Region": "ap-south-1",
    "UseSsl": true,
    "DefaultExpiry": "01:00:00",
    "KeyPrefix": "myapp",
    "Database": 0,
    "ConnectTimeoutMs": 5000,
    "SyncTimeoutMs": 5000,
    "AbortOnConnectFail": false
  }
}
```

### Option properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ConnectionString` | `string` | *(required)* | Redis endpoint (e.g. `host:port`) |
| `Region` | `string` | *(required)* | AWS region (e.g. `ap-south-1`) |
| `UseSsl` | `bool` | `true` | Enable TLS — required for ElastiCache in-transit encryption |
| `DefaultExpiry` | `TimeSpan` | `01:00:00` | Default TTL when no expiry is specified |
| `KeyPrefix` | `string` | `""` | Prefix prepended to every key as `{prefix}:{key}` |
| `Database` | `int` | `0` | Redis logical database index |
| `ConnectTimeoutMs` | `int` | `5000` | Connection timeout in milliseconds |
| `SyncTimeoutMs` | `int` | `5000` | Synchronous operation timeout in milliseconds |
| `AbortOnConnectFail` | `bool` | `false` | Whether to throw on initial connection failure |
| `UseLocalEmulator` | `bool` | `false` | Disables SSL; intended for local Redis / LocalStack |
| `LocalEmulatorUrl` | `string` | `""` | Not used by Redis directly — set `ConnectionString` to `localhost:6379` |

### Resilience configuration

The Caching provider includes built-in resilience policies (retry, circuit breaker, timeout) powered by [Polly v8](https://github.com/App-vNext/Polly) to handle transient AWS failures gracefully.

#### Resilience options

| Property | Type | Default | Description |
|---|---|---|---|
| `Resilience.Enabled` | `bool` | `true` | Enable/disable all resilience policies |
| `Resilience.RetryCount` | `int` | `3` | Max retry attempts for transient errors |
| `Resilience.RetryDelayMs` | `int` | `1000` | Base delay for exponential backoff (ms) |
| `Resilience.MaxRetryDelayMs` | `int` | `30000` | Maximum retry delay cap (ms) |
| `Resilience.CircuitBreakerFailureThreshold` | `double` | `0.5` | Failure ratio (0.0-1.0) to open circuit |
| `Resilience.CircuitBreakerMinimumThroughput` | `int` | `10` | Min calls before circuit can trip |
| `Resilience.CircuitBreakerDurationSeconds` | `int` | `60` | Duration to keep circuit open (seconds) |
| `Resilience.CircuitBreakerSamplingDurationSeconds` | `int` | `30` | Sampling window for failure ratio (seconds) |
| `Resilience.TimeoutSeconds` | `int` | `30` | Per-operation timeout (seconds) |

#### Example configuration

```json
{
  "Caching": {
    "ConnectionString": "my-cluster.cache.amazonaws.com:6379",
    "Region": "ap-south-1",
    "Resilience": {
      "Enabled": true,
      "RetryCount": 3,
      "RetryDelayMs": 1000,
      "CircuitBreakerFailureThreshold": 0.5,
      "CircuitBreakerDurationSeconds": 60,
      "TimeoutSeconds": 30
    }
  }
}
```

#### Transient errors handled

The resilience pipeline automatically retries these transient AWS and network errors:

- **Throttling**: HTTP 429, `ThrottlingException`, `RequestLimitExceeded`
- **Service errors**: HTTP 500, 503, `ServiceUnavailable`, `InternalServerError`
- **Network errors**: `IOException`, `SocketException`, `HttpRequestException`
- **Timeouts**: `TimeoutException`, `RequestTimeout`

#### Disabling resilience

To disable resilience (e.g., for testing):

```json
{
  "Caching": {
    "Resilience": {
      "Enabled": false
    }
  }
}
```

## Registration

### IConfiguration overload

```csharp
builder.Services.AddGenesisCaching(
    builder.Configuration.GetSection("Caching"));
```

### Action overload

```csharp
builder.Services.AddGenesisCaching(options =>
{
    options.ConnectionString = "localhost:6379";
    options.Region = "ap-south-1";
    options.UseSsl = false;
    options.DefaultExpiry = TimeSpan.FromMinutes(30);
});
```

The provider is registered as a **Singleton** (`ICache → ElastiCacheProvider`). The underlying Redis connection uses `Lazy<IConnectionMultiplexer>` so it is created on first use.

## Usage

Inject `ICache` wherever caching is needed:

```csharp
public class ProductService(ICache cache)
{
    private const string Prefix = "products";

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct = default)
    {
        var key = $"{Prefix}:{id}";

        var cached = await cache.GetAsync<Product>(key, ct);
        if (cached is not null)
        {
            return cached;
        }

        var product = await _repository.FindAsync(id, ct);
        if (product is not null)
        {
            await cache.SetAsync(key, product, TimeSpan.FromMinutes(15), ct);
        }

        return product;
    }

    public async Task InvalidateAsync(int id, CancellationToken ct = default)
        => await cache.RemoveAsync($"{Prefix}:{id}", ct);
}
```

### Available operations

| Method | Description |
|---|---|
| `GetAsync<T>(key)` | Get a single cached item; returns `null` on miss |
| `SetAsync<T>(key, value, expiry?)` | Store an item; uses `DefaultExpiry` when `expiry` is omitted |
| `RemoveAsync(key)` | Delete a key; returns `true` if the key existed |
| `ExistsAsync(key)` | Check if a key exists without fetching the value |
| `GetManyAsync<T>(keys)` | Batch-get multiple keys in a single round-trip |
| `SetManyAsync<T>(items, expiry?)` | Batch-set multiple items using a Redis pipeline |
| `RefreshAsync(key, expiry?)` | Reset the TTL on an existing key |

All methods accept an optional `CancellationToken` and throw `GenesisException` on Redis errors.

## Local Development with LocalStack

```json
{
  "Caching": {
    "ConnectionString": "localhost:6379",
    "Region": "us-east-1",
    "UseSsl": false,
    "UseLocalEmulator": true
  }
}
```

Start Redis locally:

```bash
docker run -d -p 6379:6379 redis:7-alpine
```

Or with LocalStack:

```bash
localstack start
awslocal elasticache create-replication-group \
  --replication-group-id local-cluster \
  --replication-group-description "Local dev"
```

## IAM Permissions

The `AWSSDK.ElastiCache` package is included for optional configuration discovery (e.g. listing cluster endpoints at startup). The Redis operations themselves go directly over the TCP connection and do not require IAM permissions.

If you use the AWS SDK for endpoint discovery, the following permissions are required:

```json
{
  "Effect": "Allow",
  "Action": [
    "elasticache:DescribeReplicationGroups",
    "elasticache:DescribeCacheClusters"
  ],
  "Resource": "*"
}
```

## Serialization

Values are serialized to JSON using `System.Text.Json` with `PropertyNameCaseInsensitive = true`. Ensure your cached types are JSON-serializable.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `GenesisConfigurationException` on startup | `ConnectionString` or `Region` is empty | Check `appsettings.json` / environment variables |
| `GenesisException: Cache set operation failed` | Redis unreachable | Verify security group, VPC, and endpoint |
| All reads return `null` | Wrong `KeyPrefix` or `Database` index | Confirm the prefix matches what was used during write |
| SSL handshake error | `UseSsl=true` but Redis not TLS-enabled | Set `UseSsl=false` for local/non-TLS clusters |
| Timeout exceptions | Network latency or cluster overload | Increase `ConnectTimeoutMs` / `SyncTimeoutMs` |
