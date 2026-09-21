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
| `-d, --database` | Database provider: `SQLite`, `SqlServer`, `PostgreSQL`, `MySQL` | prompted |
| `--auth` | Auth provider: `none`, `openiddict`, `auth0`, `authentik`, `azuread`, `duende`, `keycloak`, `okta` | prompted |
| `--no-example` | Skip the example Catalog module | prompted (include) |
| `--message-broker` | Message broker: `none`, `rabbitmq`, `kafka` | prompted |
| `--caching` | Caching provider: `inmemory`, `redis` | prompted |
| `--storage` | File storage: `local`, `s3`, `azureblobs` | prompted |
| `--signalr` | SignalR backplane: `none`, `redis`, `azure` | prompted |
| `--enable-api-versioning` | Enable API versioning | `true` |
| `--enable-rate-limiting` | Enable rate limiting | `true` |
| `--enable-health-checks` | Enable health checks | `true` |
| `--enable-feature-flags` | Enable feature flags | `false` |
| `--enable-cors` | Enable CORS | `true` |
| `--enable-security-headers` | Enable security headers | `true` |
| `--enable-idempotency` | Enable HTTP idempotency | `false` |
| `--enable-correlation` | Enable request correlation | `true` |
| `--enable-secrets-guard` | Enable secrets guard | `true` |
| `--enable-personal-data-protection` | Enable personal data protection | `false` |
| `--migration-engine` | Migration engine: `efcore`, `dbsh` | prompted |
| `--package-source` | Path to a local NuGet feed for the `Cobytelabs.Modulus.*` packages | nuget.org only |
| `-o, --output` | Output directory | current directory |

Omitted options are prompted interactively (pass every flag for non-interactive/CI use).

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
modulus app MyApp --message-broker rabbitmq --caching redis
```
