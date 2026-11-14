# Pervaxis.Genesis.Secrets

Read contracts for tenant secrets: string, JSON and binary values.
Install [Pervaxis.Genesis.Secrets.AWS](../Pervaxis.Genesis.Secrets.AWS/README.md)
for the Secrets Manager implementation.

ISecretProvider requires a trusted tenant context in the AWS implementation.
IPlatformSecretProvider is a separate explicitly registered service for trusted
platform startup reads. Missing secrets return null; service and parsing errors
propagate. JSON reads of missing values return default(T).
