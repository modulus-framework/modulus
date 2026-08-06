# Modulus Framework

An enterprise-grade **modular monolith** framework for **.NET 10**, built with an
ABP-style `[DependsOn]` module system, CLI scaffolding, a transactional outbox/inbox,
and first-class multi-tenancy.

## Overview

Modulus is designed for teams who need the architectural rigour of ABP or eShop
without the heavyweight abstractions. It provides proven building blocks that
compose cleanly — pick only what your application needs. The framework ships as
**23 focused NuGet packages** (published as `Cobytelabs.Modulus.*`) plus a
`dotnet tool` CLI for scaffolding complete solutions, modules, and CRUD code.

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
                 BackgroundJobs + in-memory Caching + local Storage +
                 in-process SignalR — NO heavy cloud SDKs), cloud providers
                 opt-in: Storage.S3, Storage.AzureBlobs, Caching.Redis,
                 SignalR.Backplane, MultiTenancy.EntityFrameworkCore
  observability/ Modulus.Observability (Diagnostics + OpenTelemetry merged)
  cli/           Modulus.Cli (Spectre.Console.Cli scaffolding tool)
tests/
  unit/          xUnit + NSubstitute + FluentAssertions (7 projects)
  integration/   xUnit + Testcontainers (2 projects)
```

## Getting started

### Prerequisites

- .NET SDK **10.0.109** or newer (`dotnet --version`)
- Docker (only for the Testcontainers-based integration tests)

### Create a new application

The `Cobytelabs.Modulus.*` packages aren't on nuget.org yet, so pack the local
feed first, then install the CLI tool and scaffold a complete modular-monolith
solution:

```bash
# Pack and install the CLI tool
dotnet pack modulus.slnx -c Release
dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli

# Generate a new application (SQLite by default) and run it
modulus app MyApp
cd MyApp
dotnet restore
dotnet run --project src/API/MyApp.Api
```

## CLI commands

The `modulus` CLI (Spectre.Console.Cli + Scriban) is a `dotnet tool` that
generates complete solutions, modules, commands, queries, and CRUD code:

| Command | Description |
|---------|-------------|
| `modulus app <name>` | Creates a modular-monolith solution (Host + Shared kernel + example module + tests) |
| `modulus module <name>` | Creates a blank 4-layer business module |
| `modulus add-module <name>` | Adds a module to an existing app + wires `[DependsOn]` + `ProjectReference` |
| `modulus generate-crud <Entity> --module M` | Generates entity, repo, DTOs, command/query handlers, controller |
| `modulus generate-command <Name> --module M` | Generates a single command + handler |
| `modulus generate-query <Name> --module M` | Generates a single query + handler |
| `modulus migrate add <Name>` | Scaffolds an EF Core migration in each module's Infrastructure project |
| `modulus migrate update` | Applies pending migrations to each module's database |

Each generated module uses a **4-layer Clean-Architecture** layout
(`{App}.Modules.{Module}.{Domain,Application,Infrastructure,Presentation}`) with
a per-module DbContext, per-module `IUnitOfWork`, and DTOs/integration events
living under `Application/Dtos` and `Application/IntegrationEvents`.

Templates are embedded Scriban resources under `cli/Templates/`.

## Sample application

- **`samples/ModulusSampleErp`** — a reference application (API host + Users
  module) showing the framework's recommended shape: module system, CQRS via
  `Modulus.Mediator`, per-module EF Core persistence, Serilog, Sentry, and
  forwarded-headers hardening. Ships a `NuGet.config` pointing at the repo's
  local `nupkg/` feed, so it builds straight after `dotnet pack modulus.slnx -c Release`.

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
- **CQRS mediator** — `ICommand<TResult>` / `IQuery<TResult>` with open-generic
  pipeline behaviors (validation, logging, transaction). No MediatR dependency.
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
- **API hardening** — rate limiting, API versioning, health probes
  (`/health/live`, `/health/ready`), CORS, security headers, idempotency keys,
  feature flags, secrets guard, at-rest PII encryption, forwarded-headers.
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
