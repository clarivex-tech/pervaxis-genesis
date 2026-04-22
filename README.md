# Pervaxis Genesis

AWS-native provider implementations for the Pervaxis Platform.

## Overview

Pervaxis Genesis provides production-ready AWS service integrations for enterprise applications. All providers are AWS-native, targeting .NET 10, and follow Pervaxis platform standards.

## Packages

| Package | AWS Service | Purpose |
|---------|-------------|---------|
| **Pervaxis.Genesis.Base** | - | Template configuration loader and base abstractions |
| **Pervaxis.Genesis.Caching** | ElastiCache Redis | Distributed caching |
| **Pervaxis.Genesis.Messaging** | SQS + SNS | Message queuing and pub/sub |
| **Pervaxis.Genesis.Search** | OpenSearch | Full-text search and analytics |
| **Pervaxis.Genesis.Workflow** | Step Functions | Serverless workflow orchestration |
| **Pervaxis.Genesis.AIAssistance** | Bedrock | AI/ML model integration |
| **Pervaxis.Genesis.FileStorage** | S3 | Object storage |
| **Pervaxis.Genesis.Notifications** | SES + SNS | Email and push notifications |
| **Pervaxis.Genesis.Reporting** | Metabase | REST API integration for reporting |

## Prerequisites

- .NET SDK 10.0.200 or later
- AWS CLI v2
- Docker Desktop (for LocalStack local development)
- Visual Studio 2022 / Rider / VS Code

## Quick Start

```bash
# Clone the repository
git clone https://github.com/clarivex-tech/pervaxis-genesis.git
cd pervaxis-genesis

# Restore dependencies
dotnet restore Pervaxis.Genesis.sln

# Build
dotnet build Pervaxis.Genesis.sln

# Run tests
dotnet test Pervaxis.Genesis.sln
```

## Installation

Install individual packages as needed:

```bash
dotnet add package Pervaxis.Genesis.Caching
dotnet add package Pervaxis.Genesis.Messaging
dotnet add package Pervaxis.Genesis.Search
# ... etc
```

## Documentation

- [Pervaxis Standards](docs/PERVAXIS_STANDARDS.md) - Code standards and conventions
- [Project Structure Guide](docs/PROJECT_STRUCTURE_GUIDE.md) - Project organization patterns
- [Dependency Management](docs/DEPENDENCY_MANAGEMENT.md) - NuGet package guidelines
- [Architecture Decision Records](docs/architecture/) - ADRs for key decisions

## Contributing

1. Read [PERVAXIS_STANDARDS.md](docs/PERVAXIS_STANDARDS.md)
2. Create a feature branch: `git checkout -b feature/GEN-123-description`
3. Follow conventional commits
4. Open a PR against `develop`

## License

Copyright © 2026 Clarivex Technologies Private Limited. All rights reserved.

See [LICENSE](LICENSE) for details.

---

*Pervaxis Platform · Clarivex Technologies · https://clarivex.tech*