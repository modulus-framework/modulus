# Modulus Sample ERP

A comprehensive sample application built on the **Modulus** modular-monolith framework for .NET 10.

This sample demonstrates a real-world enterprise resource planning (ERP) application with:
- Modular architecture with clean domain boundaries
- **7 complete business modules**: Identity, Settings, Tenants, Features, VirtualFileExplorer, Notifications, Media (S3/MinIO uploads)
- Production-ready features: caching, rate limiting, security, observability
- 4-layer Clean Architecture per module (Domain → Application → Infrastructure → Presentation)

## Architecture

### Module System

The application uses the **Modulus module system** with a clean 4-layer separation of concerns per module:

Each business module follows this structure:
- **Domain**: Entities, value objects, domain events, repository interfaces
- **Application**: Commands, queries, handlers, DTOs, integration events, `IUnitOfWork`
- **Infrastructure**: `ModuleDbContext`, migrations, repository implementations, module composition root
- **Presentation**: Minimal API endpoints (REPR pattern)

All modules are auto-discovered via `AddModulus<ModulusSampleHostModule>()` and share:
- Multi-tenancy support via `ICurrentTenant`
- User context via `ICurrentUser`
- Domain event dispatching via `DomainEventDispatcher`
- Audit trails via `ModuleDbContext`
- Snake_case naming conventions via `EFCore.NamingConventions`

```
src/
├── API/
│   └── ModulusSample.Api/          # Composition root, middleware, startup
├── Shared/                         # Shared kernel (framework-level utilities)
│   ├── ModulusSample.Shared.Domain/
│   ├── ModulusSample.Shared.Application/
│   ├── ModulusSample.Shared.Infrastructure/
│   └── ModulusSample.Shared.Presentation/
└── Modules/
    ├── Identity/                    # User authentication, authorization, session management
    ├── Settings/                    # Application configuration management
    ├── Tenants/                     # Multi-tenant organization management
    ├── Features/                    # Feature flag management system
    ├── VirtualFileExplorer/         # File and folder management
    └── Notifications/               # User notification system
```

### Business Modules

All modules implement the **4-layer Clean Architecture** pattern with complete separation of concerns:

#### **Identity Module**
- **Authentication**: User registration, email verification, password management
- **Authorization**: RBAC with role-permission mapping, dynamic permission assignment
- **Session Management**: Device tokens, user sessions with revocation support
- **External OIDC**: Authentik, Keycloak, Auth0 integration

#### **Settings Module**
- Application configuration management
- Setting categories (System, User, Tenant)
- Bulk update operations
- Public/private setting visibility

#### **Tenants Module**
- Multi-tenant organization management
- Tenant isolation with audit trails
- Tenant-level configuration

#### **Features Module**
- Feature flag management system
- Percentage rollout and time-window gates
- Feature categorization

#### **VirtualFileExplorer Module**
- File and folder management
- Virtual directory structure
- Upload/download operations

#### **Notifications Module**
- User notification system
- Notification delivery channels
- Notification preferences

## Running it

This app uses a **single shared Postgres database** (per-module schemas), Redis,
and RabbitMQ (`docker compose up -d` starts the full stack **including the API**).
OIDC is optional — configure `Identity:Oidc` to point at any external identity
provider (e.g. Authentik, Keycloak, Auth0); the sample also supports its own
password grant flow without one.

```bash
docker compose up -d                          # infra + API (http://localhost:5016)
docker compose ps                             # wait until all services are healthy
```

To run the API locally instead (needs just the infra services up):

```bash
dotnet restore ModulusSampleErp.slnx
dotnet build ModulusSampleErp.slnx
dotnet run --project src/API/ModulusSample.Api -- --seed   # migrate + seed sample data (incl. users admin/Admin123!)
dotnet run --project src/API/ModulusSample.Api
```

> **Media module & MinIO**: the Media module stores uploads in an S3-compatible
> bucket (`modulussample-uploads`). `docker compose up -d` also starts MinIO
> (http://localhost:9001, minioadmin/minioadmin) and a one-shot init container
> that creates the bucket. See `TESTING.md` for the full walkthrough.

## Configuration

Update the per-module config files (`modules.<module>.json`) with your connection
strings and external service settings. Identity settings live in
`modules.identity.json`, the shared `Database`/`Cache` connection strings in
`appsettings.json`:

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Database": "Host=localhost;Port=5432;Database=ModulusSample;Username=ModulusSample;Password=ModulusSample",
    "Cache": "localhost:6379"
  },
  "Identity": {
    "Oidc": {
      "IssuerUrl": "https://your-oidc-provider.com",
      "ClientId": "your-client-id"
    }
  }
}
```

```json
// modules.settings.json  (each module file carries its own connection string)
{
  "ConnectionStrings": {
    "Settings": "Host=localhost;Port=5432;Database=ModulusSample;Username=ModulusSample;Password=ModulusSample"
  }
}
```

Every module connection string points at the **same** `ModulusSample` database —
modules keep their tables isolated via per-module Postgres schemas, so one
database serves the whole app. The containerized stack overrides these with the
`ConnectionStrings__*` environment variables in `docker-compose.yml`.

## Testing

```bash
# Run all unit tests
dotnet test ModulusSampleErp.slnx --filter "Category=Unit"

# Run specific module tests
dotnet test src/Modules/Identity/ModulusSample.Modules.Identity.UnitTests/ModulusSample.Modules.Identity.UnitTests.csproj
```

## Features

### Modulus Framework Integration

This sample demonstrates integration with Modulus framework packages (all published under the
`Cobytelabs.Modulus.*` NuGet namespace):

- **Module System**: `Cobytelabs.Modulus.Core` for auto-discovery and dependency management
- **Mediator**: `Cobytelabs.Modulus.Core` for command/query handling
- **Data Access**: `Npgsql.EntityFrameworkCore.PostgreSQL` + EF Core, following Modulus's module-per-`DbContext` convention
- **Identity**: `Cobytelabs.Modulus.Identity` for OpenIddict-based auth (password + authorization-code flows)
- **Inbox/Outbox**: `Cobytelabs.Modulus.Inbox` / `Cobytelabs.Modulus.Outbox` for reliable messaging
- **Authorization**: `Cobytelabs.Modulus.Authorization.Management` for RBAC + permission-based access control
- **Multi-tenancy**: `Cobytelabs.Modulus.MultiTenancy.EntityFrameworkCore` for tenant isolation
- **Sagas**: `Cobytelabs.Modulus.Sagas` for long-running process orchestration (bring your own Rebus transport)
- **Observability**: `Cobytelabs.Modulus.Observability` for OpenTelemetry integration
- **API features**: `Cobytelabs.Modulus.AspNetCore` for rate limiting, security headers, idempotency
- **Event Bus**: `Cobytelabs.Modulus.EventBus.RabbitMQ` / `.Kafka` for integration events

Optional, disabled-by-default add-ons (see comments in `Program.cs`):
`Cobytelabs.Modulus.Caching.Redis` (distributed caching), `Cobytelabs.Modulus.SignalR.Backplane`
(multi-node SignalR), `Cobytelabs.Modulus.Storage.AzureBlobs` / `.Storage.S3` (cloud file storage —
local disk storage is used by default).

### Production-Ready Features

- **Security**: CORS, rate limiting, security headers, correlation IDs
- **Observability**: OpenTelemetry with OTLP export, distributed tracing
- **Health Checks**: Liveness and readiness probes
- **Error Handling**: Global exception handler with RFC 7807 Problem Details
- **Idempotency**: Request deduplication for POST/PATCH operations
- **API Documentation**: Swagger/OpenAPI with versioning
- **Event Bus**: RabbitMQ/Kafka support for integration events

## Customization

This is designed as a **modular template** that can be adapted for any business domain:

1. **Use the Identity module** as-is for identity and access management
2. **Add business modules** following the 4-layer pattern (Domain → Application → Infrastructure → Presentation)
3. **Configure external services** via `modules.{modulename}.json` files
4. **Extend permissions** in `Program.cs` for your specific domain requirements
5. **Customize endpoints** by adding new REPR endpoints in Presentation layers
6. **Run migrations** per module: `dotnet ef migrations add {Name} --project {Module}.Infrastructure`

## License

MIT
