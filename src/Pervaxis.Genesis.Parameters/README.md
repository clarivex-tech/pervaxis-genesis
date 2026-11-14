# Pervaxis.Genesis.Parameters

Read contracts for tenant configuration: string, JSON and hierarchy reads.
Install [Pervaxis.Genesis.Parameters.AWS](../Pervaxis.Genesis.Parameters.AWS/README.md)
for the Systems Manager Parameter Store implementation.

IParameterProvider requires a trusted tenant context in the AWS implementation.
IPlatformParameterProvider is a separate explicitly registered service for trusted
platform startup reads. Missing single values return null; JSON reads return
default(T). Service and parsing errors propagate.
