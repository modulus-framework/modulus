# AGENTS.md

Guidance for AI agents (and humans) working on the Modulus framework.

## Project

Modulus is a modular-monolith framework for **.NET 10** (`net10.0`). It is a
multi-project solution made of **31 libraries** under `src/` (core, data,
identity, messaging, platform, observability, testing) plus the optional,
server-rendered **UI framework** under `src/ui/` (see *Modulus.UI* below),
unit/integration tests under `tests/` and a **CLI tool** (`Modulus.Cli`) for
scaffolding.

## Prerequisites

- .NET SDK **10.0.109** or newer (`dotnet --version`)
- Docker (only for the Testcontainers-based integration tests under
  `tests/integration/`)

## Common commands

All commands are run from the repository root (`E:\Personal\framework\modulus`).

| Task | Command |
|------|---------|
| Restore + build (Debug) | `dotnet build modulus.slnx` |
| Build (Release) | `dotnet build modulus.slnx -c Release` |
| Run all tests | `dotnet test modulus.slnx` |
| Run only unit tests | `dotnet test modulus.slnx --filter "Category=Unit"` |
| Run only integration tests | `dotnet test modulus.slnx --filter "Category=Integration"` |
| Pack NuGet packages | `dotnet pack modulus.slnx -c Release` |
| Install CLI tool | `dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli` |
| Create new app | `modulus app MyApp` |
| Add a module | `modulus add-module Catalog` |
| Generate CRUD | `modulus generate-crud Product --module Catalog` |
| Audit vulnerable packages | `dotnet list modulus.slnx package --vulnerable` |
| Format check (no writes) | `dotnet format modulus.slnx --verify-no-changes` |
| Format (apply) | `dotnet format modulus.slnx` |
| Check for updates | `modulus outdated` |
| Update packages | `modulus update` |

## Build conventions

- `TreatWarningsAsErrors` is **enabled globally** (`Directory.Build.props`).
  Any new warning fails the build — fix the root cause rather than suppressing.
- Central Package Management is on. Add/upgrade packages in
  `Directory.Packages.props` (set `<PackageVersion>`), then reference them in
  the project with a versionless `<PackageReference Include="..." />`.
- `Nullable` and `ImplicitUsings` are enabled everywhere. Prefer `NotNullWhen`
  / `throw new ArgumentNullException` over null-forgiving `!`.
- Source Link + deterministic builds are configured for Release/CI.

## Before submitting changes

1. `dotnet build modulus.slnx` — must compile with **0 warnings, 0 errors**.
2. `dotnet test modulus.slnx --filter "Category=Unit"` — unit tests must pass
   (integration tests need Docker).
3. `dotnet format modulus.slnx --verify-no-changes` — formatting must be clean.
4. Do not commit secrets, connection strings, or `bin/`/`obj/`/`*.user` files.
5. Do not commit unless explicitly asked.

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
                 Modulus.Sagas (Rebus saga orchestration)
  platform/      Modulus.Platform (MultiTenancy + Authorization +
                 BackgroundJobs + in-memory Caching + local Storage +
                 in-process SignalR — NO heavy cloud SDKs). Cloud providers are
                 separate packages so their SDKs are opt-in:
                 Modulus.Storage.S3 (AWSSDK.S3), Modulus.Storage.AzureBlobs
                 (Azure.Storage.Blobs), Modulus.Caching.Redis (StackExchange.Redis),
                 Modulus.SignalR.Backplane (Redis/Azure SignalR backplane),
                 Modulus.MultiTenancy.EntityFrameworkCore (EF-backed ITenantStore
                 — keeps EF Core out of Modulus.Platform),
                 Modulus.Authorization.EntityFrameworkCore (EF permission grant
                 store) + Modulus.Authorization.Management (admin API),
                 Modulus.AspNetCore.Redis (distributed idempotency store).
  observability/ Modulus.Observability (Diagnostics + OpenTelemetry merged)
  testing/       Modulus.Testing (WebApplicationFactory harness, RecordingModuleBus,
                 event assertions), Modulus.Testing.Architecture (module boundary
                 rules: integration event naming, cross-module reference detection)
  ui/            Modulus.UI.Theme.Abstractions (ITheme contracts), Modulus.UI.Core
                 (HTMX model, contributors, view resolver), Modulus.UI.Theme.Tabler
                 (reference theme), + feature UIs: Identity, Users, Permissions,
                 Tenancy, Settings, AuditLogging, Notifications, Files
  cli/           Modulus.Cli (Spectre.Console.Cli scaffolding tool)
tests/
  unit/          xUnit + NSubstitute + FluentAssertions
  integration/   xUnit + Testcontainers (Docker required)
```

## Module system

Modules implement `IModule` or inherit from `ModulusModule` and are
registered **explicitly** in `Program.cs` — there is no dependency-attribute
discovery and no startup module. `AddModulus(configuration, configure)`
invokes a `ModulusBuilder` callback where each module is added in order;
**registration order is authoritative** for every lifecycle phase.

```csharp
public sealed class ShopModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection s, IConfiguration c)
    {
        // Register module-specific services
    }
}

// Program.cs
builder.Services.AddModulus(builder.Configuration, modules => modules
    .AddModule<IdentityModule>()
    .AddModule<DataModule>()
    .AddModule<ShopModule>());
```

Service registration runs in three ordered phases across all modules (each in
registration order): `PreConfigureServices` for every module, then
`ConfigureServices` for every module, then `PostConfigureServices` for every
module. Use `PreConfigureServices` to seed shared options/registries other
modules contribute to, and `PostConfigureServices` to finalize once every module
has registered (freeze registries, build consolidated maps). All three are
optional no-op virtuals on `ModulusModule` (default interface methods on
`IModule`). After the host builds, `InitializeAsync` runs per module (migrations,
seeding) and `ShutdownAsync` runs in reverse order on graceful shutdown.

## CLI tool

The `modulus` CLI (Spectre.Console.Cli + Scriban) is a `dotnet tool` that
generates complete solutions, modules, and CRUD code using a **4-layer
Clean-Architecture / modular-monolith** layout (Domain → Application →
Infrastructure → Presentation per module), mirroring the structure used by
large DDD/CQRS modular monoliths.

| Command | Description |
|---------|-------------|
| `modulus app <name>` | Creates a solution: `src/API/{App}.Api` host + `src/Shared/{App}.Shared.*` kernel (4 projects) + example `Catalog` module (4 projects) + top-level tests. `--migration-engine dbsh` uses SQL-first migrations. `--kind api\|web` picks the **app kind** (see below). |
| `modulus module <name>` | Creates a blank 4-layer business module |
| `modulus add-module <name>` | Adds a module to an existing app + wires Program.cs registration + Host `ProjectReference`s. `--migration-engine` defaults to `dbsh` when all existing modules use dbsh. |
| `modulus generate-crud <Entity>` | Generates entity, repo, DTOs, command/query handlers across the module's layers |
| `modulus migrate add <Name>` | Scaffolds a migration in each module's Infrastructure project (or one via `--module`). Auto-detects engine: `dotnet ef` for EF Core modules, `dbsh create` for dbsh modules. |
| `modulus migrate update` | Applies pending migrations to each module's database. Auto-detects engine: `dotnet ef database update` for EF Core, `dbsh init && dbsh migrate` for dbsh modules. |
| `modulus doctor` | Checks environment health: .NET SDK, `dotnet-ef`, `dbsh` tool availability, dbsh config presence, CLI tool version, framework package versions. |
| `modulus outdated` | Shows all packages with newer versions available on NuGet. `--framework-only` to check only `Cobytelabs.Modulus.*` packages. |
| `modulus update` | Updates packages to latest versions with backup and rollback support. `--dry-run` to preview changes, `--force` to skip prompts. |

### Generated layout

```
{App}/
├── {App}.slnx
├── Directory.Build.props        # net10.0, Nullable, CPM off (explicit Versions)
├── .editorconfig
└── src/
    ├── API/{App}.Api/                        # composition root (single executable)
    │   ├── Program.cs                        # AddModulus(callback), AddMediator; MigrateModulusDatabasesAsync over all DbContexts
    ├── Shared/                               # shared kernel (4 projects)
    │   ├── {App}.Shared.Domain
    │   ├── {App}.Shared.Application
    │   ├── {App}.Shared.Infrastructure
    │   └── {App}.Shared.Presentation
    └── Modules/                              # feature modules (4 projects each)
        └── {App}.Modules.{Module}/
            ├── .Domain/          # entity, IRepository
            ├── .Application/     # IUnitOfWork, commands/queries/handlers + Dtos/ + IntegrationEvents/
            ├── .Infrastructure/  # {Module}DbContext, {Module}DbContextFactory, repository impl, {Module}Module composition root
            └── .Presentation/    # API controllers
```

Each module gets **four** projects named `{App}.Modules.{Module}.{Layer}`:

| Layer | Contains | References |
|-------|----------|------------|
| `Domain` | Entity, `IRepository` | `Shared.Domain`, `Modulus.Core`, `Modulus.Data.Abstractions` |
| `Application` | **`IUnitOfWork`**, commands, queries, handlers, **DTOs** (`Dtos/`), **integration events** (`IntegrationEvents/`) | `Domain`, `Shared.Application`, `Modulus.Mediator`, `Modulus.EntityFrameworkCore`, `Modulus.Events` |
| `Infrastructure` | **`{Module}DbContext`**, **`{Module}DbContextFactory`**, repository impl, `{Module}Module` composition root | `Application`, `Domain`, `Shared.Infrastructure`, `Modulus.EntityFrameworkCore`, `Modulus.Events`, EF Core provider package |
| `Presentation` | API controllers | `Application`, `Shared.Presentation`, `Modulus.AspNetCore` |

There are **no separate Contracts / IntegrationEvents / Tests projects** —
DTOs live under `Application/Dtos` and integration events under
`Application/IntegrationEvents`; tests live in one top-level
`tests/{App}.Tests` project. This collapses the previous 7-layer design to 4.

**Per-module DbContext + per-module IUnitOfWork.** Each module owns its
`{Module}DbContext : ModuleDbContext` (in `Infrastructure`) with its own
`TablePrefix` and connection string, and defines its own `IUnitOfWork`
interface (`SaveChangesAsync`) in `Application`. The `{Module}Module`
composition root calls `AddModuleDatabase<{Module}DbContext>` (registers the
context + generic `IRepository<>`) and binds the module's `IUnitOfWork` to the
context via `AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<{Module}DbContext>())`.
Handlers inject the module's `IUnitOfWork` and call `SaveChangesAsync`.

The host owns **no** `DbContext`. `Program.cs` calls
`await app.Services.MigrateModulusDatabasesAsync()`, which resolves every
`IEnumerable<DbContext>` (populated by each module's `AddModuleDatabase`) and,
per module, applies EF Core migrations when any exist — otherwise it falls back
to `EnsureCreated` (see **EF Core migrations** below). Each module gets its own
connection string key in `appsettings.json` (e.g. `"Catalog"`, `"Orders"`).
The framework's
`AddModuleDatabase<TContext>` no longer registers `IUnitOfWork` (the module
does), and `EfRepository<T>` routes each entity to the correct context via the
registration-time `IEntityContextMap` — built once (a singleton reads each
registered context's metadata model, no DB hit) so a repository resolves *only*
the owning context instead of instantiating every module context to scan it. It
falls back to a runtime `GetServices<DbContext>()` scan for contexts registered
outside `AddModuleDatabase`.

When you `generate-crud` a new entity, the CLI auto-inserts the `DbSet<T>`
property + Domain `using` into the module's `{Module}DbContext.cs`.

The `{Module}Module` composition root lives in `Infrastructure` and registers
its DbContext + `IUnitOfWork` + repository + `services.AddMediatorHandlers(...)`.
The host calls `AddMediator()` once (pipeline behaviours only); each module
contributes its own handlers without re-registering behaviours. The generated
`.slnx` uses flat sibling solution folders (no nesting) so that `dotnet test`
and `dotnet sln list` discover all projects.

A working example lives at `samples/TradeFlow` (API host + Users module,
SQLite). Because the `Cobytelabs.Modulus.*` packages aren't on nuget.org yet, the
sample ships a `NuGet.config` pointing at the repo's local `nupkg/` feed — run
`dotnet pack modulus.slnx -c Release` first if the feed is empty.

Templates are embedded Scriban resources under `cli/Templates/`
(`app/`, `shared/`, `module/{Domain,Application,Infrastructure,Presentation}/`).

## Known review findings (high-impact, not yet fixed)

These are architectural defects that need design discussion before fixing. See
the review notes and do not silently change them:

- **Transactional outbox (dual-write)** — FIXED: `ModuleDbContext.SaveChangesAsync` now enqueues domain events that implement `IIntegrationEvent` directly into its own `Set<OutboxMessage>()` BEFORE calling `base.SaveChangesAsync`, so the outbox row(s) participate in the same DB transaction. Enqueue is gated on `IOutboxWriter` being registered (i.e. the app called `AddOutbox<TContext>`); rows are built through the shared `OutboxRowFactory` (Modulus.Outbox.Abstractions) so all call sites produce identical rows, including `CorrelationId` (captured from `ICorrelationContext`). The former `IIntegrationEventOutbox`/`NullIntegrationEventOutbox` seam was removed — it was dead code whose registration dance never executed at runtime (the context writes inline), and it duplicated row-building with a silent CorrelationId divergence.
- **Inbox (MongoDB)** — FIXED: `AddMongoInbox` now calls `DecorateIntegrationEventHandlers()` (the same decorator wiring as `AddInbox<TContext>`), so all `IIntegrationEventHandler<T>` registrations are wrapped with the idempotent decorator backed by `MongoInboxStore`. The original bug: `MongoInboxStore` was registered but the handler pipeline was never decorated — `AddMongoInbox` provided zero dedup. A common `IInboxStore` interface now backs both EF Core (`EfInboxStore`) and MongoDB (`MongoInboxStore`), so the decorator logic is shared.
- **Inbox (EF Core model mapping)** — FIXED: `AddInbox<TContext>` now registers an `IModuleModelContributor` that maps `InboxMessage` into every `ModuleDbContext` (applied in `OnModelCreating` before table-prefixing). The original bug: `InboxMessageConfiguration` existed but nothing applied it — the EF inbox could not persist claims at all without undocumented hand-wiring.
- **`TransactionBehavior` enlists only the first registered `DbContext`** — FIXED: now starts an explicit `BeginTransactionAsync` on *every* resolved `DbContext` (see below). The original bug was actually worse than "first context only": `AddDbContext<T>` does not register the context as `DbContext`, so `GetServices<DbContext>()` returned **zero** items and the behavior silently skipped transaction wrapping entirely. `AddModuleDatabase<TContext>` now also registers the context as `DbContext` so the behavior can discover it. Multi-context caveat: each context runs in its own independent DB transaction (true cross-connection atomicity needs 2PC/MSDTC); for cross-module consistency prefer the transactional outbox.
- 5 empty test projects removed; `Modulus.App` template replaced by CLI
  (`modulus app`).

## Production-hardening helpers (Tier 1)

Added to `Modulus.AspNetCore` as opt-in, config-bound helpers. The `modulus app`
template wires all of them in `Program.cs` and seeds default sections in
`appsettings.json`; a freshly generated app builds and boots clean (validated
end-to-end: `/health/live` + security headers verified over HTTP).

- **Rate limiting** (`RateLimiting/`) — `AddModulusRateLimiting(config)` +
  `UseModulusRateLimiting()`. Built-in fixed-window limiter partitioned by
  `User` / `Tenant` / `IP` / `Global` (`RateLimiting` section). Per-user
  partition resolves `ICurrentUser`, falls back to IP for anonymous.
- **API versioning** (`Versioning/`) — `AddModulusApiVersioning(config)` wires
  `Asp.Versioning` for real (query / header / URL-segment readers + ApiExplorer).
  Previously `AddModulusEndpoints` *claimed* to configure versioning in its doc
  comment but only registered validators; comment corrected.
- **Health probes** (`HealthChecks/`) — `MapModulusHealthChecks()` exposes
  `/health/live` (liveness, no dependency I/O) and `/health/ready` (aggregates
  `IModuleHealthCheck`; 503 when any is `Unhealthy`, 200 for `Degraded`). This is
  separate from Observability's existing `/health/modules` aggregator.
- **CORS** (`Cors/`) — `AddModulusCors(config)` + `UseModulusCors()`; single
  named policy, wildcard-subdomain aware, never combines `*` origin with
  credentials.
- **Security headers** (`Security/`) — `UseModulusSecurityHeaders()`: HSTS
  (HTTPS-only), `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`,
  optional CSP / Permissions-Policy, `Server` header stripping.
- **Options validation** (`Configuration/`) — `AddValidatedOptions<T>()` binds +
  `ValidateDataAnnotations` + `ValidateOnStart` so misconfiguration fails fast at
  boot.
- **`Microsoft.OpenApi` advisory pin** — `Microsoft.AspNetCore.OpenApi 10.0.9`
  floats to `Microsoft.OpenApi 2.0.0` (high-severity GHSA-v5pm-xwqc-g5wc), which
  broke the build under `TreatWarningsAsErrors`. Pinned `2.9.0` in
  `Directory.Packages.props` and in the generated `api.csproj` template.
- **`TransactionBehavior` × `EnableRetryOnFailure` incompatibility** — FIXED:
  the behavior called `BeginTransactionAsync` directly, which throws *"the
  configured execution strategy does not support user-initiated transactions"*
  under a retrying provider (all relational providers enable
  `EnableRetryOnFailure(3)`), so write commands crashed at runtime. Now drives
  the whole unit through `contexts[0].Database.CreateExecutionStrategy().ExecuteAsync(...)`
  (EF-mandated pattern; a passthrough when retry is off, e.g. SQLite). Handler
  bodies must be safe to re-run on a transient-failure retry.

## EF Core migrations (per-module)

Replaces the old `EnsureCreated`-only startup path. Each module owns its own
migrations in its `Infrastructure` project (matching the per-module DbContext
design), so modules stay independently deployable. Validated end-to-end:
`modulus migrate add` → `modulus migrate update` → app boots logging *"Applied
migrations for CatalogDbContext"*.

- **Runtime helper** — `Modulus.EntityFrameworkCore.Extensions.MigrateModulusDatabasesAsync(this IServiceProvider, DatabaseInitializationMode = MigrateOrCreate, ct)`.
  Resolves every `DbContext`, and per module runs (each driven through its own
  execution strategy for connection resilience):
  - `MigrateOrCreate` (default) — `Migrate()` when the context has migrations,
    else `EnsureCreated()`. Lets a freshly generated app boot before any
    migration is authored, then switch to migrations automatically once one exists.
    Do **not** mix the two on one DB in production (`EnsureCreated` writes no
    migrations-history table).
  - `Migrate` — always applies migrations; throws if none. Use in production.
  - `EnsureCreated` — snapshot only; prototyping/tests.
- **Design-time factory** — the framework exposes `Modulus.EntityFrameworkCore.Design.DesignTimeContext`
  (stub `ICurrentTenant`/`ICurrentUser`/`DomainEventDispatcher`/`IServiceProvider`
  — migrations only build the model, never touch live request state). The module
  template emits `{Module}DbContextFactory : IDesignTimeDbContextFactory<{Module}DbContext>`
  so `dotnet ef` can construct the context without the app's DI container. The
  connection string comes from the `{MODULE}_CONNECTION` env var (for CI/CD),
  falling back to the module's design-time default.
- **CLI** — `modulus migrate add <Name> [--module M]` scaffolds a migration in
  every module's Infrastructure project (or one module) via `dotnet ef migrations add … --output-dir Migrations`;
  `modulus migrate update [--module M]` runs `dotnet ef database update` per module.
  Discovers the `*.Api.csproj` startup project and `*.Infrastructure.csproj`
  module projects under the app root. Requires the `dotnet-ef` global tool.
- **Per-tenant fan-out** — `Modulus.MultiTenancy.EntityFrameworkCore.TenantDatabaseMigrationExtensions.MigrateModulusDatabasesForTenantsAsync(mode, ct)`
  migrates the host database first, then enumerates `ITenantStore.ListAsync()`
  (default returns empty; `EfTenantStore` lists active tenants in slug order)
  and re-runs the migration inside each tenant's `Change(tenant)` scope so
  tenant-aware connection resolvers target each tenant database. Call from a
  dedicated migrator job / init container, not every replica.

## EF Core migrations engine

All modules use **EF Core migrations** by default. Each module includes an
`IDesignTimeDbContextFactory` implementation and EF migration files in
`Infrastructure/Migrations/`. `MigrateModulusDatabasesAsync` applies them at
startup via `dotnet ef database update` routed per module. The CLI provides
`modulus migrate add <Name>` and `modulus migrate update` commands to scaffold
and apply migrations respectively.

**dbsh** is the alternative engine. When `--migration-engine dbsh` is passed
to `modulus app` or `modulus add-module`, the generated module gets:

- `Database/Config/migration.json` — dbsh config (provider, `${VAR}` connection)
- `Database/Config/local.json` — dev environment override
- `Database/Migrations/{Module}/` — empty dir for hand-written `.sql` files
- `.ExternallyManaged<TContext>()` in the module composition root — tells
  `MigrateModulusDatabasesAsync` to skip this context

The CLI detects dbsh modules by the `Database/Config/migration.json` marker
file. `modulus migrate add` runs `dbsh create` instead of
`dotnet ef migrations add`; `modulus migrate update` runs `dbsh init && dbsh
migrate` instead of `dotnet ef database update`. When `--migration-engine` is
omitted, the CLI defaults to `dbsh` when every existing module already uses
dbsh, otherwise `efcore`.

Runtime: `MigrateModulusDatabasesAsync` skips contexts registered via
`ExternallyManaged<TContext>()` — the schema is applied by the dbsh tool,
not by EF Core. The `modulus doctor` command checks `dbsh --version` when
any dbsh module is present.

## Microservice hardening (Tier 2)

Cross-service concerns for the microservice deployment style: request
correlation and resilient outbound HTTP. The `modulus app` template wires
correlation into `Program.cs` + `appsettings.json`; the resilient HTTP client is
opt-in (add a `Modulus.Platform` reference when a service makes outbound calls).
Validated end-to-end: a generated app echoes `X-Correlation-ID` — adopting a
caller-supplied id, or deriving one from the request trace id when absent.

- **Correlation context** (`Modulus.Core`) — `ICorrelationContext` (abstraction)
  + `CorrelationContext` (AsyncLocal, singleton) + `CorrelationHeaders.Default`
  (`X-Correlation-ID`). AsyncLocal so the id flows into background scopes and
  message consumers (`using var _ = correlation.BeginScope(id)`), mirroring
  `CurrentTenant`. Registered **singleton** so the pooled outbound handler can
  depend on it.
- **Inbound middleware** (`Modulus.AspNetCore`) — `AddModulusCorrelation(config)`
  + `UseModulusCorrelation()` (place first in the pipeline). Adopts the inbound
  header or derives an id (trace id, else GUID), pushes it into
  `ICorrelationContext`, tags `Activity.Current` with `correlation.id`, and echoes
  it on the response. Config section `Correlation`
  (`HeaderName`/`IncludeInResponse`/`UseTraceIdWhenMissing`).
- **Outbound propagation** (`Modulus.Core`) — `CorrelationIdPropagationHandler`
  (a `DelegatingHandler`) copies the current id onto outgoing requests (never
  overwriting a caller-set header). W3C `traceparent` is already auto-injected by
  `HttpClient` when an `Activity` is current, so this carries only the *business*
  correlation id, which survives even when tracing is off.
- **Resilient HTTP client** (`Modulus.Platform`) — `services.AddModulusHttpClient(name)`
  and `AddModulusHttpClient<TClient>()` return an `IHttpClientBuilder` wired with
  the .NET **standard resilience handler** (`AddStandardResilienceHandler`: retry
  w/ jittered back-off, circuit breaker, total + per-attempt timeout, concurrency
  limiter) plus the correlation handler as the outer handler. `TryAddSingleton`s
  the correlation context so it works even without the inbound middleware (then
  no-ops). New package: `Microsoft.Extensions.Http.Resilience` 10.7.0.

## Sagas (Rebus) — as-built decision

`Modulus.Sagas` is the **opt-in saga/process-manager package** built on Rebus.
It deliberately wraps (not replaces) Rebus's native `Saga<TData>` model — sagas
are authored with standard Rebus APIs; the package wires them into Modulus:

- **`AddModulusSagas(b => b ...)`** composes: MS logging, an optional Polly v8
  retry/circuit-breaker incoming-pipeline step (`PollyRetryStep`), ambient
  tenant/correlation propagation (`AmbientContextIncomingStep`), handler
  auto-registration from chosen assemblies, and adapters that bridge existing
  `IIntegrationEventHandler<T>` registrations into Rebus's
  `IHandleMessages<T>` pipeline (so inbox-decorated handlers work unchanged).
- **Ambient context on messages**: publishers stamp `mod-tenant-id` /
  `mod-correlation-id` headers (`RebusModuleBus`, `RebusOutboxDispatcher`);
  consumers restore them around handler invocation so tenant query filters and
  log correlation behave like the HTTP path.
- **`.ReplaceModuleBus()` / `.ReplaceOutboxDispatcher()`** route
  `IModuleBus.PublishAsync` and outbox relaying through Rebus instead of the
  in-process bus / dispatcher. Both are opt-in.
- Transport + saga persistence are bring-your-own in the `.Rebus(...)`
  callback (`cfg.Transport(...)`, `cfg.Options(o => o.EnableSagas())` +
  `cfg.Sagas(...)`). Covered by `tests/unit/Modulus.Sagas.Tests`.

## HTTP endpoint styles — both supported by design

Modulus ships **two endpoint authoring styles**; apps choose per module or per
endpoint (both compile to ordinary ASP.NET Core routes):

1. **Minimal API style** — implement `IEndpoint`/`IMinimalEndpoint`
   (`src/core/Modulus.AspNetCore/Endpoints/`), register via
   `services.AddEndpoints(assembly)`, map via `app.MapEndpoints()`.
2. **REPR pattern** — inherit `Endpoint<TRequest,TResponse>` with declarative
   `Configure()` metadata, map via `app.MapModulusEndpoints()`.

Framework-internal diagnostics endpoints (`/health/modules`,
`/health/graph`) are mapped explicitly via `MapModulusDiagnostics(app)` —
they no longer depend on the user-facing registration path.

## API robustness (Tier 3)

Request-level safety for unsafe (mutating) HTTP endpoints. The `modulus app`
template wires idempotency into `Program.cs` + `appsettings.json`. Complements
the message-level inbox dedup (`Modulus.Inbox`) — same "process once" guarantee,
but for synchronous HTTP callers/retries rather than integration events.

- **HTTP idempotency** (`Modulus.AspNetCore`) — `AddModulusIdempotency(config)` +
  `UseModulusIdempotency()` (place after `UseModulus()` so the tenant is resolved
  before keys are scoped, and it still wraps the controller so responses replay).
  The middleware guards the configured `Methods` (default POST/PATCH): the first
  request carrying an `Idempotency-Key` is processed and its response buffered;
  concurrent duplicates get **409** while it runs, later duplicates get the
  original response **replayed** (with an `Idempotency-Replayed: true` header), and
  a key reused with a *different* request payload/target gets **422**. Only 2xx
  responses are cached; 5xx and thrown exceptions **release the claim** so a
  genuine retry re-runs. Keys are scoped by tenant (`ICurrentTenant`) so they
  can't collide or leak responses across tenants; the request fingerprint is a
  SHA-256 of method + path + query + body.
- **Store abstraction** — `IIdempotencyStore` (atomic `TryBeginAsync` →
  Started/InProgress/Completed, plus `CompleteAsync`/`AbandonAsync`). Default
  `InMemoryIdempotencyStore` is **per-instance, TTL-bounded**
  (`RetentionSeconds`, default 24h) — fine for a single node/dev/tests. Multi-node
  deployments register their own `IIdempotencyStore` (Redis/EF) **before**
  `AddModulusIdempotency` (`TryAdd` leaves it in place). Config section
  `Idempotency` (`HeaderName`/`Methods`/`RequireKey`/`ValidateRequestMatch`/
  `MaxKeyLength`/`RetentionSeconds`). Covered by `Modulus.AspNetCore.Tests`
  (13 tests: store claim/expiry/replay state machine + middleware
  passthrough/replay/409/422/400/5xx-not-cached).
- **OpenAPI hardening** (`Modulus.AspNetCore`) — `AddModulusOpenApi(config)`
  replaces a bare `AddOpenApi()` (still exposed via `app.MapOpenApi()`). A document
  transformer stamps info (title/version/description/contact/license, bound from
  the `OpenApi` section) and registers a reusable JWT **Bearer** security scheme;
  an operation transformer adds a Bearer requirement to operations carrying
  `[Authorize]` (skipping `[AllowAnonymous]`) so UIs show a padlock only where it
  applies. Built on .NET 10 transformers + `Microsoft.OpenApi` 2.x
  (`IOpenApiSecurityScheme`, `OpenApiSecuritySchemeReference`). Validated
  end-to-end: `/openapi/v1.json` on a generated app emits the config-driven title
  and `components.securitySchemes.Bearer` (http/bearer/JWT). Covered by 7 tests
  (document info/scheme/contact-license + operation authorize/anonymous/none).

- **Integration-test harness** (`Modulus.Testing`, a packable library) —
  `ModulusWebAppFactory<TEntryPoint>` boots the fully composed host (every
  middleware + the mediator pipeline) and swaps **every** module `DbContext` to
  its **own** per-factory in-memory SQLite database (one shared `Cache=Shared`
  database per module context — required for multi-module apps, because a shared
  database makes `EnsureCreated` a no-op for every context after the first), so
  tests drive real endpoints over HTTP with no external database. The swap
  (`TestDatabaseRegistration.UsePerContextSqlite`) removes each context's options
  *and* its EF Core 9+ `IDbContextOptionsConfiguration` descriptor before
  re-adding `UseSqlite` — leaving the config behind applies both the module
  provider and SQLite ("multiple providers registered"). In-memory SQLite dies
  when its last connection closes, so the registry (`TestDatabaseRegistry`) owns a **keep-alive per
  connection string**, opened the first time a context's options are built (Program.cs code between `Build()`
  and `Run()`, such as migrate/seed, runs before any hosted service, so opening it later loses the schema), and
  the factory re-runs `EnsureCreated` per context. The factory also forces its test scheme as the default
  authenticate/challenge/forbid scheme, so a host with its own default scheme (Identity, OpenIddict) still sees the test user. Isolation is by a unique `Cache=Shared`
  name held open by the keep-alive connection for the factory's lifetime. `CreateAuthenticatedClient(...)` drives a header-based
  `TestAuthHandler` (default scheme `Test`) so `[Authorize]` endpoints and a
  `ClaimsPrincipal`-based `ICurrentUser` see a caller-chosen identity; requests with
  no user header stay anonymous. Generated `Program.cs` exposes `public partial
  class Program;`, and the generated test project references `Modulus.Testing` and
  ships an HTTP smoke test (health probe + example-module POST→GET round-trip
  through the swapped DB). Covered by 4 unit tests on the swap reflection; validated
  end-to-end by regenerating an app off the packed 1.0.0 tool and running its
  integration tests (3 passing: boot, health, POST→GET).
- **Feature flags** (`Modulus.AspNetCore/FeatureFlags/`) — a thin wrapper over
  `Microsoft.FeatureManagement`. `AddModulusFeatureFlags(configuration)` binds the
  `FeatureManagement` section (the library's own convention), registers the
  `Percentage` and `TimeWindow` filters, and uses **scoped** evaluation
  (`AddScopedFeatureManagement`) so filters can read the ambient tenant/user;
  `IFeatureManager` / `IVariantFeatureManager` are injectable. Minimal-API (REPR)
  endpoints gate with `.RequireFeature("Flag")` (`RequireFeatureExtensions`) — an
  endpoint filter that short-circuits with **404** when a flag is off (hiding the
  endpoint), the equivalent of MVC's `[FeatureGate]`. Templates wire
  `AddModulusFeatureFlags(builder.Configuration)` and seed a disabled
  `"FeatureManagement": { "SampleFeature": false }` block. Covered by 4 wiring unit
  tests (on/off/unknown flag + filters registered); validated end-to-end in a
  regenerated app by gating `/feature-probe` on `SampleFeature` — 404 with the flag
  off, 200 with `FeatureManagement__SampleFeature=true`.
- **Secrets guard** (`Modulus.AspNetCore/Configuration/SecretsGuard*`) — a startup
  guard rail, not a big feature. `AddModulusSecretsGuard(configuration)` registers a
  hosted service that scans the **effective** configuration at boot and, in
  Development/Staging only (Production is excluded so a false positive can never
  block a boot), **fails fast** when a sensitive value is sourced from a committed
  `appsettings*.json` rather than environment variables, User Secrets (which live
  outside the content root), or a vault. `SecretsGuardScanner` finds the *effective*
  provider per key (last-wins) and flags it only when that provider is a
  `FileConfigurationProvider` physically under the content root; connection strings
  are flagged only when they carry a credential (`Password=`/`AccountKey=`…) and
  don't point at a local host, so a SQLite/localhost dev string never trips it.
  Config lives under `SecretsGuard` (`Enabled`, `FailOnViolation`, `Environments`,
  `SensitiveKeyPatterns`). No new NuGet dependency. Template hygiene: the host
  `.csproj` now carries a `<UserSecretsId>` (a home for dev secrets), generated apps
  ship a `.gitignore` covering `secrets.json` / `appsettings.*.json` / SQLite files,
  and `appsettings.json` seeds a `SecretsGuard` block. Covered by 7 scanner unit
  tests (committed secret flagged; env-override, out-of-tree file, local/SQLite
  connection string, and non-sensitive keys ignored; remote credential flagged;
  source file reported); validated end-to-end in a regenerated app — a fake
  `ExternalApi:ApiKey` in `appsettings.json` fails startup in Development with a
  clear message, and supplying the same key via `ExternalApi__ApiKey` env var boots
  clean (`/health/live` → 200).

- **PII encryption** (marker + abstraction in `Modulus.Core/Abstractions/DataProtection`;
  EF integration in `Modulus.EntityFrameworkCore/DataProtection`; DataProtection-backed
  impl + registration in `Modulus.AspNetCore/DataProtection`) — transparent at-rest
  encryption of designated personal-data columns. Mark a `string` property with
  `[ProtectedPersonalData]`; the base `ModuleDbContext.OnModelCreating` applies an
  `EncryptingConverter` (via `UseModulusPersonalDataEncryption`) to every marked
  property **when an `IPersonalDataProtector` is registered**, so encryption is strictly
  opt-in and adds nothing when unused. `AddModulusPersonalDataProtection(configuration)`
  registers the default `IPersonalDataProtector` backed by **ASP.NET Data Protection**
  (`IDataProtector` with a stable named purpose) — it owns the key ring, storage, and
  rotation, so ciphertext under a retired key keeps decrypting without a bulk
  re-encrypt. Because `Protect` is non-deterministic, encrypted columns can't be queried
  by equality; `IPersonalDataProtector.Hash` gives a deterministic HMAC-SHA256 (keyed by
  `PersonalDataProtection:SearchHashKey`, supplied out-of-band — never committed) to
  populate a companion hash column for lookups. Placement keeps `Modulus.Core` dep-free
  and `Modulus.EntityFrameworkCore` dep-light (both depend only on the `IPersonalDataProtector`
  abstraction); the DataProtection types come from the ASP.NET shared framework, so **no
  new NuGet dependency**. Config lives under `PersonalDataProtection` (`Enabled`,
  `Purpose`, `SearchHashKey`); `Enabled: false` registers nothing (columns stay
  plaintext). Template wiring: `AddModulusPersonalDataProtection` in `Program.sbn` plus a
  `PersonalDataProtection` block in `appsettings.json` (the sample entity stays
  unencrypted — marking a field is the opt-in). Covered by 4 EFCore converter/hook tests
  (ciphertext at rest, plaintext in memory, unmarked columns untouched, deterministic
  hash enables equality search) and 5 protector tests (round-trip, non-deterministic
  ciphertext, keyed hash, throws without a hash key, persisted key ring decrypts an
  earlier provider's ciphertext); validated end-to-end in a regenerated app — a
  `[ProtectedPersonalData]` field is stored as Data Protection ciphertext (`CfDJ8…`, no
  plaintext anywhere in the `.db`) yet reads back transparently over HTTP.
  **Key management (production):** persist the key ring outside the app (file share / DB
  / Key Vault) or restarts lose data; keep the `Purpose` string stable; enabling
  encryption on an existing populated column needs a one-off plaintext→ciphertext
  data-migration pass.

All Tier 3 items are complete; see [`ROADMAP_TIER3.md`](ROADMAP_TIER3.md) for the
as-built records.

## Deferred hardening (recent work)

Follow-ups that closed gaps left by earlier batches; all verified
(`dotnet build` 0/0, `dotnet format` clean, `Category=Unit` green):

- **Outbox management (EF)** — `MapModulusOutboxManagement` list endpoint now
  pushes filters to the DB and fetches at most `page*pageSize` rows per context
  (bounded memory under a failure storm); replay only touches dead-lettered rows
  (`ProcessedAt == null && RetryCount >= MaxRetries`) via batched
  `ExecuteUpdateAsync`, archiving the error + acting user to the log instead of
  nulling history away.
- **Outbox management (Mongo)** — `MapModulusMongoOutboxManagement`
  (`Modulus.Outbox.MongoDB`) mirrors list/inspect/replay/purge with server-side
  filter/sort/skip/limit; reuses the same models + `messaging:manage` permission
  (register via `AddModulusOutboxManagement`, then map the group).
- **Mongo outbox sessions** — `IMongoOutboxSessionProvider` lets a unit of work
  flow its `IClientSessionHandle`; the writer joins the session's transaction
  (atomic with domain writes on a replica set) and falls back to a plain insert
  otherwise. Consumers still dedup via inbox either way.
- **OTel bootstrap** — `Modulus.Observability.ModulusOpenTelemetrySetup.AddModulusOpenTelemetry(configuration, environment)`
  binds the `OpenTelemetry` section (`Enabled`/`ServiceName`/
  `EnableConsoleExporter`/`Otlp:Endpoint|ExportTraces|ExportMetrics`), wires
  ASP.NET Core + HttpClient + Runtime instrumentation plus the Modulus
  sources/meters, and only adds exporters when explicitly enabled (OTLP needs an
  endpoint). New `Modulus.Observability.Tests` project covers on/off wiring.

## Already addressed (recent work)

These were flagged in the initial review and have been fixed; kept here so the
history is discoverable:

- **CLI 4-layer (FoodDelivery-style) rewrite** — the `modulus` CLI now generates
  a modular-monolith layout matching `PROJECT_STRUCTURE.md`: `src/API/{App}.Api`
  host + `src/Shared/{App}.Shared.*` kernel + `src/Modules/{App}.Modules.*`,
  with **4 layers per module** (Domain / Application / Infrastructure /
  Presentation). The previous 7-layer design (separate Contracts /
  IntegrationEvents / Tests projects) was collapsed: DTOs live under
  `Application/Dtos`, integration events under `Application/IntegrationEvents`,
  and tests at the solution root. The host is `{App}.Api` (was `{App}.Host`).
  A working `samples/TradeFlow` (API host + Users module, SQLite)
  validates the full flow: `app` → `add-module` → `generate-crud`, building and
  running end-to-end.
- **Outbox row-locking & retries** (`OutboxProcessor`) — claims rows atomically
  via an `ExecuteUpdateAsync` whose `WHERE` re-checks `LockedUntil` (the
  provider-agnostic equivalent of `FOR UPDATE SKIP LOCKED`), so multiple app
  instances no longer duplicate-dispatch every event. Crashed instances' locks
  expire and are reclaimed (at-least-once). Failed dispatches now schedule
  exponential backoff (`NextAttemptAt`) and dead-letter (with an error log)
  after `MaxRetries` instead of being silently dropped. `OutboxProcessor` is
  now registered in DI (the hosted polling service previously couldn't resolve
  it). Dispatch → `ProcessedAt` non-atomicity is inherent to the pattern and is
  covered by consumer-side inbox dedup.
- **Inbox decorator (EF Core)** — `AddInbox<TContext>` now resolves the inner
  handler via `ActivatorUtilities.CreateInstance` instead of
  `GetRequiredService(ImplementationType)` (which threw because handlers are
  registered only as `IIntegrationEventHandler<T>`). Integration events now
  dispatch instead of throwing on every one.
- **`IdempotentIntegrationEventHandler`** — claims the row atomically via the
  EventId PK (concurrent inserts race; the loser defers via
  `DbUpdateException` → `InboxDeferralException`). No longer double-executes
  when a redelivery arrives mid-`Processing`. Dead-letters after
  `InboxOptions.MaxRetries` instead of hot-looping. The original handler
  exception is preserved even if the final-state `SaveChanges` fails.
- **Multi-tenancy query filter** (`ModuleDbContext.cs`) — now captures the
  `ICurrentTenant` service field (not a value), registers the filter
  unconditionally, and degrades to match-all when no tenant is in scope (no
  more `Guid.Empty` leak). EF's one-filter-per-entity rule is honoured by
  combining soft-delete + tenant predicates. **Caveat:** still incompatible
  with `AddDbContextPool` (the context injects scoped services); use
  `AddDbContext`. Do NOT switch to pooling without a per-request reset hook.
- **`ICurrentTenant` async flow** — `CurrentTenant` is now backed by a static
  `AsyncLocal<TenantInfo?>` with a `Change(...)` scope API, so tenant context
  flows into background jobs / message consumers / hosted services. Plain
  scoped POCO accessors still work (request path).
- **NoSQL tenant fallback** (`MongoTenantFilter`, `ElasticRepository`) — no
  longer filter on `Guid.Empty` in host context; return match-all instead.
- **Other fixed defects:** `LocalFileStorage` path traversal;
  `GlobalExceptionHandler` caught the wrong `ValidationException` type;
  `OutboxPollingService` aborted on any non-OCE exception;
  `NullCurrentUser`/`NullPermissionRegistry` were fail-open;
  `PagedList.TotalPages` divide-by-zero; `ModuleNotFoundException` literal
  message; SignalR `EnableDetailedErrors` shipped to all clients;
  `Modulus.Benchmarks` was NuGet-packed.
- **`AddMediatorHandlers` extension** —
  `Modulus.Mediator.Extensions.MediatorServiceCollectionExtensions` now exposes
  `AddMediatorHandlers(params Assembly[])` which registers command/query
  handlers **without** re-registering the pipeline behaviours. This lets each
  layered module contribute its own handlers (call from the module's
  composition root) while the host calls `AddMediator()` once to set up
  behaviours — previously a per-module `AddMediator` call would duplicate the
  logging/validation/transaction behaviours once per module.
- **Identity password grant (auth bypass)** (`ModulusTokenController`) — the
  token endpoint previously minted tokens for *any* username with zero
  credential check. It now delegates to an `IPasswordGrantCredentialValidator`;
  `AddModulusOpenIddict` registers a `NullPasswordGrantCredentialValidator`
  (deny-by-default) so the grant rejects everything until `AddModulusIdentity`
  replaces it with `IdentityPasswordGrantValidator<TUser>` (SignInManager +
  `CheckPasswordSignInAsync`, honours `IsActive` and lock-out). Granted scopes
  are intersected with a registered allow-list via `PasswordGrant.AuthorizeScopes`
  (defence-in-depth). The refresh-token branch now returns a proper
  `invalid_grant` error instead of a bare `Forbid()`.
- **External IdP token validation** (Auth0, Okta, AzureAd, Duende, Authentik) —
  the adapters validated bearer tokens by GETting the userinfo endpoint and
  treating `200` as valid (validated nothing locally and mutated the shared
  `HttpClient`'s auth header). They now use a shared `OidcDiscoveryValidator`
  that fetches the provider's JWKS via OIDC discovery and locally checks the
  **signature, issuer, and lifetime** (1-min clock skew). The issuer is taken
  from the discovery document. Audience validation is opt-in
  (`validAudiences` ctor arg) — recommended for production; off by default to
  avoid rejecting valid tokens whose audience isn't the client id. Keycloak is
  unchanged (it already used RFC 7662 introspection, which is correct). The
  pure `ExternalTokenValidator.ValidateJwtAsync` is unit-tested with real
  RSA-signed JWTs (tampered/expired/wrong-issuer/wrong-audience/unknown-key).
- **CLI generated app improvements** — the `modulus app` template now generates
  an `.editorconfig` (consistent formatting across editors), a smoke test
  (`ModulePipelineSmokeTest.cs`) that boots the full module pipeline and verifies
  every module `DbContext` resolves from DI, and pins `SQLitePCLRaw.bundle_e_sqlite3 3.0.3`
  to eliminate the NU1903 vulnerability warning. The generated `Program.cs` now
  calls `AddModulusEvents()` which registers `DomainEventDispatcher` (required
  by `ModuleDbContext` — previously missing, causing runtime activation failure).
  Host csproj Scriban template whitespace was fixed (doubled indentation on
  `{{ if }}` blocks). `.slnx` uses flat sibling folders so `dotnet test`/`dotnet
  sln list` discover all projects.

## Package consolidation (55 → 23)

The framework was consolidated from 55 packages to 23:

- **Merged abstractions into implementations:** `Core.Abstractions` → `Core`,
  `EFCore.Abstractions` → `EFCore`, `Mediator.Abstractions` → `Mediator`,
  `Events.Abstractions` → `Events`, `Inbox.Abstractions` → `Inbox`,
  `SignalR.Abstractions` → `Platform`, `Identity.Abstractions` → `Identity`.
- **`Outbox.Abstractions` kept separate** — it's the seam that prevents
  a circular dependency (`EFCore` → `Outbox.Abstractions`, `Outbox` → `EFCore`).
- **Merged platform services:** `MultiTenancy`, `Authorization`,
  `BackgroundJobs`, `Caching`, `Storage`, `SignalR` → `Modulus.Platform`.
- **Merged identity adapters:** 6 external IdP validators + EF Core mapping →
  `Modulus.Identity`.
- **Merged observability:** `Diagnostics` + `OpenTelemetry` → `Modulus.Observability`.
- **Dropped stubs:** Cassandra, CosmosDB, DynamoDB, Elasticsearch, Redis, Dapper,
  ServiceBus, Sqs, SignalR.Azure/Redis, BackgroundJobs.Hangfire/Quartz,
  Benchmarks (can be re-added as needed).
- **Namespaces preserved:** types keep their original namespaces (e.g.
  `Modulus.Core.Abstractions.IModule`) even when compiled into a different
  assembly. Only `<ProjectReference>` / `<PackageReference>` names changed.

## Hardening (Batch A–D, recent work)

These were identified in the full framework review and fixed across four batches:

### Batch A — Defect fixes (13 items)

- **A1** `RedisCacheService` — tag keys now tenant-scoped via `ICurrentTenant`.
- **A2+A3** `ChannelJobQueue` — `JobEnvelope` carries `TenantId`/`CorrelationId`;
  worker loop restores ambient context; `ScheduleAsync` fire-and-forget catches OCE.
- **A4** `EfOrgHierarchy` — `Snapshot()` DB reads moved outside `lock(_gate)`
  with double-check swap to avoid blocking writers during queries.
- **A5** `IntegrationEventDispatcher` — static `ConcurrentDictionary`
  compiled-delegate cache mirrors `ChannelJobQueue.s_jobInvokers`.
- **A6** `PermissionResolver` + `PermissionRequirementHandler` +
  `DelegationAwarePermissionResolver` — new 2-arg `Resolve(query, grants)`;
  handler fetches grants once and passes to resolver (single DB read per request).
- **A7** `IdempotencyMiddleware` — fingerprint now includes `Content-Type`.
- **A8** `EfRepository` — `GetByIdAsync` uses `EF.Property` predicates
  (filter-honoring), supports composite PKs as `object[]`.
- **A9** `RabbitMqEventBus` — `BasicReturnAsync` handler logs + meters
  unroutable publishes; new `ModulusMeters.Events` meter added.
- **A10** `ModuleHealthEndpoint` — per-check try/catch + 5s timeout.
- **A11** `ModulusTokenController` — refresh grant rebuilds principal from
  current user store (roles, claims).
- **A12** `TestDatabaseRegistrationTests` — 3 tests converted to async.
- **A13** `EndpointConfig.WrapResponse` default changed to `true`.

### Batch B — Foundations (4 items)

- **B1** `TimeProvider` — optional param on `ModuleDbContext` primary constructor;
  `ApplyAuditFields` uses `_clock.GetUtcNow()`; `RegisterCoreDefaults` registers
  `TimeProvider.System`.
- **B2** Auto-`TenantId` stamping — `ModuleDbContext.SaveChangesAsync` stamps
  `IHasTenantId.TenantId` on `EntityState.Added` entities when tenant is active.
- **B3** `IDataSeeder` interface + `SeedModulusDataAsync` extension on
  `IServiceProvider` for startup data seeding.
- **B4** `IHasConcurrencyStamp` marker + `ConfigureConcurrencyTokens` on
  `ModuleDbContext` + `GlobalExceptionHandler` maps `DbUpdateConcurrencyException`
  → 409.

### Batch C — Tenancy + multi-node (4 items)

- **C1** Tenant-scoped cache keys (done in A1).
- **C2** `TenantInfo.ConnectionString` + per-tenant `AddModuleDatabase` overload
  with `Func<IServiceProvider, string>` resolver and `optionsLifetime: Scoped`.
- **C3** `RedisCacheBackplane` — `BackgroundService` subscribing to Redis
  pub/sub; `RedisCacheService` publishes invalidations after
  `RemoveByTagAsync`/`RemoveAsync`; `AddRedisCacheBackplane()` extension.
- **C4** Outbox leader election — `OutboxOptions.EnableLeaderElection` acquires
  `IDistributedLock` before each polling cycle; other replicas idle until lease
  expires.

### Batch D — Identity (2 items)

- **D1** `/connect/revoke` endpoint — enabled via `SetRevocationEndpointUris`
  in `AddModulusOpenIddict`; OpenIddict handles RFC 7009 revocation natively.
- **D4** Security-stamp sessions — `PasswordGrantResult.SecurityStamp` embedded
  in access token; refresh handler validates stamp still matches current user;
  `BuildPrincipalAsync` refreshes stamp on each refresh cycle; claim gated to
  `AccessToken` destination only.

**Remaining:** `IAuthorizeInteractionService` hook (consent/custom interaction
handling). User/role CRUD is app-specific and intentionally not in the framework;
`Modulus.Identity` exposes `ModulusUser`/`ModulusRole` + ASP.NET Core Identity
abstractions for apps to wire their own admin APIs.

## Modulus.UI (Razor Pages + HTMX + Alpine + Tabler)

Optional server-rendered UI, designed in
[`docs/UI_FRAMEWORK_GUIDELINE.md`](docs/UI_FRAMEWORK_GUIDELINE.md) (v2, adopted;
supersedes `UI_FRAMEWORK_PLAN.md`). v1 is evolved in place, so there is no separate
`Modulus.UI.Htmx` package. **Dependency rule (enforced by tests):** UI references
Modulus, never the reverse; feature UIs depend on `Theme.Abstractions`, never on
`Theme.Tabler` (`ThemeDependencyRulesTests`, `UiDependencyDirectionTests`).

- **`Modulus.UI.Theme.Abstractions`** — `ITheme` (`GetLayout`, `Styles`, `Scripts`),
  `ThemeAsset`, `StandardLayouts` (Application/Account/Empty/Public), `ThemeOptions`
  (`Modulus:Ui:Theme`), `UiSlots`, `UiDesignTokens` (`--m-*`).
- **`Modulus.UI.Core`** — `HtmxResponse`/`HtmxPageModel`, `UiNavigationRegistry` +
  `IMenuContributor` / `IToolbarContributor` / `ISlotContributor` (all run in
  registration order, permission-filtered against `ICurrentUser`),
  `IModulusViewResolver` (app `/Views/Shared/Modulus/...` → theme
  `/Themes/{Theme}/Views/...` → `_Default`), `IThemeAccessor`, framework error pages
  (`UseModulusErrorPages` + `MapModulusErrorPages`). `AddModulusUi()` registers a
  fail-closed `NullCurrentUser` default so menus/slots resolve without Identity.
  `IUiNavigationRegistry.GetMenu()` / `IToolbarProvider.GetItems()` are synchronous
  contracts, so they block on the (async) contributor `ValueTask` under a scoped
  `VSTHRD002` suppression — safe because Modulus hosts have no `SynchronizationContext`.
- **`Modulus.UI.Theme.Tabler`** — `AddTablerTheme(configuration?)`. Layouts at
  `/Themes/Tabler/Layouts/{Application,Account,Empty,Public}.cshtml`; shell partials
  in `Shell/` (`_Head`, `_Sidebar`, `_Topbar`, `_Footer`, `_PageEnd`, `_PageScripts`);
  standalone `_403/_404/_500` under `Views/Errors/`. Vendored htmx/Alpine/Tabler live
  in `wwwroot/tabler/vendor/` (no CDN); `css/modulus.css` maps `--m-*` tokens onto
  `--tblr-*` (apps override only `--m-*`; set `--m-primary-rgb` with `--m-primary`);
  `js/modulus.js` is the client runtime (`window.Modulus`: `onLoad`, `components`,
  `toast`, `confirm`, `colorMode`, antiforgery header, 422 swap, modal host).
- **Layout gotchas.** `RenderSectionAsync` writes straight to the output (never call it
  into a variable), and a partial cannot render a section — so each layout emits
  `_PageEnd`, then `@await RenderSectionAsync("Scripts")`, then `_PageScripts`
  (Scripts slot, Alpine last so page `Alpine.data()` registrations precede Alpine's
  start). Framework views must stay CSP-clean: no inline `<script>` or `style=`
  (asserted by `TablerLayoutRenderTests`). Layouts stamp `data-bs-theme` (Tabler's
  attribute); the color-mode cookie is `modulus-color-mode`.
- **Testing** — `tests/unit/Modulus.UI.Theme.Tabler.Tests` boots a real TestServer
  host (Razor SDK test project with probe views) and asserts on rendered HTML; add
  `AddApplicationPart` for both `Modulus.UI.Core` and the theme assembly in such
  hosts, since the entry assembly is the test host.
- **Alpine is the CSP build** (`@alpinejs/csp`, no `eval`), so directives may only
  reference component members, never inline expressions (`x-data="{ show: true }"`
  fails). Shared components (`mDismissibleAlert`, `mCopyButton`, `mPasswordToggle`) live
  in `Modulus.UI.Core/wwwroot/modulus-ui/alpine-components.js`, which every theme must
  load *before* Alpine (`alpine:init` fires once). `AlpineCspSafetyTests` scans every
  `src/ui` view and fails on an inline expression or an unregistered `x-data` name.
- **`Features.Morph`** adds `hx-ext="morph"` to the body (idiomorph 0.7.3 is vendored);
  elements opt in with `hx-swap="morph"`. It is off by default.
- **Feature UIs use the active theme.** Each package's `Pages/_ViewStart.cshtml` calls
  `Context.GetThemeLayout(StandardLayouts.Application)` (Identity uses `Account`), which
  resolves through `ThemeOptions.Layouts`, returns `null` for htmx fragment requests, and
  falls back to the legacy `_UiLayout` when **no** `ITheme` is registered — so apps that
  only call `AddModulusUi()` (including CLI-generated ones) keep rendering. Call
  `AddTablerTheme()` to opt in.
- **Component library (guideline phase 2, in `Modulus.UI.Core/Components/`).** Tag helpers
  hold no markup: they build a view model and render `Views/Shared/Modulus/_Default/{Component}/Default.cshtml`
  through `ComponentRenderer` → `IModulusViewResolver` (app `/Views/Shared/Modulus/...` →
  theme `/Themes/{Theme}/Views/...` → `_Default`), so any component is overridable by file
  path. `AddModulusUi()` registers the resolver, so components work without a theme.
  - `m-card`, `m-datatable` (+ `m-column` headers; the page writes its own `<tr>` rows, so
    cell markup stays in the page; `empty`/`empty-message`, optional pager via `page`),
    `m-pagination` (links rebuilt from the **current URL**: filters survive paging, the
    `handler` key is dropped, `route-Name="v"` layers extra/overriding values, null removes),
    `m-form` (`handler`/`page`/`action`, `target` or `modal` for htmx, validation summary on 422),
    `m-modal` (+ `m-modal-footer`; content of `#m-modal-container`), `m-tabs`/`m-tab`
    (Bootstrap data attributes; `source` lazy-loads with `hx-trigger="intersect once"`),
    `m-breadcrumbs` (`IBreadcrumbContributor` / `IBreadcrumbProvider` /
    `ViewData.SetBreadcrumbs`, gated by `Features.Breadcrumbs`, rendered by the Application
    layout), `m-toolbar` and `m-page-header page-id="..."` (contributed `ToolbarItem`s; `Target`
    defaults to the modal container).
  - Pages declare `ViewData.SetPageId(...)`. Migrated pages reuse their **menu ids** as page ids
    (`Users.Directory`, `Users.Roles`, `Tenancy.Directory`, `AuditLogging.Browser`,
    `Notifications.Inbox`); treat them as public contract like menu ids.
  - Gotchas found by the render tests: the MVC form tag helper turns antiforgery **off** when an
    explicit `action` attribute exists, so `m-form`'s view sets `asp-antiforgery="true"`; a
    self-closing `<m-page-header ... />` drops helper-set content unless
    `TagMode.StartTagAndEndTag` is forced (it used to render an empty `<div />`); views under
    `Views/Shared/Modulus/` need `@using global::Modulus.UI` because the generated namespace
    contains a `Modulus` segment.
  - **Self-refreshing grids.** `m-datatable source="/x?handler=Rows" refresh-on="product:changed"` puts
    `hx-get`/`hx-trigger="product:changed from:body"` on the `<tbody>`, so a response that calls
    `HtmxResponse.NotifyChanged("product:changed")` makes the grid re-query and swap fresh `<tr>` fragments.
    Entries must end with `:changed` (validated); `source` alone loads once (`hx-trigger="load"`); an empty grid
    with a `source` still renders its table shell. The pager is not refreshed, so keep source grids unpaged.
    The rendered attributes are tested; the htmx round-trip itself is not exercised in a browser.
  - Display components: `m-stat` (`label`/`value`/`delta`/`tone`/`hint`; values arrive pre-formatted),
    `m-empty-state` (`title`/`message`/`icon`, content = actions), `m-detail-list` + `m-detail`
    (`value` attribute is encoded, element content is markup, empty shows a dash), `m-timeline` +
    `m-timeline-item` (Tabler 1.5 `.timeline-event*`), `m-confirm` (a button with `hx-confirm`, exactly one of
    `post`/`delete`; the theme runtime routes `hx-confirm` to its modal, so no script).
  - Form fields: `m-input for="Input.Email"` and `m-select for="…" items="…"`. The tag helper resolves the
    posted name/id from the expression, the label (`[Display]`) and required marker from metadata (never for
    a checkbox), prefers the **posted** value on a re-render (ModelState `AttemptedValue`), gathers the field's
    errors, and never echoes a password; the partial is plain markup. `type` covers text/email/password/number/
    date/datetime-local/textarea/checkbox, so dates and money are `type="date"` / `step="0.01"` rather than
    separate `m-date`/`m-money` components. The class names are `ModulusInputTagHelper`/`ModulusSelectTagHelper`
    (MVC ships its own `InputTagHelper`/`SelectTagHelper`).
  - File upload: `m-file for="Input.Attachment"` (an `IFormFile` member) or `m-file name="file"` (a handler parameter such as
    `OnPostUpload(IFormFile file)`), inside `<m-form multipart="true">` (adds `enctype` and, with a `target`, `hx-encoding`).
    `accept`/`multiple`/`hint`/`label` as usual; a file input is never pre-filled. Razor Pages files a missing-file error
    under the bare property name (`Attachment`) when no file part is posted, so `m-file` also looks there. The Files UI's
    upload form uses it.
  - Editable child rows: `<m-line-items for="Input.Lines" row-partial="_LineRow" add-label="…" remove-label="…" />`. The row
    partial's model is the collection's element type and its fields are ordinary `m-input for="Sku"`s: the tag helper renders it
    once per item with `TemplateInfo.HtmlFieldPrefix = "Input.Lines[i]"` (so names are `Input.Lines[i].Sku` and posted values/errors
    land on the right row on a 422), and once blank inside a `<template>` with the literal index `__index__`. The `mLineItems`
    Alpine component clones that template for "add" and renumbers names/ids/`for`s after a "remove", so indexes stay contiguous
    (the default collection binder silently drops rows after a gap, and model-state keys would no longer match a re-render).
    An overriding view must keep the `data-line-items` / `data-line-rows` / `data-line-row` / `data-line-template` and
    `data-name-prefix` / `data-id-prefix` hooks. The row's model needs a parameterless constructor for the blank template row
    (otherwise the blank row renders with a null model). Gotcha: a partial's `ViewDataDictionary` cannot be copied from the page's
    (it is typed to the page), so the tag helper builds an untyped one that shares the page's `ModelState`.
  - Extension fields: another module contributes fields to an entity's form with
    `services.ConfigureEntityUi("Catalog.Product", e => e.Fields.Add(new EntityField("ReorderLevel", typeof(decimal), "Reorder level", tab: "Inventory", validators: [new RangeAttribute(0, 1000)])))`
    (from `ConfigureServices`; calls accumulate in module registration order and a later module may `Fields.Remove` an earlier
    one's field; a duplicate name throws), and the owning page renders them with
    `<m-fields entity="Catalog.Product" for="Input.Extra" />`, where `Extra` is a `Dictionary<string, string?>` on the input model.
    Each field goes through the overridable `Input` component (so it posts as `Input.Extra[Name]`, shows the posted value and its
    errors on a 422) inside the overridable `Fields/Default` wrapper; `tab="..."` renders one tab's fields (omit or leave empty for all).
    Supported types: string, bool, int, long, decimal, double, DateOnly, DateTime, TimeOnly (or nullable), converted with the invariant
    culture. Server side, `registry.ValidateEntityFields(entity, user, Input.Extra, ModelState, "Input.Extra")` runs each field's
    `ValidationAttribute`s and files errors under `Input.Extra[Name]`, and `registry.ReadEntityFields(...)` returns the typed values.
    **A field with `requiredPermission` is neither rendered, validated nor read for a user without it**, so a hand-crafted post cannot
    set it (always go through `ReadEntityFields`, never copy the raw bag). `IEntityUiRegistry` is frozen on first use. Gotcha: Razor passes a null
    `tab="@Model.Tab"` to a string attribute as `""`, so an empty tab means "all".
  - Extension-field **storage** (`IHasExtraProperties`, in `Modulus.Core/Abstractions/Entities`): an entity that implements it
    (`Dictionary<string, string?> ExtraProperties { get; set; }`) gets a required JSON text column mapped by `ModuleDbContext`
    (`UseModulusExtraProperties`, applied to every root entity type implementing the marker, no per-field schema change) with a
    **content-based `ValueComparer`**, so `entity.ExtraProperties["Bin"] = "A-7"` is detected as a change; a new entity is stored as
    `{}` and never reads back null. Values are invariant-culture text keyed by field name. The page's save path is
    `registry.ValidateEntityFields(...)`, then `entity.SetExtraProperties(registry.ReadEntityFieldText("Catalog.Product", user, Input.Extra))`:
    `ReadEntityFieldText` returns the *visible* fields as canonical text (`EntityField.ToText`: `true`/`false`, `25.5`, `2026-09-20`,
    `2026-09-20T10:30`, `08:15`; seconds only when non-zero; round-trips through `TryConvert`), with null for an emptied field, and
    `SetExtraProperties` **merges** (null/empty removes the key, unmentioned keys stay), so a user without the field's permission can
    neither set nor erase it. To pre-fill an edit form copy the stored bag into `Input.Extra`. `EF` expression trees cannot hold
    `out var`, hence the static comparer helpers. Not built: `IEntityFieldHandler` (the contributing module saves into its own tables
    instead of the JSON bag) and a migration step: an existing table needs the `ExtraProperties` column added (not null, default `'{}'`).
  - Extension columns and row actions (same `ConfigureEntityUi`, `e.Columns` / `e.Actions`; a later module may `Remove` either, duplicates
    throw): `new EntityAction("Inventory.Adjust", "Adjust stock", hxGet: "/Inventory/Adjust?productId={id}", target: EntityActionTarget.Modal,
    requiredPermission: ...)` (exactly one of `url`/`hxGet`/`hxPost`; `{id}` becomes the URL-encoded row id; `Modal` swaps into
    `#m-modal-container`, `Row` replaces `closest tr` with the response) and `new EntityColumn("Stock", "Stock", typeof(StockProvider), order: 45)`.
    Because a page writes its own `<tr>`s, the owning page places them: `<m-datatable entity="Catalog.Product">` appends the visible contributed
    headers where `<m-entity-columns />` sits among the `m-column`s (default: after the last; the empty-state `colspan` counts them),
    `<m-entity-cells entity=".." row-id="@p.Id" values="Model.Extra" />` writes the matching `<td>`s at the same position (a dash when there
    is no value, so the table stays aligned) and `<m-entity-actions entity=".." row-id="@p.Id" />` renders the row's buttons (nothing when none).
    The page loads the values once per page: `Extra = await registry.LoadEntityColumnsAsync(HttpContext.RequestServices, "Catalog.Product", user, ids)`
    calls each visible column's `IEntityColumnValueProvider.LoadAsync(ids)` **once with the whole page** (resolved from DI if registered, else
    `ActivatorUtilities`); values are pre-formatted text and are HTML-encoded on output. A column/action needing a permission the user lacks
    is neither rendered nor loaded. There is no `Columns.Hide` (page-written cells cannot be hidden by name).
  - Covered by `TablerComponentRenderTests` (probe views + a Razor Page), `FieldAndDisplayComponentTests`
    (display components + `Pages/Probe/FieldsPage`, including a rejected post), `LineItemsComponentTests`
    (`Pages/Probe/LineItemsPage`: rows, blank template, bind, per-row errors), `EntityFieldsComponentTests`
    (`Pages/Probe/EntityFieldsPage`: contributed fields, tabs, permissions, typed post, per-field errors), `EntityListComponentTests`
    (`Pages/Probe/EntityListPage`: contributed headers/cells/actions), and `EntityUiRegistryTests` / `EntityListContributionTests`
    (UI.Core: registry, conversion, validation, batch loading), `FeatureUiRenderTests`
    (real Tenancy and AuditLogs pages through the theme) and `ComponentLogicTests`. The `mLineItems` script itself is not
    exercised in a browser by the suite (it was checked once against the rendered page in jsdom).
- **CSP rules for views.** Framework views load nothing from a CDN and use no inline event handlers
  (`onchange=`/`onclick=`): use a vendored asset or an Alpine component (`mAutoSubmit` submits its
  `<form>` on change; Identity relies on server-side validation only). `AlpineCspSafetyTests` fails on either.
- **App kind (`api` vs `web`).** Product rule: an **API** app creates no UI at all; a **web** app (`--kind web`) creates the web UI **and** keeps the API,
  so the same modules serve the UI and external clients (mobile, desktop, other systems). All UI, prebuilt feature UIs included (Identity, Users, ...), must
  stay customizable by the app. The kind lives in the host csproj as `<ModulusAppKind>` (`AppKinds.Read`, `ModuleDiscovery.AppInventory.Kind`); a host
  without it predates app kinds and is unconstrained (`null`, so the old opt-in `--with-ui` keeps working). `modulus app` resolves it in `NewAppCommand.ResolveKind`
  (explicit `--kind`; `--ui-modules` alone implies `web`; `--kind api` with UI modules is an error; prompt, else `api` when non-interactive). A web app installs
  `NewAppCommand.ResolveWebInstall`: `UI.Core` foundation, the chosen modules, then the Tabler theme (`--no-theme` opts out), plus `Platform`, even with no prebuilt module.
  `AppKinds.ResolveCrudUi` decides `generate-crud`: `web` scaffolds the admin page by default (`--no-ui` = API side only), `api` refuses `--with-ui`, unmarked = opt-in
  `--with-ui`. `ui add`, `ui eject` and `ui diff` refuse an `api` host. Covered by `AppKindTests`. **Identity backend (`--auth openiddict`).** A local token server needs users, so `modulus app --auth openiddict` (either kind) also generates
  `src/Modules/{App}.Modules.Identity/{App}.Modules.Identity.Infrastructure` (an infrastructure-only module: `AppIdentityDbContext : ModulusIdentityDbContext`,
  its design-time factory, `IdentityModule`, `IdentitySeeding`; `NewAppCommand.GenerateIdentityModule`, templates in `cli/Templates/identity/`). `modulus migrate` finds it like
  any module (`migrate add InitialCreate --module Identity`), and `CodeGen.ChooseModuleRoot` ignores it (no Application layer) so `generate-crud` without `--module` still
  picks the one business module. `IdentityModule` registers the context, `AddModulusIdentity`, `AddModulusIdentityStore` and the token controller's application part;
  `AddModulusOpenIddict` stays in Program.cs and **must come before `AddModulus(...)`** (the module's `AddModulusIdentity` replaces the deny-everything password validator
  and the later registration wins; the framework now `TryAdd`s that default so the order no longer matters). Program.cs seeds after migrating (`SeedIdentityAsync`):
  the `Admin` role and a public first-party client whose id is the lower-cased app name (`ClientId`) always; the first administrator only when no users exist and either
  `Identity:Seed:AdminEmail`/`AdminPassword` are configured (any environment) or, in Development only, `admin@{app}.local` with a random password logged once.
  Development settings turn `Identity:AllowPasswordFlow` on (the base file leaves it off: the password grant hands credentials to the client, so production opts in
  deliberately for trusted first-party clients), plus development certificates. Auth schemes: a **web** app calls `AddModulusSmartAuth()` (bearer for `/api` and
  `Bearer` headers, the Identity cookie for pages; the helper now `PostConfigure`s authenticate/forbid too, because `AddIdentity` sets its own defaults that beat
  `DefaultScheme`), an **api** app makes the OpenIddict validation scheme the default. Verified end to end for both kinds: anonymous API call 401, password grant 200,
  bearer `POST` 201 / `GET` 200, refresh 200, revoke 200, and a web app's UI shows the row created through the API. Generated Create/Update/Delete (and `generate-command`)
  commands carry `[Transactional(typeof(IUnitOfWork))]`: with more than one `DbContext` registered (the identity database, or a second module) an undeclared command failed
  with "ambiguous transaction scope", i.e. every generated POST answered 500; `IUnitOfWork` is the module's own interface, implemented by its context, so the Application layer
  scopes the transaction without referencing Infrastructure. `--auth none` still registers no scheme (endpoints require an authenticated user by default, so the API answers 500
  until one is added; `modulus app` warns and Program.cs has a comment) and external providers validate tokens only. **API permission:** every endpoint of a generated CRUD set declares `Permissions("{module}:{route}:manage")` (`ModuleModel.RequiredPermission`, set when the host has
  the Admin role: `UiAccessGates.HasAdminRole` = the sign-in or `SeedIdentityAsync(`, so an api app qualifies), the permission that guards the entity's admin page; the Admin role holds it
  (`Program.cs`: `AddModulusAuthorization`, `AddGrantStorePermissionChecker`, `AddPermissions`, `AddPermissionGrants`, for the example module and for each later `generate-crud`, in an api host inserted
  before `builder.Build()`, at the file's own indentation). Anonymous 401, signed in without the role 403. A host with no identity backend keeps endpoints as open as the host. The generated test project
  signs in as `Admin` and asserts 401/403/200, and ships `appsettings.Testing.json` (throwaway certificates, password grant; un-ignored in `.gitignore`). `generate-crud` also updates existing files it
  otherwise never overwrites: `UiNavSidecar.EnsureItem` (second entity gets a sidebar item and manifest feature; an item without `requiredPermission` gets one) and
  `UiAccessGates.EnsurePageGuard` (an unguarded `IndexModel` gets `[Authorize(Policy = ...)]`); a hand-edited file without the generated shape is left alone.
  **Authorization-code + PKCE** (`Identity:AllowAuthorizationCodeFlow`, on in a generated web app): `ModulusAuthorizeController` (`/connect/authorize`) signs the user in through the Identity cookie
  (`/account/login`, `ReturnUrl` = the same request; `prompt=login` / `max_age` re-authenticate, `prompt=none` answers `login_required`), issues the code with the cookie user's claims (`BuildPrincipal`,
  scopes intersected with `AllowedGrantScopes`, security stamp kept), and `ModulusTokenController` redeems it through the refresh path's re-verification (active, lock-out, stamp, current roles).
  PKCE is mandatory, consent implicit (first-party). A web app with `--auth openiddict` always gets the Identity UI (it is the login page: `NewAppCommand.WithSignInPage`); Development lists
  `Identity:Seed:RedirectUris` (`{app}://callback`, `http://localhost:5173/callback`) and `IdentitySeeding.EnsureClientAsync` syncs the first-party client (authorization endpoint, code grant, PKCE,
  redirect URIs) on every start. An API app has no login page and stays on the password grant. Verified end to end (login, code, redeem, gated API, refresh, replay/wrong verifier/missing challenge/bad
  redirect refused; note a replayed code revokes the tokens it issued, by design). Known gaps: `modulus app` does not scaffold the example module's page (run `generate-crud Product --module Catalog`),
  grants are the in-memory seed (switch to the EF grant store for runtime edits), an external-provider web app has no cookie login for pages, and there is no third-party consent screen.
- **API extension fields (web apps).** A web app's generated API exposes an entity's extension fields to external clients, through the same registry and per-field
  permissions as the admin page, never by returning the stored `ExtraProperties` bag. `EntityApiFields` (Modulus.UI.Core, extension methods on `IEntityUiRegistry`):
  `VisibleExtraProperties(entity, user, stored)` (only the entries of fields the caller may see), `ValidateEntityFieldsForApi(entity, user, values, partial)` (error list; a create
  checks every visible field so a missing required one is reported, an update (`partial`) only those sent; a name that is not a visible field is `Unknown extension field 'X'`, the same
  message whether it does not exist or is gated, so the response never reveals a hidden field) and `ReadSubmittedEntityFieldText(entity, user, values)` (canonical text of only the visible
  fields the caller sent; an empty value maps to null, which removes the key; unsent fields stay untouched). They differ from `EntityFieldValues` (the form's helpers) on purpose: a form
  always posts every field so an empty one means "clear", an API caller sends only what it changes. Generated for a **fresh** set in a web app (`ModuleModel.HasApiExtraFields`,
  `GenerateCrudCommand.ExposesExtraFieldsInApi`: kind is `web`, none of the DTO/query-handler/endpoint files exist yet, and the entity and both commands carry the marker; files are never
  overwritten, so an older set keeps its API as it was; the example module of `modulus app --kind web` gets it too): the DTO becomes a `record` with an `ExtraProperties` bag that the
  query handlers fill with the **unfiltered** stored copy (an endpoint must filter it), the create and update requests take `extraProperties`, the endpoints inject
  `IEntityUiRegistry` + `ICurrentUser`, reject with `ValidationException` (400 with an `errors` list) and pass only `ReadSubmittedEntityFieldText(...)` to the command, whose handler already
  merges. The Presentation project gains a `Cobytelabs.Modulus.UI.Core` reference (`generate-crud` adds it to an existing module). Registry key = `{Module}.{Entity}`, the admin page's
  `EntityKey`. Verified end to end on a generated web app with two contributed fields (one gated by a permission): create 201, out-of-range 400, the gated field 400 "Unknown
  extension field", GET returns only visible fields, an update without `extraProperties` keeps them, an empty value clears one, and the stored JSON is `{"ReorderLevel":"30"}` then `{}`.
  Covered by `EntityApiFieldsTests` and `ApiExtraFieldsTests`. Not done: exposing contributed columns/actions over the API, and a discovery endpoint listing the fields a caller may set.
- **CLI (guideline phase 3, partial).** The generated UI shell (`ui/ViewStart.sbn`,
  `ui/CrudIndexCshtml.sbn`) uses `Context.GetThemeLayout()`, so generated pages follow the active theme
  and still fall back to `_UiLayout` when none is registered. `modulus ui add Tabler` installs
  `Cobytelabs.Modulus.UI.Theme.Tabler` and wires `AddTablerTheme(builder.Configuration)` plus its
  `using Modulus.UI.Theming.Tabler;` (`UiModuleDefinition.HasEndpoints: false` = no `Map…` call;
  `ExtensionNamespace` = extra using). **`generate-crud --with-ui` installs the theme by default** (package
  reference + `AddTablerTheme`, via `UiCrudWiring.EnsureHostWiring`); `--no-theme` opts out, and a later run
  upgrades a host that was wired without one. `modulus app --ui-modules …` installs it by default too
  (`--no-theme` opts out; a web app always gets it), via `NewAppCommand.ResolveWebInstall`, which resolves ids
  through `UiModuleCatalog.Find` (the old inline lookup compared `identity` to the catalog id `Modulus.Identity`,
  never matched, and silently wired nothing). `UiHostWiring.EnsureUiWiring` adds `using Modulus.UI;` and the
  module's own extension namespace (`ExtensionNamespace ?? Namespace`); before, only the theme's namespace was
  added, so `ui add` and `app --ui-modules` produced a non-compiling `Program.cs`. `UiHostWiring` also registers the backend
  services a feature UI's pages resolve (`UiModuleDefinition.BackendRegistrations`: Files → `AddFileStorage`, Settings →
  `AddModulusSettings`, Notifications → `AddModulusNotifications`, AuditLogging → `AddModulusAuditLogging`, Tenancy →
  `AddMultiTenancy`, Permissions → `AddModulusAuthorization`), all in-memory/`TryAdd` defaults, so the page no longer 500s on first
  request. A registration the host already has is left alone; the `Marker` must include the `(` (`AddModulusSettings`
  alone matches the module's own `AddModulusSettingsUi(`). Identity/Users are not covered: they need an app-specific user store.
  The generated list (`ui/CrudTablePartial.sbn`) is an entity-aware `m-datatable entity="@Model.EntityKey"` (`EntityKey` = `{Module}.{Entity}`,
  e.g. `Catalog.Product`): columns and row actions other modules contribute with `ConfigureEntityUi` show up with no page edit, the page model
  loads their values once per page (`LoadEntityColumnsAsync` inside `LoadAsync`, so htmx table swaps refresh them) and `_Table` takes the
  page model, not the item list. The create flow carries extension fields: a new entity implements `IHasExtraProperties` (so its table gets the
  JSON `ExtraProperties` column), `Create{Entity}Command(string Name, IReadOnlyDictionary<string, string?>? ExtraProperties = null)` (optional, so the API
  endpoint and existing callers still compile) has its handler call `SetExtraProperties`, and the page model (`ModuleModel.HasExtraFields`) validates
  `Input.Extra` with `ValidateEntityFields` (422 with per-field errors, before the command is sent) and passes `ReadEntityFieldText(...)` to the command,
  while `_CreateForm` renders `<m-fields entity="@Model.EntityKey" for="Input.Extra" />` (nothing when no module contributed a field). Files are never
  overwritten, so `GenerateCrudCommand.SupportsExtraFields` checks the entity and create command on disk: a CRUD set generated before this keeps its old
  command and gets a UI without `m-fields` that still compiles. An API-only app's endpoints and DTOs never expose the bag (there is no registry to filter it with); a web app's do, see *API extension fields* below. A new entity therefore has an `ExtraProperties` column, and a migration scaffolded after `generate-crud` includes it.
  **Edit modal** (`ModuleModel.HasEditForm`): a row's Edit button (`hx-get` `?handler=Edit&id=` into `#m-modal-container`) loads `_EditForm` (an `<m-modal>` whose
  footer submit points at the form by id), pre-filled by `Get{Entity}ForEditQuery` (a UI-only query returning `{Entity}EditDto` with a *copy* of the bag; no API
  endpoint uses it). The form posts `Edit.*` (never mixed with the create form's `Input.*`) to `OnPostUpdateAsync`, which validates `Edit.Extra` first and sends
  `Update{Entity}Command(Id, Name, ExtraProperties = null)`; its handler merges (an emptied field removes its key, fields the user cannot see stay).
  The form targets the modal so a 422 re-renders it in place; success sets `HX-Retarget: #{entity}-table` + `HX-Reswap: innerHTML` and `CloseModal()`,
  so the fresh table goes to the list. It is generated only when the update command on disk carries `ExtraProperties` **and** a theme is installed
  (only a theme's layout hosts the modal container; Core's legacy shell has none), so `--no-theme` and older CRUD sets get no Edit button.
  The CRUD form resets through the shared `mResetOnSuccess` Alpine component (no inline `hx-on`);
  `AlpineCspSafetyTests` also scans `cli/Templates/ui/*.sbn` for unregistered `x-data`, CDN assets and `hx-on`.
  Gotcha: a Razor **Page** has `HttpContext`, not `Context` (views and `_ViewStart` have `Context`), so the CRUD
  Index page template uses `HttpContext.GetThemeLayout()`. `UiHostWiring` inserts with the file's own line
  ending (never `Environment.NewLine`), or a second wiring pass mis-anchors its `using` insert.
  **`modulus ui eject` / `ui diff`** (every UI view: components, feature-UI pages, Core shared partials, theme layouts): the packages ship their views compiled, so the CLI embeds
  them (`Modulus.Cli.csproj`: `UiViews/{Component}/{View}.cshtml` from Core's `_Default` components, `UiPages/{Group}/...` from each feature UI's `Pages/**`, `UiPages/Shared/Shared/...`
  from Core's `Pages/Shared`, `UiThemes/Tabler/...` from the theme; `UiViewCatalog`). A target is a **component** (`Card`, `Card:Compact`, `--view V`), a **group** named like a
  `UiModuleCatalog` entry (`Users`, `Identity`, `Tenancy`, `Permissions`, `Settings`, `AuditLogging`, `Notifications`, `Files`), `Shared` (Core's `_Alert`/`_UiIcon`) or `Tabler`, or
  **one view**: a page's bare path (`Users/Details`, `Account/Login`) or a theme path (`Tabler/Layouts/Application`). Group names must not collide with component names. Feature UIs
  and the theme must be installed (`UiEject.IsInstalled` reads the host csproj's `PackageReference`/`ProjectReference` `Include`, since a CPM app has no `Version`), else the command
  errors with a `modulus ui add X` hint; `--all` covers every component plus every installed group. `ui eject <target>… | --all [--view V] [--force] [--dry-run] [--list]` writes
  the view where the framework's own lives, so it wins with **no registration**: an app file at the same virtual path beats a package's compiled view (the entry assembly's part is
  first), for `/Pages/...`, `/Themes/Tabler/...` and absolute-path partials alike; components resolve app (`Views/Shared/Modulus/{C}/{V}.cshtml`), theme, then `_Default`. It also
  writes the **nearest `_ViewImports.cshtml`** (marked, when missing): imports are compile-time, an ejected view compiles in the *app's* assembly, so it needs the package's `@using`s,
  tag helpers and `@namespace` beside it (`@model FileResultView` relies on the namespace); `_ViewStart` is found by path at runtime, so it is never copied. An existing file is
  skipped without `--force`. **A page's PageModel stays in the package** (public classes): the app owns the markup, and behavior changes through the services the page uses. Ejected
  theme views use `TablerShell` and `TablerAssets.IsAlpine`, which are therefore public. The first line of an ejected file is a Razor comment
  `@* modulus-eject component= view= base=<sha256/16 of the framework source> framework=<version> *@` before `@page` (line endings are normalized to LF before hashing, so a CRLF
  checkout compares equal). `ui diff [target] [--summary] [--check]` walks the catalog, checks each `AppPath` and classifies every override against the framework's **current**
  view: *identical* (nothing customized; deletable), *customized* (app changed, framework did not since eject), *outdated* (app unchanged, framework changed: `eject --force` takes
  the update), *conflict* (both changed: merge by hand) or *unmarked* (hand-written, no baseline), with a small line diff; an unmarked `_ViewImports` is the app's own and is skipped.
  `--check` exits 1 for outdated/conflict (CI after an upgrade). The CLI version must match the app's framework version for the baseline to mean anything; the embedded views are the
  CLI's own build, not the installed package's. Covered by `UiEjectTests`, `UiEjectPagesTests` and the compile guard `Modulus.UI.Ejection.Tests` (links every ejectable view into a Razor
  test host, so a view that stops compiling in an app's assembly, or loses to its package copy, fails). Verified end to end on a generated web app with the real CLI: all 66 views
  ejected and compiled with 0 warnings, the overrides won at runtime with the right layouts, the ejected login signed in, and `ui diff` reported customized and identical.
  **Per-folder `_ViewStart` / `_ViewImports`.** Each feature UI ships them inside its own page folders (`Account`, `Users` + `Roles`, `Tenancy`, `Permissions`, `Settings`, `AuditLogs`,
  `Notifications`, `Files`; Core: `Pages/Shared`), never at `Pages/` root: several RCLs shipping `/Pages/_ViewStart.cshtml` collide (first wins), which rendered every admin page in
  Identity's login-card `Account` layout once Identity was installed. The theme keeps one `Themes/Tabler/_ViewImports.cshtml`. Guarded by
  `Feature_uis_installed_together_each_keep_their_own_layout` (`FeatureUiRenderTests`, both registration orders).
  **Page authorization.** A feature UI gates its folder only when the app sets its `RequirePermission` (default null = open), so a generated web app calls
  `AddModulusPageAuthorization()` (`Modulus.UI.Core`, next to `AddModulusSmartAuth()` in `Program.cs`): `AuthorizeFolder("/")` plus `AllowAnonymousToFolder("/Account")` (pass folders to
  keep others public), so an anonymous visitor is challenged (the cookie redirect to the login page) and a permission set on a feature UI still applies on top. It uses
  `PostConfigure<RazorPagesOptions>`: `AddRazorPages()` registers a setup that **replaces the conventions collection**, so a plain `Configure` registered before it is silently lost
  (the UI wiring adds `AddRazorPages` after the template's auth block). API endpoints, controllers and the minimal-API error pages are unaffected. An api host has no pages and does
  not call it; a host with `--auth none` has no scheme to challenge with, so it does not either. Covered by `PageAuthorizationTests` and the template assertion in `IdentityBackendTests`.
  **Admin gates** (`UiAccessGates`, `UiModuleDefinition.Gate`): a signed-in user is not an administrator, so `ui add` / `app --ui-modules` also lock the admin UIs to the
  `Admin` role, the role the identity backend seeds the first administrator into: Users `users:manage`, Tenancy `tenancy:view`, Permissions `permissions:view`, Settings
  `settings:manage`, AuditLogging `audit:view`, Files `files:manage` (Identity and Notifications, the user's own inbox, only need the sign-in). Three pieces, all idempotent:
  `appsettings.json` gets `"UsersUi": { "RequirePermission": "users:manage" }` (appended textually so formatting and comments stay; an existing section is the app's choice and is
  never rewritten; the UI reads it when it registers its folder convention), `Program.cs` gets `AddModulusAuthorization()` (the `:`-policy provider) and
  `AddPermissionGrants(grants => grants.GrantToRole("Admin", "users:manage"))` (in-memory seed; switch to the EF grant store for grants edited at runtime). It applies only when
  `Program.cs` has `AddModulusPageAuthorization(` (a host with no sign-in has no role to grant to, so requiring a permission would lock everyone out). The gate names duplicate the
  UI packages' constants (the CLI cannot reference them); `UiAccessGateTests` reads the UI option sources to keep them in step. Verified on a generated web app: the administrator gets
  `/Users` 200, a self-registered user is sent to `/Account/AccessDenied`, an anonymous visitor to the login page. The same wiring adds `AddGrantStorePermissionChecker()`: the
  menu asks `ICurrentUser.HasPermission`, which reads `permission` claims (a cookie sign-in carries none) unless the grant store backs it, so without it every gated menu item, the
  administrator's included, was hidden. **`generate-crud --with-ui`** guards its page the same way when the host has the sign-in (`ModuleModel.RequiredPermission`, else the page
  stays as open as its host): `[Authorize(Policy = "{module}:{route}:manage")]` on the `IndexModel` (`UiAccessGates.CrudPermission`, e.g. `catalog:products:manage`, so `catalog:*`
  covers a module), `requiredPermission:` on the sidecar's nav item, and in `Program.cs` the registry declaration (`AddPermissions("catalog", ...)`, so the Permissions UI lists it) plus
  the Admin grant (`UiCrudWiring.EnsurePagePermission`, idempotent per entity). An existing page keeps its old markup (files are never overwritten), so it stays behind the sign-in only.
  **`ICurrentUser` fix:** `AddModulusIdentity` used `TryAddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>`, but `AddModulus`, `AddMediator` and `AddModulusUi` each `TryAdd` the
  fail-closed `NullCurrentUser` first, so in a generated web app every `ICurrentUser` consumer saw an anonymous user (empty permission-filtered menu, entity-field permissions, audit).
  It now replaces a `NullCurrentUser` registration and keeps any other implementation the app registered (`CurrentUserRegistrationTests`).
  Verified end to end: a generated app (`app` → `generate-crud --with-ui`) built against freshly packed
  UI.Core/Theme.Tabler, booted, and served the Tabler layout, vendored assets, sidebar entry and a working
  htmx create.
- **Known gaps.** Not built yet:
  `IEntityFieldHandler` (extension values in the contributing module's own tables; only the `IHasExtraProperties` JSON bag exists), an edit form
  for `--no-theme` hosts (the modal needs a theme) and
  `IUserUiPreferenceStore`; tiered mode is a later phase. `ui eject` copies views only (a page's handlers/PageModel stay in the package), and static assets
  are customized through the `--m-*` tokens or a same-path file in the app's `wwwroot`. Legacy `_UiLayout` is not ejectable and still ships Core's standard Alpine
  build plus an inline `<style>`.

## Testing notes

- Unit tests use `[Trait("Category", "Unit")]`; integration tests use
  `"Integration"`. Keep this convention so the `--filter` above keeps working.
- CLI tests that scaffold files (they write through `Ux.WriteFile`, which honours the static `Ux.DryRun`) or flip `Ux.DryRun`/`Force`/`Quiet`
  belong to the `[Collection(UxStateCollection.Name)]` collection (runs alone), or they race `UxTests` and fail randomly.
- Integration tests spin up real containers; prefer `IClassFixture`/collection
  fixtures rather than a container-per-test.
