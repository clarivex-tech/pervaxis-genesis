# PR #52 Security Review - BLOCK MERGE

**Status**: ❌ **CRITICAL ISSUES FOUND - DO NOT MERGE**

## Executive Summary

PR #52 introduces **6 critical security vulnerabilities** that must be fixed before merge:

1. ❌ **Singleton with captured tenant context** (Severity: CRITICAL)
2. ❌ **Manual key building bypasses security** (Severity: CRITICAL)  
3. ❌ **Missing tenant metadata stamps** (Severity: HIGH)
4. ❌ **No presigned URL expiry cap** (Severity: MEDIUM)
5. ❌ **List operation isolation gaps** (Severity: HIGH)
6. ❌ **Missing cross-tenant tests** (Severity: HIGH)

---

## Critical Issues Detail

### 1. Singleton Lifetime with Captured Tenant Context ❌ CRITICAL

**File**: `FileStorageServiceCollectionExtensions.cs`  
**Current Code**:
```csharp
services.AddSingleton<IFileStorage, S3FileStorageProvider>();
```

**Problem**:
- `S3FileStorageProvider` constructor takes `ITenantContext?`
- As a Singleton, it captures tenant context at creation time
- **Result**: All requests served by wrong tenant context OR scope validation failures

**Impact**:
- Tenant A can access Tenant B's files
- Production outage due to scope validation errors

**Fix Applied** (Partial):
```csharp
services.AddScoped<TenantKeyResolver>();
services.AddScoped<IFileStorage, S3FileStorageProvider>();
```

**Status**: ✅ DI registration fixed, but provider implementation needs updates

---

### 2. Manual Key Building Bypasses TenantKeyResolver ❌ CRITICAL

**File**: `S3FileStorageProvider.cs`  
**Method**: `GetFullKey()`

**Current Code** (Lines 795-830):
```csharp
private string GetFullKey(string key)
{
    // Validates ONLY basic path traversal
    if (key.Contains("../") || key.Contains("..\\"))
    {
        throw new ArgumentException("...");
    }

    var parts = new List<string>();
    if (!string.IsNullOrWhiteSpace(_options.KeyPrefix))
    {
        parts.Add(_options.KeyPrefix.TrimEnd('/'));
    }

    // CRITICAL: Manually builds tenant prefix!
    if (_options.EnableTenantIsolation && _tenantContext?.IsResolved == true)
    {
        parts.Add($"tenant-{_tenantContext.TenantId.Value}");  // ❌ WRONG!
    }

    var normalizedKey = NormalizeS3Key(key);
    parts.Add(normalizedKey);
    return string.Join("/", parts);
}
```

**Problems**:
1. Bypasses ALL TenantKeyResolver validations:
   - No URL decoding attack prevention
   - No Unicode lookalike detection  
   - No control character checks
   - No tenant ID format validation
   - No key length validation
   - No audit logging

2. Wrong tenant prefix format: `tenant-{id}` vs `tenants/{id}/`

3. Used in ALL operations: Upload, Download, Delete, Exists, GetPresignedUrl, GetMetadata, List

**Impact**:
- Attackers can bypass Phase 1 security with encoded keys
- Keys don't match expected `tenants/{id}/` format
- No audit trail of rejected attempts

**Required Fix**:
```csharp
// S3FileStorageProvider constructor
public S3FileStorageProvider(
    IOptions<FileStorageOptions> options,
    ILogger<S3FileStorageProvider> logger,
    TenantKeyResolver tenantKeyResolver,  // ← INJECT THIS
    ITenantContext? tenantContext = null)
{
    _tenantKeyResolver = tenantKeyResolver;
    // ...
}

// Replace GetFullKey entirely
private string GetFullKey(string key)
{
    return _tenantKeyResolver.ResolveKey(key);  // ← USE THIS
}
```

**Status**: ❌ NOT FIXED - Requires refactor

---

### 3. Missing Tenant Metadata Stamps ❌ HIGH

**Files**: `S3FileStorageProvider.cs`  
**Methods**: `UploadSinglePartAsync()`, `UploadMultipartAsync()`, `DownloadAsync()`

**Current State**:
- ❌ No `pervaxis-tenant-id` metadata on uploads
- ❌ No metadata verification on downloads

**Required Fix**:

**Upload (Single-Part)**:
```csharp
private async Task UploadSinglePartAsync(...)
{
    var request = new PutObjectRequest
    {
        // ... existing code ...
    };

    // ADD THIS:
    var tenantId = _tenantKeyResolver.GetTenantId();
    request.Metadata.Add(FileStorageConstants.TenantIdMetadataKey, tenantId);

    // ... rest of upload ...
}
```

**Upload (Multipart)**:
```csharp
private async Task UploadMultipartAsync(...)
{
    var request = new TransferUtilityUploadRequest
    {
        // ... existing code ...
    };

    // ADD THIS:
    var tenantId = _tenantKeyResolver.GetTenantId();
    request.Metadata.Add(FileStorageConstants.TenantIdMetadataKey, tenantId);

    // ... rest of upload ...
}
```

**Download**:
```csharp
public async Task<Stream?> DownloadAsync(string key, ...)
{
    // ... get response ...

    // ADD THIS VERIFICATION:
    if (_options.EnableTenantIsolation)
    {
        var expectedTenantId = _tenantKeyResolver.GetTenantId();
        if (response.Metadata.TryGetValue(FileStorageConstants.TenantIdMetadataKey, out var storedTenantId))
        {
            if (storedTenantId != expectedTenantId)
            {
                throw new GenesisException(
                    nameof(S3FileStorageProvider),
                    $"Tenant ID mismatch: expected {expectedTenantId}, found {storedTenantId}",
                    errorCode: FileStorageConstants.FailureReasons.TenantMismatchOnDownload);
            }
        }
        else
        {
            _logger.LogWarning("File {Key} missing tenant metadata stamp", key);
        }
    }

    // ... return stream ...
}
```

**Status**: ❌ NOT FIXED

---

### 4. No Presigned URL Expiry Cap ❌ MEDIUM

**File**: `S3FileStorageProvider.cs`  
**Method**: `GetPresignedUrlAsync()`

**Current Code**:
```csharp
public async Task<string> GetPresignedUrlAsync(
    string key,
    TimeSpan expiry,  // ← No validation!
    CancellationToken cancellationToken = default)
{
    if (expiry <= TimeSpan.Zero)
    {
        throw new ArgumentException("Expiry must be greater than zero.");
    }

    // No upper bound check!
    var request = new GetPreSignedUrlRequest
    {
        Expires = DateTime.UtcNow.Add(expiry)  // ← Can be 7+ days!
    };
    // ...
}
```

**Problem**:
- Callers can create 7-day (or longer) presigned URLs
- Reduces audit trail and increases security risk

**Required Fix**:
```csharp
public async Task<string> GetPresignedUrlAsync(
    string key,
    TimeSpan expiry,
    CancellationToken cancellationToken = default)
{
    if (expiry <= TimeSpan.Zero)
    {
        throw new ArgumentException("Expiry must be greater than zero.", nameof(expiry));
    }

    // ADD THIS:
    var maxExpiry = TimeSpan.FromMinutes(_options.MaxPresignedUrlExpiryMinutes);
    if (expiry > maxExpiry)
    {
        throw new GenesisException(
            nameof(S3FileStorageProvider),
            $"Presigned URL expiry cannot exceed {maxExpiry.TotalHours:F0} hours. Requested: {expiry.TotalHours:F0} hours.",
            errorCode: FileStorageConstants.FailureReasons.PresignedUrlExpiryExceeded);
    }

    // ... rest of method ...
}
```

**Status**: ⚠️ PARTIAL - Options updated, provider check not added

---

### 5. List Operation Isolation Gaps ❌ HIGH

**File**: `S3FileStorageProvider.cs`  
**Method**: `ListAsync()`, `RemoveKeyPrefix()`

**Current Code**:
```csharp
public async Task<IEnumerable<string>> ListAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
{
    var fullPrefix = string.IsNullOrWhiteSpace(prefix)
        ? _options.KeyPrefix  // ❌ Only uses KeyPrefix, not tenant isolation!
        : GetFullKey(prefix);

    // ... list objects ...

    keys.AddRange(response.S3Objects.Select(obj =>
        RemoveKeyPrefix(obj.Key)));  // ❌ Wrong method!

    // ...
}

private string RemoveKeyPrefix(string fullKey)
{
    // ❌ Only strips KeyPrefix, not tenant prefix!
    var prefix = _options.KeyPrefix.TrimEnd('/') + "/";
    return fullKey.StartsWith(prefix) ? fullKey[prefix.Length..] : fullKey;
}
```

**Problems**:
1. Empty prefix uses `KeyPrefix` only, misses tenant isolation
2. `RemoveKeyPrefix` doesn't strip tenant prefix
3. Continuation tokens may leak across tenant boundaries

**Required Fix**:
```csharp
public async Task<IEnumerable<string>> ListAsync(
    string? prefix = null,
    CancellationToken cancellationToken = default)
{
    // FIX: Use TenantKeyResolver for prefix resolution
    var fullPrefix = _tenantKeyResolver.ResolvePrefix(prefix);

    // ... list logic ...

    // FIX: Use TenantKeyResolver to strip prefix
    keys.AddRange(response.S3Objects.Select(obj =>
        _tenantKeyResolver.StripTenantPrefix(obj.Key)));

    // ...
}
```

**Status**: ❌ NOT FIXED

---

### 6. Missing Cross-Tenant Tests ❌ HIGH

**File**: `S3FileStorageProviderTests.cs`

**Current State**:
- 37 provider tests
- 77 TenantKeyResolver tests
- ❌ ZERO cross-tenant isolation tests

**Required Tests**:

1. **Concurrent Access Test**:
```csharp
[Fact]
public async Task ConcurrentTenants_UseSameProviderInstance_IsolationMaintained()
{
    // Create TWO tenant contexts
    var tenant1Context = CreateMockTenantContext("tenant-a");
    var tenant2Context = CreateMockTenantContext("tenant-b");

    // Create TWO resolvers
    var resolver1 = new TenantKeyResolver(tenant1Context.Object, ...);
    var resolver2 = new TenantKeyResolver(tenant2Context.Object, ...);

    // SAME S3 client (simulating singleton)
    var s3Client = new Mock<IAmazonS3>();

    // Create TWO providers with different resolvers
    var provider1 = new S3FileStorageProvider(..., resolver1, tenant1Context.Object);
    var provider2 = new S3FileStorageProvider(..., resolver2, tenant2Context.Object);

    // Upload from both tenants concurrently
    var task1 = provider1.UploadAsync("doc.pdf", stream1);
    var task2 = provider2.UploadAsync("doc.pdf", stream2);

    await Task.WhenAll(task1, task2);

    // Verify keys are different
    task1.Result.Should().Be("tenants/tenant-a/doc.pdf");
    task2.Result.Should().Be("tenants/tenant-b/doc.pdf");
}
```

2. **Cross-Tenant Read Test**:
```csharp
[Fact]
public async Task Download_TenantA_CannotAccessTenantBFile()
{
    // Setup: File exists for Tenant B
    var tenantBFile = "tenants/tenant-b/secret.pdf";
    
    // Act: Tenant A tries to download Tenant B's file
    var tenantAContext = CreateMockTenantContext("tenant-a");
    var provider = CreateProvider(tenantAContext.Object);

    var act = () => provider.DownloadAsync("secret.pdf");

    // Assert: Should fail with tenant mismatch or not found
    await act.Should().ThrowAsync<GenesisException>()
        .Where(ex => ex.ErrorCode == FileStorageConstants.FailureReasons.TenantMismatchOnDownload ||
                    ex.Message.Contains("not found"));
}
```

3. **Cross-Tenant List Test**
4. **Cross-Tenant Delete Test**
5. **Cross-Tenant Metadata Test**

**Status**: ❌ NOT IMPLEMENTED

---

## Additional Issues

### 7. Config Safety - LocalStack in Production

**Status**: ✅ FIXED in FileStorageOptions:
```csharp
if (UseLocalEmulator && Environment == "Production")
{
    throw new GenesisConfigurationException("LocalStack cannot be enabled in Production");
}
```

### 8. Test Count Discrepancy

**Claimed**: 114 tests (77 resolver + 37 provider)  
**Actual**: Need verification - run full test suite to confirm

---

## Recommendations

### Immediate Actions (Block Merge)

1. ❌ **Refactor S3FileStorageProvider**:
   - Inject `TenantKeyResolver` dependency
   - Replace `GetFullKey()` with `_tenantKeyResolver.ResolveKey()`
   - Add tenant metadata stamps on all uploads
   - Verify metadata on downloads
   - Cap presigned URL expiry
   - Fix `ListAsync()` to use `ResolvePrefix()` and `StripTenantPrefix()`

2. ❌ **Add Cross-Tenant Tests**:
   - Minimum 5 new tests covering all isolation boundaries
   - Concurrent access test to prove Scoped registration works

3. ⚠️ **Update PR Description**:
   - Add "APPLICATION-ENFORCED ISOLATION" disclaimer
   - Note that IAM policies are Phase 2
   - List all security assumptions

### Before Production

4. 🔒 **Branch Protection**:
   - Require PR for `main` and `develop`
   - Require 1 approval
   - Require passing CI

5. 📋 **PR Template**:
   - Isolation checklist
   - Test coverage confirmation
   - "App-enforced" acknowledgment

6. 🤖 **CI Integration**:
   - Run all tests on PR
   - Block merge on any test failure
   - Optional: Claude Code review action

---

## Current Partial Fix Status

✅ **Completed**:
- DI registration changed to Scoped
- FileStorageOptions updated with max expiry and production checks

❌ **Not Started**:
- S3FileStorageProvider refactor to use TenantKeyResolver
- Tenant metadata stamps
- Presigned URL expiry enforcement in provider
- List operation fixes
- Cross-tenant tests

---

## Severity Assessment

| Issue | Severity | Exploitability | Impact |
|-------|----------|----------------|--------|
| 1. Singleton context | CRITICAL | High | Total isolation bypass |
| 2. Manual key building | CRITICAL | Medium | Security validation bypass |
| 3. No metadata stamps | HIGH | Low | Defense-in-depth failure |
| 4. No URL expiry cap | MEDIUM | Low | Audit trail degradation |
| 5. List isolation gaps | HIGH | Medium | Cross-tenant data leakage |
| 6. Missing tests | HIGH | N/A | No proof of isolation |

**Overall Risk**: ❌ **UNACCEPTABLE FOR PRODUCTION**

---

## Sign-Off Required

Before merge, the following must be confirmed:

- [ ] All 6 critical issues fixed
- [ ] All tests passing (including new cross-tenant tests)
- [ ] Code review by security-aware engineer
- [ ] Manual testing with two tenants in staging
- [ ] PR description updated with security disclaimers
- [ ] Branch protection enabled

**Reviewer**: ___________________  
**Date**: ___________________  
**Approval**: YES / NO

