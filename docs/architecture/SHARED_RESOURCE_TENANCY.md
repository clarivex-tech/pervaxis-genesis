# Shared resources and basic tenant isolation

This phase enforces tenant boundaries in the application. The host must resolve a
trusted `ITenantContext` from authenticated execution before using tenant services,
and create a separate DI scope for each tenant request or background operation.

| Package | Shared resource | Tenant boundary |
| --- | --- | --- |
| Caching | Redis cluster | Tenant key prefix |
| FileStorage | S3 bucket | Tenant object-key prefix |
| Search | OpenSearch domain/index | Tenant field, mandatory query filter and tenant document IDs |
| Messaging | SQS/SNS | Tenant message attribute and tenant-scoped processing |
| Notifications | SES identity/SNS | Required tenant, send tags/attributes and direct recipient sends |
| Idempotency | DynamoDB table | Trusted tenant checks and tenant-prefixed record keys |
| TransactionalLogging | DynamoDB table/S3 bucket | Tenant storage keys and constrained lookup requests |
| FeatureFlags | AppConfig/SSM configuration | Scoped evaluation and tenant targeting filters |
| Secrets | AWS Secrets Manager account/client | tenant-{id}/ secret names and resolved-name cache keys |
| Parameters | SSM account/client | /tenant/{id}/ hierarchy, paginated path boundaries and resolved-name cache keys |

Notification tags identify the sender tenant. The host must authorize email
addresses, phone numbers and device tokens for that tenant. Global feature flags
are intentionally available to every resolved tenant unless tenant targeting is set.

TransactionalLogging read/result-mapping placeholders are still unimplemented.
Secrets and Parameters source projects, read contracts and tests have been
reconstructed from surviving API documentation and added to the solution.
The original provider source was not found in fetched branches, reachable history,
local unreachable commit trees or embedded PDB source. Build artifacts prove earlier
implementations existed; their SourceLink commit `6768a7b` does not contain the source.
The reconstruction is not a claim of verbatim recovery or binary compatibility.

Tenant reads require trusted context even for cached values. Platform startup reads
require separate explicit platform provider registration and fixed platform prefixes;
there is no automatic fallback or tenant-call scope switch. Keep these platform
interfaces out of tenant-controlled APIs. See the [Secrets guide](../../src/Pervaxis.Genesis.Secrets.AWS/README.md)
and [Parameters guide](../../src/Pervaxis.Genesis.Parameters.AWS/README.md) for APIs,
configuration and migration requirements. Base supplies common helpers, not an AWS resource.

Workflow, AIAssistance and Reporting remain deferred. OpenSearch ingestion pipelines
and reindexing also remain out of scope. This phase adds no per-tenant IAM policies,
resource provisioning or quotas.
