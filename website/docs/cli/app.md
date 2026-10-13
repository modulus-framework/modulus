---
sidebar_position: 2
---

# modulus app

Creates a complete modular-monolith application with interactive setup. With `--kind webapp+api` it also creates a web project on the Modulus UI framework.

## Usage

```bash
modulus app <name> [options]
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `-d, --database` | Database provider: `SQLite`, `SqlServer`, `PostgreSQL`, `MySQL` | prompted |
| `--auth` | Auth provider: `none`, `openiddict`, `auth0`, `authentik`, `azuread`, `duende`, `keycloak`, `okta` | prompted |
| `--kind` | App kind: `api` (an API host, no UI) or `webapp+api` (a web project on the Modulus UI framework plus the API) | prompted |
| `--ui-engine` | Web apps only: `blazor`, `mvc` or `razor-pages`. Use `none` for an API. See [the UI overview](../ui/overview). | prompted for web apps |
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
| `--package-source` | Path to a local NuGet feed for the `Synvro.Modulus.*` packages | nuget.org only |
| `-o, --output` | Output directory | current directory |

Omitted options are prompted interactively (pass every flag for non-interactive/CI use).

## Interactive Prompts

The CLI prompts for:

1. **Database**: SQLite, SqlServer, PostgreSQL, MySQL
2. **App kind and UI**: `api`, or `webapp+api` with a UI engine (`blazor`, `mvc` or `razor-pages`)
3. **Authentication**: none, openiddict, auth0, authentik, azuread, duende, keycloak, okta
4. **Message Broker**: none, rabbitmq, kafka
5. **Caching**: inmemory, redis
6. **Storage**: local, s3, azureblobs
7. **SignalR Backplane**: none, redis, azure
8. **Migration Engine**: efcore, dbsh
9. **Production Features**: API versioning, rate limiting, health checks, CORS, security headers, idempotency, correlation, secrets guard, PII encryption, feature flags

## Generated Structure

The structure below is for `--kind api`. A `webapp+api` app adds a web project next to the API:

```
MyApp/src/Web/MyApp.Web/   the UI host for the chosen engine (Blazor components, or MVC / Razor Pages)
```

See [Getting started with the UI](../ui/getting-started) for that project's layout.

### API structure

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
