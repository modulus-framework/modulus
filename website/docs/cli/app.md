---
sidebar_position: 2
---

# modulus app

Creates a complete modular-monolith application with interactive setup.

## Usage

```bash
modulus app <name> [options]
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `--database` | Database provider | sqlite |
| `--auth` | Authentication provider | none |
| `--message-broker` | Message broker | none |
| `--cache` | Cache provider | inmemory |
| `--storage` | File storage | local |
| `--signalr-backplane` | SignalR backplane | none |
| `--migration-engine` | Migration engine | efcore |

## Interactive Prompts

The CLI prompts for:

1. **Database**: SQLite, SqlServer, PostgreSQL, MySQL
2. **Authentication**: none, openiddict, auth0, authentik, azuread, duende, keycloak, okta
3. **Message Broker**: none, rabbitmq, kafka
4. **Caching**: inmemory, redis
5. **Storage**: local, s3, azureblobs
6. **SignalR Backplane**: none, redis, azure
7. **Migration Engine**: efcore, dbsh
8. **Production Features**: API versioning, rate limiting, health checks, CORS, security headers, idempotency, correlation, secrets guard, PII encryption, feature flags

## Generated Structure

```
MyApp/
├── MyApp.slnx
├── Directory.Build.props
├── .editorconfig
├── .gitignore
├── NuGet.config
└── src/
    ├── API/MyApp.Api/
    │   └── Program.cs
    ├── Shared/
    │   ├── MyApp.Shared.Domain
    │   ├── MyApp.Shared.Application
    │   ├── MyApp.Shared.Infrastructure
    │   └── MyApp.Shared.Presentation
    └── Modules/MyApp.Modules.Catalog/
        ├── .Domain/
        ├── .Application/
        ├── .Infrastructure/
        └── .Presentation/
```

## Examples

```bash
# Basic app with SQLite
modulus app MyApp

# With SQL Server and Keycloak
modulus app MyApp --database sqlserver --auth keycloak

# With RabbitMQ and Redis
modulus app MyApp --message-broker rabbitmq --cache redis
```
