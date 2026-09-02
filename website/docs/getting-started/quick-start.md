---
sidebar_position: 2
---

# Quick Start

## 1. Install the CLI

```bash
dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli
```

## 2. Create an Application

```bash
modulus app MyApp
```

The CLI will guide you through interactive setup:

| Prompt | Options | Default |
|--------|---------|---------|
| Database | SQLite, SqlServer, PostgreSQL, MySQL | SQLite |
| Authentication | none, openiddict, auth0, authentik, azuread, duende, keycloak, okta | none |
| Message Broker | none, rabbitmq, kafka | none |
| Caching | inmemory, redis | inmemory |
| Storage | local, s3, azureblobs | local |
| SignalR Backplane | none, redis, azure | none |
| Migration Engine | efcore, dbsh | efcore |

Plus production hardening features: API versioning, rate limiting, health checks, CORS, security headers, idempotency, correlation, secrets guard, personal data protection, feature flags.

## 3. Explore the Generated Structure

```
MyApp/
├── MyApp.slnx
├── Directory.Build.props
├── .editorconfig
├── .gitignore
└── src/
    ├── API/MyApp.Api/                    # Host (composition root)
    │   └── Program.cs
    ├── Shared/                           # Shared kernel (4 projects)
    │   ├── MyApp.Shared.Domain
    │   ├── MyApp.Shared.Application
    │   ├── MyApp.Shared.Infrastructure
    │   └── MyApp.Shared.Presentation
    └── Modules/                          # Business modules
        └── MyApp.Modules.Catalog/
            ├── .Domain/
            ├── .Application/
            ├── .Infrastructure/
            └── .Presentation/
```

## 4. Build and Run

```bash
cd MyApp/src/API/MyApp.Api
dotnet run
```

## 5. Verify

```bash
# Health check
curl http://localhost:5000/health/live

# OpenAPI spec
curl http://localhost:5000/openapi/v1.json
```

## Next Steps

- [First Module](first-module) — Add your first business module
- [CLI Reference](/docs/cli/) — Explore all CLI commands
