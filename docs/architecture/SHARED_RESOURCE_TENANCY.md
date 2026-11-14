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

Notification tags identify the sender tenant. The host must authorize email
addresses, phone numbers and device tokens for that tenant. Global feature flags
are intentionally available to every resolved tenant unless tenant targeting is set.

TransactionalLogging read/result-mapping placeholders are still unimplemented.
Parameters and Secrets have no source implementation in this checkout and are not
included as completed packages. Base supplies common helpers, not an AWS resource.

Workflow, AIAssistance and Reporting remain deferred. OpenSearch ingestion pipelines
and reindexing also remain out of scope. This phase adds no per-tenant IAM policies,
resource provisioning or quotas.
