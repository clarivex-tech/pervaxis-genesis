# ADR-0001 — FileStorage (S3) tenant isolation model

| Field | Value |
|-------|-------|
| **Status** | Proposed |
| **Date** | 2026-11-12 |
| **Authors** | Pervaxis Genesis team |
| **Reviewers** | _Pending human review (PR #53)_ |
| **Section** | Multi-tenancy, FileStorage module |

---

## Context

`Pervaxis.Genesis.FileStorage.AWS` stores every tenant's objects in one shared S3 bucket. Isolation therefore depends entirely on the provider building the right key and refusing objects that are not the caller's.

The provider published in v3.4.0–v3.4.4 had four isolation gaps:

1. It was registered **Singleton**, so it captured the first `ITenantContext` it was built with. Every later request, for any tenant, used that tenant's folder.
2. With an unresolved tenant it **fell back to an unscoped key** instead of failing.
3. Keys were built by hand (`{KeyPrefix}/tenant-{id}/{key}`) with only a basic `../` check.
4. Objects carried `TenantId`/`TenantName` metadata written after caller metadata, so a caller could spoof it, and nothing verified it on read.

---

## Decision

> **We will enforce tenant isolation in the application: every key is built by `TenantKeyResolver` as `{KeyPrefix}tenants/{tenantId}/{key}`, every object is stamped with `pervaxis-tenant-id`, every read verifies the stamp, and anything missing or mismatched is rejected (fail closed).**

---

## Rationale

### Options considered

| Option | Pros | Cons | Decision |
|--------|------|------|----------|
| App-enforced prefix + metadata stamp, fail closed | No infrastructure change; works with one bucket; defense in depth on reads | A bug in the provider, or any code using the S3 client directly, bypasses it | **Selected (Phase 1)** |
| IAM-enforced (per-tenant role/session policy scoped to `tenants/{id}/*`) | Isolation holds even if app code is wrong | Needs STS/session policies per request and infra work | Deferred to Phase 2 |
| Bucket per tenant | Strongest separation | Bucket limits, provisioning cost, operational overhead | Rejected for now |

### Key factors

- Isolation must not depend on callers behaving correctly.
- A wrong answer must be an error, never another tenant's data.
- The change must ship without infrastructure work.

---

## The rules

### Keys
- Built only by `TenantKeyResolver`; no hand-built `tenants/{id}/` anywhere.
- `KeyPrefix` is applied outside the tenant prefix: `{KeyPrefix}tenants/{tenantId}/{key}`. It is infrastructure (for example per environment) and is never returned to callers.
- Path traversal, URL encoding, Unicode lookalikes and control characters are rejected with error codes (`KEY_PATH_TRAVERSAL`, `INVALID_KEY_FORMAT`, ...).
- An unresolved tenant fails every operation with `TENANT_CONTEXT_NOT_RESOLVED` before any S3 call.

### Tenant stamp
- Every upload, single-part and multipart, writes `pervaxis-tenant-id = {tenantId}`.
- Callers may not set `pervaxis-tenant-id` (in any case, with or without `x-amz-meta-`), or `TenantId`/`TenantName` while isolation is on. The upload is **rejected** with `RESERVED_METADATA_KEY`, not silently overwritten, so a bad caller is visible.
- `Download`, `Exists` and `GetMetadata` verify the stamp. S3 returns metadata names lower-cased with an `x-amz-meta-` prefix; the check ignores both.
  - Missing stamp → `TENANT_STAMP_MISSING`
  - Different tenant → `TENANT_MISMATCH_ON_DOWNLOAD`
- `GetMetadata` does not return the stamp to callers.

### List
- The S3 prefix is always `{KeyPrefix}tenants/{tenantId}/{userPrefix}`, including on every continuation page.
- If S3 ever returns a key outside that prefix, the provider throws rather than returning it.

### Presigned URLs
- A presigned URL downloads straight from S3, so **the stamp check does not run**. The tenant-prefixed key is the only protection.
- Default lifetime 60 minutes. `MaxPresignedUrlExpiryMinutes` defaults to 1440 (24 hours) and configuration cannot raise it above 1440.
- A request above the maximum throws `PRESIGNED_URL_EXPIRY_EXCEEDED` and nothing is signed. It is never clamped silently.

### Lifetimes
- `IFileStorage` and `TenantKeyResolver` are **Scoped**. The `IAmazonS3` client is a **keyed Singleton** (`FileStorageConstants.S3ClientServiceKey`) shared by all scopes and never disposed by a scope. It is keyed so another module's unkeyed `IAmazonS3` (TransactionalLogging registers one) cannot override this module's region or emulator settings.
- The TransactionalLogging decorator keeps the Scoped lifetime (tested).
- **Singletons and background services must create a scope** (`IServiceScopeFactory.CreateScope()`) and set the tenant for that scope before resolving `IFileStorage`. Injecting it into a Singleton now fails DI validation, by design.

---

## Existing objects

### What exists
Objects written by v3.4.0–v3.4.4 live at `{KeyPrefix}/tenant-{id}/{key}` and have no `pervaxis-tenant-id` stamp.

### What happens after upgrade
- They are at a different path, so the new provider does not see them: `Download` returns `null`, `Exists` returns `false`, `List` omits them. They are hidden, not exposed and not deleted.
- If an unstamped object is placed under a `tenants/{id}/` path, reads reject it with `TENANT_STAMP_MISSING`.

### Decision: no automatic stamping
Old objects are **not** stamped automatically, and the provider has no "trust the folder" fallback. Neither the folder nor the old metadata reliably says who owns an object:
- Because the old provider was a Singleton, an object may sit under the folder of whichever tenant was captured first, not the tenant that uploaded it.
- The old `TenantId` metadata could be set by the caller.
- With an unresolved tenant the old provider wrote unscoped keys with no tenant at all.

Stamping from the folder name could permanently hand one tenant's file to another.

### If old objects need to be kept
Run a one-off, operator-owned migration, not code in the provider:
1. Inventory `{KeyPrefix}/tenant-*/` and any unscoped keys (S3 Inventory or `ListObjectsV2`).
2. Decide the owner of each object from the **application's own records** (the database row that references the key), not from the S3 path or metadata.
3. Copy to `{KeyPrefix}tenants/{owner}/{key}` with `MetadataDirective = REPLACE`, setting `pervaxis-tenant-id = {owner}` and keeping the remaining user metadata.
4. Verify by reading through the new provider as the owner tenant, update the application's stored key, then delete the old object.
5. Objects whose owner cannot be determined are quarantined for manual review, not migrated.

If no deployment ever ran v3.4.x with real multi-tenant data, no migration is needed. **This has not been confirmed; it is an open question for the owners of each deployment.**

---

## Consequences

### Positive
- A missing or wrong tenant is always an error, never another tenant's data.
- Every operation has a cross-tenant test, and a per-method mutation check (removing the tenant prefix, the stamp write, the stamp check or the reserved-key check) fails at least one test for each.

### Negative
- **Breaking:** `UploadAsync` returns the caller's key, without `KeyPrefix`.
- **Breaking:** invalid keys throw `GenesisException` with an error code instead of `ArgumentException`.
- **Breaking:** Singleton consumers of `IFileStorage` must create a scope.
- **Breaking:** old objects are hidden until migrated (see above).
- `Exists` and `GetMetadata` now throw (instead of returning true or the metadata) for a mismatched or unstamped object.

### Risks
- **Isolation is app-enforced, not IAM-enforced.** Code that uses `IAmazonS3` directly against the bucket, or a bug in the provider, bypasses it. Mitigation: only the provider should use the keyed client; Phase 2 should add IAM session policies scoped to `tenants/{tenantId}/*`.
- Presigned URLs rely on the key prefix alone; keep lifetimes short.

---

## Deferred

- **Copy / Move:** not implemented. When added, both source and destination must go through `TenantKeyResolver`, the stamp must be re-written with `MetadataDirective = REPLACE`, and copying from another tenant must be rejected.
- **Batch delete (`DeleteObjects`):** not implemented. When added, every key must be resolved individually; a batch must never take raw keys.
- **IAM-enforced isolation (Phase 2):** see Risks.

---

## Related

- PR #52 — FileStorage Step 2 (provider)
- PR #53 — tenant isolation security fixes
- `TenantKeyResolver` — key and prefix resolution

---

_Pervaxis Platform · Clarivex Technologies_
