---
sidebar_position: 2
---

# modulus app

Creates a complete modular-monolith application with interactive setup. The app is an API only: a modular monolith, or microservices with `--bff` and `--services`. It creates no UI.

## Usage

```bash
modulus app <name> [options]
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `-d, --database` | Database provider: `SQLite`, `SqlServer`, `PostgreSQL`, `MySQL` | prompted |
| `--framework` | Target framework: `net10.0`, the only one Modulus supports | `net10.0` |
| `--auth` | Auth provider: `none`, `openiddict`, `auth0`, `authentik`, `casdoor`, `azuread`, `duende`, `keycloak`, `okta` | prompted |
| `--with-example` | Include the example Catalog module | off (no example) |
| `--no-example` | Deprecated: the example is off by default | |
| `--message-broker` | Message broker: `none`, `rabbitmq`, `kafka` | prompted |
| `--caching` | Caching provider: `inmemory`, `redis` | prompted |
| `--storage` | File storage: `local`, `s3`, `azureblobs` | prompted |
| `--s3-provider` | With `--storage s3`: `aws`, `wasabi`, `b2`, `r2` or `minio` | `aws` |
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
| `--migration-engine` | Migration engine: `efcore` (EF Core migrations) or `dbsh` (SQL-first migrations) | prompted |
| `--bff` | Backends for frontends to generate: `web`, `mobile`, `partner` (comma-separated), or `none`. See [BFF](../platform/bff). | none |
| `--services` | Microservices: upstream services the BFFs call besides the API, for example `catalog=http://localhost:5201,orders` | none |
| `--multi-tenancy` | Companies as tenants: membership checks, a tenant store and isolation tests. Needs `--auth openiddict`. See [Security model](../security/overview). | off |
| `--package-source` | Path to a local NuGet feed for the `Synvro.Modulus.*` packages | nuget.org only |
| `-o, --output` | Output directory | current directory |

Omitted options are prompted interactively (pass every flag for non-interactive/CI use).

## Interactive Prompts

The CLI prompts for:

1. **Database**: SQLite, SqlServer, PostgreSQL, MySQL
2. **Authentication**: none, openiddict, auth0, authentik, casdoor, azuread, duende, keycloak, okta
3. **Message Broker**: none, rabbitmq, kafka
4. **Caching**: inmemory, redis
5. **Storage**: local, s3, azureblobs
6. **SignalR Backplane**: none, redis, azure
7. **Migration Engine**: efcore, dbsh
8. **Production Features**: API versioning, rate limiting, health checks, CORS, security headers, idempotency, correlation, secrets guard, PII encryption, feature flags

## Generated Structure

The structure below uses `--with-example`, which adds the sample Catalog module.

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
