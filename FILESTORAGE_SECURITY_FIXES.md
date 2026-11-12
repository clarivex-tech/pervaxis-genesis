# FileStorage Security Fixes - Required Before Merge

## Critical Issues Found

### 1. **Singleton Lifetime with Captured Tenant Context** (HIGHEST RISK)
- **Current**: `S3FileStorageProvider` is registered as Singleton but takes `ITenantContext?` in constructor
- **Risk**: Tenant context captured at singleton creation serves wrong tenant or fails scope validation
- **Fix Options**:
  - **Option A**: Change to Scoped registration (simpler, less performant)
  - **Option B**: Keep Singleton, change to `ITenantScopeFactory` pattern (more complex, better performance)
- **Recommendation**: Option A for simplicity, since FileStorage isn't called as frequently as messaging

### 2. **Manual Key Building Bypasses TenantKeyResolver** (HIGHEST RISK)
- **Current**: `GetFullKey()` manually builds `tenant-{id}/` keys
- **Risk**: Bypasses all security validation in TenantKeyResolver
- **Fix**: Use `TenantKeyResolver.ResolveKey()` for ALL key operations

### 3. **Missing Tenant Metadata Stamps** (HIGH RISK)
- **Current**: No tenant ID metadata on uploads
- **Risk**: Can't verify tenant ownership on downloads
- **Fix**: Add `pervaxis-tenant-id` metadata on all uploads (single & multipart)
- **Fix**: Verify metadata on downloads

### 4. **Presigned URL Expiry Too Long** (MEDIUM RISK)
- **Current**: No cap on expiry, docs suggest 168 hours
- **Risk**: Long-lived URLs reduce audit trail
- **Fix**: Cap at 1 hour default, max 24 hours

### 5. **List Operation Isolation Gaps** (HIGH RISK)
- **Current**: RemoveKeyPrefix uses KeyPrefix, not tenant-aware
- **Risk**: Empty prefix or continuation tokens may leak across tenants
- **Fix**: Use TenantKeyResolver.StripTenantPrefix() for returned keys

### 6. **Missing Cross-Tenant Tests** (HIGH RISK)
- **Current**: No tests for Tenant A accessing Tenant B data
- **Risk**: Isolation not proven
- **Fix**: Add tests for read, list, delete, metadata across tenants

### 7. **LocalStack Config Safety** (MEDIUM RISK)
- **Current**: LocalStack URL/path-style can be set in any environment
- **Risk**: Production could accidentally point to localhost
- **Fix**: Validate UseLocalEmulator is false or throw in production

### 8. **Test Count Discrepancy**
- **Claimed**: 114 tests (77 resolver + 37 provider)
- **Actual**: Need to verify no tests were lost

## Implementation Plan

### Phase 1: Critical Security Fixes (Block Merge)
1. Change DI registration to Scoped
2. TenantKeyResolver integration - remove GetFullKey manual building
3. Add tenant metadata stamps (upload & download verify)
4. Cap presigned URL expiry
5. Fix List isolation (use StripTenantPrefix)
6. Add cross-tenant tests

### Phase 2: Config & Documentation (Before Production)
7. Add LocalStack production validation
8. Update PR description with "app-enforced" disclaimer
9. Branch protection rules
10. PR template with isolation checklist

## Files to Modify

### Core Changes
- `src/Pervaxis.Genesis.FileStorage.AWS/Providers/S3/S3FileStorageProvider.cs`
- `src/Pervaxis.Genesis.FileStorage.AWS/Extensions/FileStorageServiceCollectionExtensions.cs`
- `src/Pervaxis.Genesis.FileStorage.AWS/Options/FileStorageOptions.cs`
- `src/Pervaxis.Genesis.FileStorage.AWS/FileStorageConstants.cs`

### Tests
- `tests/Pervaxis.Genesis.FileStorage.AWS.Tests/S3FileStorageProviderTests.cs`
- New: `tests/Pervaxis.Genesis.FileStorage.AWS.Tests/CrossTenantIsolationTests.cs`
- New: `tests/Pervaxis.Genesis.FileStorage.AWS.Tests/ConcurrentTenantTests.cs`

### Documentation
- Update README.md with security disclaimers
- Add CLAUDE.md isolation checklist
- PR template

## Breaking Changes
- DI registration: Singleton → Scoped (minimal impact, semantically correct)
- Constructor signature changes if using ITenantScopeFactory
