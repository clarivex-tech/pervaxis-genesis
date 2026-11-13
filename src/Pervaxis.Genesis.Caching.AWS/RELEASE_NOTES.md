# Caching tenant isolation release notes

## Unreleased — requires a major version bump (2.0.0)

The provider constructor, DI lifetime, key namespace and validation behavior
change incompatibly. Release this change as 2.0.0 rather than a patch or minor
update. The repository centralizes versions in Directory.Build.props; the shared
release version must be updated before publishing. These notes do not change the
package version themselves.

- **Cold cache:** keys change from `tenant:{tenantId}:{key}` to
  `tenants:{tenantId}:{key}`. Any environment prefix still precedes the tenant
  namespace. No old-key fallback or migration runs. Existing values expire under
  their original TTL; applications must tolerate cache misses and the resulting
  backing-store load during rollout.
- **TTL:** omitted expiry uses `DefaultExpiry`, which defaults to one hour.
  `MaxTtl` defaults to 24 hours. Both must be positive, the default must not
  exceed the maximum, and explicit TTLs outside `0 < expiry <= MaxTtl` throw.
  Set and bulk set include expiry in each Redis SET command.
- **Registration:** provider and resolver are Scoped. The connection multiplexer
  is Singleton and owned by the container. Create a scope in background services;
  do not capture the provider in a singleton. Direct construction requires an
  externally owned connection.
- **Isolation:** all seven operations require a resolved tenant, including empty
  bulk calls. The legacy isolation flag no longer permits unscoped access.
  Changing the tenant after the first call on a provider instance throws.
  Logical keys are validated and preserved instead of being sanitized.
- **Interface:** no counters are exposed, so no counter or counter-TTL API is added.

## Verification and merge gates

Local verification on 2026-11-13: all 14 targeted mutants were killed (two in
each method), then all 116 unmodified caching tests passed in Release mode.
These checks use mocked Redis; no Redis server is needed. The DI test checks
the package's actual lifetime registrations before substituting the mock
connection and verifies separate provider/resolver instances across scopes.

Run the caching test project and its `Check-ProviderMutations.ps1` script. The
script bypasses key resolution and removes the tenant guard separately in each
of the seven methods, requires actual test failures, and restores the source in
a finally block. Run the unmodified suite afterward.

Remote CI must pass for the final pushed commit. An independent person's approval
is required before merge; local tests and mutation results do not replace it.
