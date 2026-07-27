# Modulus Framework

An enterprise-grade **modular monolith** framework for **.NET 10**, built with an
ABP-style `[DependsOn]` module system, CLI scaffolding, a transactional outbox/inbox,
and first-class multi-tenancy.

## Overview

Modulus is designed for teams who need the architectural rigour of ABP or eShop
without the heavyweight abstractions. It provides proven building blocks that
compose cleanly — pick only what your application needs. The framework ships as
**31 focused NuGet packages** plus a `dotnet tool` CLI for scaffolding complete
solutions, modules, and CRUD code.

## Solution layout

```
src/
  core/          Modulus.Core (abstractions+impl merged), Modulus.AspNetCore
  data/          Modulus.Data.Abstractions, Modulus.EntityFrameworkCore,
                 EF Core providers (SqlServer, PostgreSQL, MySQL, SQLite, MongoDB)
  identity/      Modulus.Identity (OpenIddict server + 6 IdP adapters +
                 EF Core mapping merged into one package)
  messaging/     Modulus.Events (abstractions merged), Modulus.Mediator
                 (abstractions merged), Modulus.Inbox, Modulus.Outbox,
                 Modulus.Outbox.Abstractions (kept — circular-dep seam),
                 Inbox/Outbox.MongoDB, EventBus.RabbitMQ, EventBus.Kafka,
                 Modulus.Sagas (Rebus-based)
  platform/      Modulus.Platform (MultiTenancy + Authorization +
                 BackgroundJobs + Caching + Storage + SignalR merged),
                 Modulus.MultiTenancy.EntityFrameworkCore,
                 Modulus.Authorization.EntityFrameworkCore (durable EF-backed
                 grant/org/entitlement/delegation stores),
                 Modulus.Authorization.Management (admin REST API over them),
                 Modulus.AspNetCore.Redis (shared idempotency store)
  observability/ Modulus.Observability (Diagnostics + OpenTelemetry merged)
  cli/           Modulus.Cli (Spectre.Console.Cli scaffolding tool)
tests/
  unit/          xUnit + NSubstitute + FluentAssertions (13 projects)
  integration/   xUnit + Testcontainers (1 project)
```

## Getting started

### Prerequisites

- .NET SDK **10.0.109** or newer (`dotnet --version`)

### Create a new application

Install the CLI tool and scaffold a complete modular-monolith solution:

```bash
# Pack and install the CLI tool
dotnet pack modulus.slnx -c Release
dotnet tool install -g --add-source ./nupkg Modulus.Cli

# Generate a new application with a Host + example module
modulus app MyApp
```

## CLI commands

The `modulus` CLI (Spectre.Console.Cli + Scriban) is a `dotnet tool` that
generates complete solutions, modules, and CRUD code:

| Command | Description |
|---------|-------------|
| `modulus app <name>` | Creates a modular-monolith solution with Host + example module |
| `modulus module <name>` | Creates a new business module project |
| `modulus add-module <name>` | Adds module to existing app + wires `[DependsOn]` + `ProjectReference` |
| `modulus generate-crud <Entity>` | Generates entity, repo, DTOs, command/query handlers |

Templates are embedded Scriban resources under `src/cli/Modulus.Cli/Templates/`.

## Sample applications

- **`samples/Storefront`** — a runnable app generated with
  `modulus app Storefront --database SQLite`, showing the framework's
  recommended shape end to end: module system, CQRS via `Modulus.Mediator`,
  EF Core persistence with an authored migration, the RFC 7807 error
  contract, and HTTP integration tests via `Modulus.Testing`. See
  [samples/Storefront/README.md](samples/Storefront/README.md) to run it.
- **`samples/cobytemed-erp-app`** — a real, pre-existing ERP application
  retrofitted onto Modulus incrementally (module system, mediator, HTTP
  cross-cutting), while deliberately keeping the messaging/event-sourcing/job
  stack it already had (Rebus, Marten, Quartz) where Modulus has no
  equivalent. Shows what adopting Modulus into an existing, opinionated
  codebase looks like. See
  [samples/cobytemed-erp-app/README.md](samples/cobytemed-erp-app/README.md).

## Module system

Modules implement `IModule` or inherit from `ModulusModule`. Dependencies are
declared via `[DependsOn(typeof(OtherModule))]` attributes (ABP-style).
`AddModulus<TStartupModule>(configuration)` auto-discovers the full module graph
via topological sort.

```csharp
[DependsOn(typeof(IdentityModule), typeof(DataModule))]
public sealed class ShopModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register module-specific services
    }
}
```

```csharp
// Program.cs
builder.Services.AddModulus<AppHostModule>(builder.Configuration);
```

## Features

- **Modular architecture** — ABP-style `[DependsOn]` module system with
  topological-sort discovery and `AddModulus<TStartupModule>()` wiring.
- **CQRS mediator** — `IRequest` / `IRequestHandler` with open-generic pipeline
  behaviors (validation, logging, caching, transaction). No MediatR dependency.
- **Domain-Driven Design** — `AggregateRoot<TId>` with domain-event collection,
  `ValueObject` base, specifications, auditing interfaces.
- **Transactional outbox** — domain events implementing `IIntegrationEvent` are
  enqueued to the outbox *within the same DB transaction* via
  `ModuleDbContext.SaveChangesAsync`. A background `OutboxProcessor` claims rows
  atomically, retries with exponential backoff, and dead-letters after max
  retries.
- **Inbox dedup** — idempotent message processing. All
  `IIntegrationEventHandler<T>` registrations are wrapped with an atomic
  claim-by-EventId decorator (EF Core `EfInboxStore` or MongoDB
  `MongoInboxStore`) so redeliveries don't double-execute.
- **Multi-tenancy** — `ICurrentTenant` backed by `AsyncLocal<TenantInfo?>`
  (flows into background jobs / consumers), resolution from header/claim/subdomain,
  per-tenant connection-string resolver, and soft-delete+tenant query filters on
  `ModuleDbContext`.
- **OpenIddict identity** — password, client-credentials, and refresh-token
  grants with deny-by-default credential validation, scope allow-listing, and
  6 external IdP adapters (Auth0, Authentik, Azure AD, Duende, Keycloak, Okta)
  that validate bearer tokens locally via OIDC discovery (signature, issuer,
  lifetime).
- **EF Core providers** — SQL Server, PostgreSQL, MySQL, SQLite (EF Core 10),
  plus MongoDB for document storage.
- **Event bus** — RabbitMQ (topic exchange, auto-reconnect) and Kafka
  (idempotent producer, consumer groups); all implement `IModuleBus` and
  integrate with the outbox.
- **Sagas** — Rebus-based long-running orchestration.
- **Platform services** — permission-based authorization, background job
  scheduler, caching (memory, tag-based invalidation), file storage (local,
  S3, Azure Blob), and SignalR hub base classes.
- **Observability** — OpenTelemetry auto-instrumentation (ASP.NET Core, EF Core,
  HTTP client) plus correlation-ID propagation.

## Build

```bash
dotnet build modulus.slnx
```

The solution compiles with **0 errors, 0 warnings** (`TreatWarningsAsErrors` is
enabled globally). Central Package Management is used via
`Directory.Packages.props`.

### Common commands

| Task | Command |
|------|---------|
| Build (Debug) | `dotnet build modulus.slnx` |
| Run all tests | `dotnet test modulus.slnx` |
| Run unit tests | `dotnet test modulus.slnx --filter "Category=Unit"` |
| Pack NuGet packages | `dotnet pack modulus.slnx -c Release` |
| Format check | `dotnet format modulus.slnx --verify-no-changes` |

## Target framework

- **.NET 10** (`net10.0`)
- SDK 10.0.109 or later

## License

Apache License 2.0 — see [LICENSE](LICENSE).
