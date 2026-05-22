# Pervaxis.Genesis — ExceptionId + Observability Implementation Checklist

**Branch**: `feature/genesis-exceptionid-observability`  
**Scope**: Add `ExceptionId` to `GenesisException`, integrate with provider-level observability.  
**Status**: FINAL and ready for implementation.

---

## EXECUTIVE SUMMARY

**Goal**: When a provider throws an exception, auto-generate `ExceptionId` and track it in CloudWatch + Grafana.

**Current state**:
- ✅ `GenesisException` exists
- ✅ Provider-level `Activity` instrumentation is already in place
- ✅ Structured logging via Serilog is already in place
- ⬜ `ExceptionId` generation is missing
- ⬜ `ExceptionId` enrichment in logs and traces is missing

**What this adds**:
- `ExceptionId` property on `GenesisException`
- `ExceptionId` in provider-level structured logs
- `ExceptionId` in provider-level OTel activity tags
- `ExceptionId` flows to CloudWatch + Grafana

**Contract impact**: `ExceptionId`, `ErrorCode`, and `Context` are new properties on `GenesisException`. Tests and any exception handling code will need updates.

---

## 1. GENESISEXCEPTION — ADD PROPERTIES

### 1.1 Update `GenesisException` Class

**File**: `src/Pervaxis.Genesis.Base/Exceptions/GenesisException.cs`

**Current shape**:
```csharp
public class GenesisException : Exception
{
    public string ProviderName { get; }
    // existing properties
}
```

**Exact changes**:

- [ ] 1.1.1 Add `ExceptionId` property (auto-generated, immutable):
  ```csharp
  public string ExceptionId { get; } = GenerateExceptionId();

  private static string GenerateExceptionId()
  {
      return $"ex_{Guid.NewGuid().ToString("N")[..16]}";
  }
  ```

- [ ] 1.1.2 Add `ErrorCode` property (optional, domain classification):
  ```csharp
  public string? ErrorCode { get; init; }
  ```

- [ ] 1.1.3 Add `Context` property (optional, structured metadata):
  ```csharp
  public Dictionary<string, object>? Context { get; init; }
  ```

**Example updated constructor**:
```csharp
public GenesisException(
    string providerName,
    string message,
    Exception? innerException = null,
    string? errorCode = null,
    Dictionary<string, object>? context = null)
    : base(message, innerException)
{
    ProviderName = providerName;
    ErrorCode = errorCode;
    Context = context;
}
```

- [ ] 1.1.4 Update derived exception classes to support new properties:
  - `PervaxisInfrastructureException`
  - `PervaxisDomainException`
  - Others as needed

- [ ] 1.1.5 Add unit tests:
  - [ ] 1.1.5.1 `ExceptionId` format matches `^ex_[a-f0-9]{16}$`
  - [ ] 1.1.5.2 `ExceptionId` is unique across instances
  - [ ] 1.1.5.3 `ErrorCode` and `Context` are optional and nullable
  - [ ] 1.1.5.4 Properties are read-only (`ExceptionId`) or init-only (`ErrorCode`, `Context`)

- [ ] 1.1.6 Update existing tests that construct `GenesisException` to include or ignore new properties as appropriate

---

## 2. PROVIDER EXCEPTION HANDLING — ENRICH LOGS

### 2.1 Update Provider Exception Logging

**Scope**: All provider implementations that catch and log `GenesisException`.  
**Pattern**: Provider-local, inline with existing exception handling.

**Example provider**: `src/Pervaxis.Genesis.Caching.AWS/CachingProvider.cs`

**Exact changes**:

- [ ] 2.1.1 When catching `GenesisException`, log `ExceptionId` as a structured field:
  ```csharp
  catch (GenesisException gex)
  {
      _logger.LogError(
          gex,
          "Provider {ProviderName} error {ExceptionId}",
          gex.ProviderName,
          gex.ExceptionId);
      throw;
  }
  ```

- [ ] 2.1.2 Key structured fields to capture:
  - `ExceptionId`
  - `ProviderName`
  - `ErrorCode` if available

- [ ] 2.1.3 Example log output:
  ```
  @timestamp: 2026-05-22T10:30:00.1234567Z
  level: ERROR
  ProviderName: Caching.AWS
  ExceptionId: ex_a1b2c3d4e5f67890
  ErrorCode: CACHE_TIMEOUT
  @message: Provider Caching.AWS error ex_a1b2c3d4e5f67890
  ```

- [ ] 2.1.4 Apply the logging pattern consistently across all providers:
  - Caching.AWS
  - Messaging.AWS
  - Search.AWS
  - FileStorage.AWS
  - Others as they exist

- [ ] 2.1.5 Add unit tests for logging output:
  - [ ] 2.1.5.1 Mock `ILogger`, verify `ExceptionId` is logged
  - [ ] 2.1.5.2 Verify structured fields are present
  - [ ] 2.1.5.3 Test both with and without `ErrorCode`

---

## 3. OPENTELEMETRY ACTIVITY TAGS — PROVIDER-LOCAL ENRICHMENT

### 3.1 Tag Activities with `ExceptionId` (Provider-Level)

**Scope**: Provider methods that create and manage `Activity` instances.  
**Pattern**: Match existing provider-local `Activity` instrumentation, not a centralized handler.

**Example provider method**:
```csharp
public async Task<T> GetAsync<T>(string key, CancellationToken ct)
{
    using var activity = PervaxisActivitySource.StartActivity("CacheGet");
    activity?.SetTag("cache.key", key);

    try
    {
        // Get from cache...
        return result;
    }
    catch (GenesisException gex)
    {
        activity?.SetTag("exception.id", gex.ExceptionId);
        activity?.SetTag("exception.error_code", gex.ErrorCode);
        activity?.SetTag("exception.provider", gex.ProviderName);

        _logger.LogError(gex, "Cache operation failed {ExceptionId}", gex.ExceptionId);
        throw;
    }
}
```

**Exact changes**:

- [ ] 3.1.1 In each provider method that handles `GenesisException`:
  - Enrich the current `Activity` with exception tags
  - Tag `exception.id` with `ExceptionId`
  - Tag `exception.error_code` with `ErrorCode` if available
  - Tag `exception.provider` with `ProviderName`

- [ ] 3.1.2 Example activity tags in trace:
  ```
  Activity: CacheGet
    trace_id: 550e8400e29b41d4a716446655440000
    span_id: f9fe7192cd0fef23
    tags:
      - cache.key: user:123
      - exception.id: ex_a1b2c3d4e5f67890
      - exception.error_code: CACHE_TIMEOUT
      - exception.provider: Caching.AWS
      - http.status_code: 500
  ```

- [ ] 3.1.3 Pattern applies to all providers:
  - Caching
  - Messaging
  - Search
  - FileStorage
  - Others as needed

- [ ] 3.1.4 Add integration tests:
  - [ ] 3.1.4.1 Mock `Activity`, verify exception tags are set
  - [ ] 3.1.4.2 Verify tag values match exception properties
  - [ ] 3.1.4.3 Test with and without `ErrorCode`

---

## 4. CORRELATION ID CLARIFICATION

### 4.1 CorrelationId vs TraceId

**Important distinction**:
- **TraceId** (`Activity.Current?.TraceId`): OTel distributed trace identifier, auto-generated
- **CorrelationId**: Business-level request grouping, may come from HTTP header or service context

**Exact changes**:

- [ ] 4.1.1 Do not populate `CorrelationId` from `TraceId` in `GenesisException`
- [ ] 4.1.2 `CorrelationId` is optional and contextual, set by the service layer if needed
- [ ] 4.1.3 `TraceId` flows through OTel/Activity automatically and is visible in CloudWatch traces

**Example**:
```csharp
catch (GenesisException gex)
{
    var correlationId = request.Headers["X-Correlation-Id"];
    _logger.LogError(
        "Request {CorrelationId} failed: {ExceptionId}",
        correlationId,
        gex.ExceptionId);
}
```

- [ ] 4.1.4 Document in provider guide: "TraceId is automatic, CorrelationId is contextual."

---

## 5. VALIDATION — PROVIDER-LEVEL TESTING

### 5.1 Unit Tests

**Scope**: Provider methods, exception generation, logging output.

**Exact changes**:

- [ ] 5.1.1 Create unit test file: `Pervaxis.Genesis.{Provider}.Tests/ExceptionIdTests.cs`

- [ ] 5.1.2 Test `ExceptionId` generation:
  ```csharp
  [TestFixture]
  public class ExceptionIdTests
  {
      [Test]
      public void GenesisExceptionIdIsGenerated()
      {
          var ex = new GenesisException("TestProvider", "Test error");

          Assert.That(ex.ExceptionId, Does.Match(@"^ex_[a-f0-9]{16}$"));
      }

      [Test]
      public void ExceptionIdIsUnique()
      {
          var ids = new HashSet<string>();

          for (int i = 0; i < 100; i++)
          {
              var ex = new GenesisException("TestProvider", $"Error {i}");
              ids.Add(ex.ExceptionId);
          }

          Assert.That(ids.Count, Is.EqualTo(100));
      }

      [Test]
      public void ErrorCodeAndContextAreOptional()
      {
          var ex1 = new GenesisException("TestProvider", "Error");
          var ex2 = new GenesisException(
              "TestProvider",
              "Error",
              null,
              "CUSTOM_CODE",
              new Dictionary<string, object> { { "key", "value" } });

          Assert.That(ex1.ErrorCode, Is.Null);
          Assert.That(ex1.Context, Is.Null);
          Assert.That(ex2.ErrorCode, Is.EqualTo("CUSTOM_CODE"));
          Assert.That(ex2.Context, Is.Not.Null);
      }
  }
  ```

- [ ] 5.1.3 Test provider logging output against the actual exception thrown by `provider.Get()`:
  ```csharp
  [TestFixture]
  public class ProviderLoggingTests
  {
      [Test]
      public void LogsIncludeExceptionId()
      {
          var logCollector = new TestLogCollector();
          var provider = new CachingProvider(_cache, logCollector.Logger);

          GenesisException? caught = null;

          try
          {
              provider.Get("missing-key");
          }
          catch (GenesisException ex)
          {
              caught = ex;
          }

          Assert.That(caught, Is.Not.Null);

          var logs = logCollector.GetLogs();
          Assert.That(logs.Any(l => l.Contains(caught!.ExceptionId)), Is.True);
          Assert.That(logs.Any(l => l.Contains(caught.ProviderName)), Is.True);
          Assert.That(logs.Any(l => string.IsNullOrEmpty(caught.ErrorCode) || l.Contains(caught.ErrorCode!)), Is.True);
      }
  }
  ```

- [ ] 5.1.4 Test activity tags if using `TestActivityListener` or similar:
  ```csharp
  [Test]
  public void ActivityTagsIncludeExceptionId()
  {
      var listener = new TestActivityListener();
      var provider = new CachingProvider(_cache, _logger);

      using (listener.Listen())
      {
          try
          {
              provider.Get("key");
          }
          catch
          {
          }

          var activity = listener.GetRecordedActivity();
          Assert.That(activity.Tags["exception.id"], Does.Match(@"^ex_[a-f0-9]{16}$"));
      }
  }
  ```

### 5.2 Integration Tests

**Scope**: Optional, if a test environment with CloudWatch/Serilog sink is available.

**Exact changes**:

- [ ] 5.2.1 Deploy provider to test environment
- [ ] 5.2.2 Call provider method that throws exception
- [ ] 5.2.3 Query CloudWatch Logs:
  ```
  fields @timestamp, @message, ExceptionId, ProviderName, ErrorCode
  | filter ExceptionId like /^ex_/
  | limit 10
  ```
- [ ] 5.2.4 Verify `ExceptionId` appears in results

### 5.3 Grafana Validation

**Note**: Once deployed and calling providers, validate in Grafana.

**Exact validation**:
- [ ] Query Grafana trace view by `ExceptionId`
- [ ] Verify provider method and exception tags are visible

---

## 6. DOCUMENTATION

### 6.1 Update Provider README

**File**: `README.md` or equivalent in each provider package

**Exact changes**:

- [ ] 6.1.1 Add section: "Exception Handling & Observability"
  ```markdown
  ## Exception Handling & Observability

  All GenesisExceptions include a unique ExceptionId (format: `ex_{Guid:N}[..16]`).

  ### Exception Properties
  - **ExceptionId**: Unique identifier, auto-generated
  - **ProviderName**: Which provider threw the exception
  - **ErrorCode**: Optional domain classification, for example `CACHE_MISS`
  - **Context**: Optional structured metadata, for example `{ "key": "...", "ttl": "..." }`

  ### Observability Flow
  1. Exception thrown with auto-generated `ExceptionId`
  2. Provider logs exception with `ExceptionId` as a structured field
  3. Provider enriches `Activity` with `exception.id`
  4. Serilog sends logs to CloudWatch
  5. OTel traces are visible in Grafana with `exception.id`

  ### Example
  ```csharp
  try
  {
      var value = await _cache.GetAsync(key);
  }
  catch (GenesisException gex)
  {
      _logger.LogError(gex, "Cache operation failed {ExceptionId}", gex.ExceptionId);
      activity?.SetTag("exception.id", gex.ExceptionId);
      throw;
  }
  ```

  ### Debugging
  - Find `ExceptionId` in CloudWatch logs
  - Search Grafana by `ExceptionId` to see the full trace
  - Inspect exception context in structured logs
  ```

- [ ] 6.1.2 Update any existing exception handling docs to mention `ExceptionId`

### 6.2 Update Provider Guide

**File**: `PROVIDER_GUIDE.md` or `OBSERVABILITY_PATTERN.md`

**Exact changes**:

- [ ] 6.2.1 Add to the existing observability section:
  - `ExceptionId` is auto-generated, no configuration needed
  - Always log exceptions with `ExceptionId`
  - Always tag `Activity` with `exception.id` if `Activity` is present
  - `ErrorCode` and `Context` are optional but recommended

- [ ] 6.2.2 Add example code snippets for each provider type

---

## 7. SUPABASE PERSISTENCE — BACKLOG

**Status**: Out of scope for this checklist.

**Reasoning**: MVP goal is CloudWatch + Grafana. Supabase persistence is a follow-up feature for historical exception lookup.

**Backlog item**: "Add exception persistence layer" as a separate checklist when needed.

**Dependencies**:
- `Pervaxis.Core.Exceptions` package (`IExceptionRepository` interface)
- Supabase table schema
- Service-layer wiring to call the repository

---

## IMPLEMENTATION ORDER

1. [ ] Update `GenesisException` class (section 1)
2. [ ] Add `ExceptionId` logging to providers (section 2)
3. [ ] Add exception tags to provider `Activity` instances (section 3)
4. [ ] Add unit tests (section 5.1)
5. [ ] Optional: Add integration tests (section 5.2)
6. [ ] Update documentation (section 6)
7. [ ] Code review and merge

---

## CONTRACT IMPACT

⚠️ **Breaking changes**:
- `GenesisException` now has `ExceptionId` and optional `ErrorCode` / `Context`
- Exception constructors may need updates in derived classes
- Tests that construct `GenesisException` need updates
- Serialization and deserialization of exceptions may be affected

**Mitigation**:
- `ExceptionId` is read-only and generated automatically
- `ErrorCode` and `Context` are optional and init-only
- Existing code that just throws `GenesisException` will still work
- Tests will need minimal updates for `ExceptionId` assertions

---

## BLOCKERS / DECISIONS

- [x] ✅ **ExceptionId format**: `ex_{Guid:N}[..16]`
- [x] ✅ **Generation**: Auto-generated in `GenesisException`
- [x] ✅ **Logging**: Structured field in provider exception logs
- [x] ✅ **Activity tags**: Provider-local, inline with existing instrumentation
- [x] ✅ **CorrelationId**: Separate from `TraceId`, optional, contextual
- [x] ✅ **Supabase**: Backlog only, not MVP

---

## DELIVERABLES

✅ `GenesisException` with `ExceptionId`, `ErrorCode`, and `Context`  
✅ Provider-level exception logging with `ExceptionId`  
✅ Provider-level `Activity` tag enrichment  
✅ Unit tests for exception generation and logging  
✅ Updated provider documentation  
✅ Clear separation of `TraceId` vs `CorrelationId`  

---

## SIGN-OFF

✅ Aligned with repo namespaces (`Pervaxis.Genesis.*`)  
✅ Aligned with provider-local instrumentation pattern  
✅ Contract impact documented  
✅ Markdown cleaned up  
✅ FINAL and ready for implementation
