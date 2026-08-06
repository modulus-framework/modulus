# Modulus Sample ERP

A comprehensive sample application built on the **Modulus** modular-monolith framework for .NET 10.

This sample demonstrates a real-world enterprise resource planning (ERP) application with:
- Modular architecture with clean domain boundaries
- Users module with authentication, authorization, and session management
- Generic infrastructure that can be adapted for any business domain
- Production-ready features: caching, rate limiting, security, observability

## Architecture

### Module System

The application uses the **Modulus module system** with a clean separation of concerns:

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
    └── Users/
        ├── ModulusSample.Modules.Users.Domain/
        ├── ModulusSample.Modules.Users.Application/
        ├── ModulusSample.Modules.Users.Infrastructure/
        └── ModulusSample.Modules.Users.Presentation/
```

### Users Module

A generic identity and access management module featuring:

- **Authentication**
  - User registration and email verification
  - Password management
  - Session management with revocation support
  - OIDC/External authentication integration

- **Authorization**
  - Role-based access control (RBAC)
  - Permission-based access control
  - Dynamic permission assignment to roles
  - User role management

- **User Management**
  - Profile management
  - Account activation/suspension
  - Soft delete with GDPR compliance
  - Activity tracking

## Running it

This app needs Postgres, Redis, and RabbitMQ (`docker compose up -d` starts all three).
OIDC is optional — configure `Users:Oidc` to point at any external identity provider
(e.g. Authentik, Keycloak, Auth0); the sample also supports its own password grant flow
without one.

```bash
dotnet restore ModulusSampleErp.slnx
dotnet run --project src/API/ModulusSample.Api -- --migrate   # apply EF Core migrations
dotnet run --project src/API/ModulusSample.Api
```

## Configuration

Update `appsettings.json` with your connection strings and external service settings:

```json
{
  "ConnectionStrings": {
    "Database": "Host=localhost;Port=5432;Database=modulussample;Username=postgres;Password=your_password",
    "Cache": "localhost:6379",
    "RabbitMq": "amqp://guest:guest@localhost:5672/%2f"
  },
  "Users": {
    "Oidc": {
      "IssuerUrl": "https://your-oidc-provider.com",
      "ClientId": "your-client-id"
    }
  }
}
```

## Testing

```bash
dotnet test src/Modules/Users/Tests/ModulusSample.Modules.Users.UnitTests/ModulusSample.Modules.Users.UnitTests.csproj
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

This is designed as a **generic template** that can be adapted for any business domain:

1. **Keep the Users module** as-is for identity and access management
2. **Add business modules** following the same 4-layer pattern
3. **Configure external services** via `appsettings.json`
4. **Extend permissions** for your specific domain requirements
5. **Customize UI** by replacing the API endpoints with your frontend

## License

MIT
