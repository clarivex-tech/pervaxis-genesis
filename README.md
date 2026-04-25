# Pervaxis Genesis

AWS-native provider implementations for the Pervaxis Platform.

## Overview

Pervaxis Genesis provides production-ready AWS service integrations for enterprise applications. All providers are AWS-native, targeting .NET 10, and follow Pervaxis platform standards.

## Packages

| Package | AWS Service | Purpose | Status |
|---------|-------------|---------|--------|
| **Pervaxis.Genesis.Base** | - | Base abstractions and configuration | ✅ |
| **Pervaxis.Genesis.Caching.AWS** | ElastiCache Redis | Distributed caching | ✅ |
| **Pervaxis.Genesis.Messaging.AWS** | SQS + SNS | Message queuing and pub/sub | ✅ |
| **Pervaxis.Genesis.FileStorage.AWS** | S3 | Object storage | ✅ |
| **Pervaxis.Genesis.Search.AWS** | OpenSearch | Full-text search and analytics | ✅ |
| **Pervaxis.Genesis.Notifications.AWS** | SES + SNS | Email and push notifications | ✅ |
| **Pervaxis.Genesis.Workflow.AWS** | Step Functions | Serverless workflow orchestration | ✅ |
| **Pervaxis.Genesis.AIAssistance.AWS** | Bedrock | AI/ML model integration | ✅ |
| **Pervaxis.Genesis.Reporting.AWS** | Metabase | REST API integration for reporting | ✅ |

**Note:** All provider packages use the `.AWS` suffix to support future multi-cloud implementations (e.g., `.Azure`, `.GCP`).

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
dotnet restore Pervaxis.Genesis.slnx

# Build
dotnet build Pervaxis.Genesis.slnx --configuration Release

# Run tests
dotnet test Pervaxis.Genesis.slnx --configuration Release
```

## Installation

Install individual packages as needed:

```bash
dotnet add package Pervaxis.Genesis.Caching.AWS
dotnet add package Pervaxis.Genesis.Messaging.AWS
dotnet add package Pervaxis.Genesis.FileStorage.AWS
dotnet add package Pervaxis.Genesis.Search.AWS
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