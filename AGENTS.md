# AGENTS.md

Guidance for AI agents (and humans) working on the Modulus framework.

## Project

Modulus is a modular-monolith framework for **.NET 10** (`net10.0`). It is a
multi-project solution made of **23 libraries** under `src/` (core, data,
identity, messaging, platform, observability) plus unit/integration tests under
`tests/` and a **CLI tool** (`Modulus.Cli`) for scaffolding.

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
                 Modulus.Sagas (Rebus-based)
  platform/      Modulus.Platform (MultiTenancy + Authorization +
                 BackgroundJobs + in-memory Caching + local Storage +
                 in-process SignalR — NO heavy cloud SDKs). Cloud providers are
                 separate packages so their SDKs are opt-in:
                 Modulus.Storage.S3 (AWSSDK.S3), Modulus.Storage.AzureBlobs
                 (Azure.Storage.Blobs), Modulus.Caching.Redis (StackExchange.Redis),
                 Modulus.SignalR.Backplane (Redis/Azure SignalR backplane),
                 Modulus.MultiTenancy.EntityFrameworkCore (EF-backed ITenantStore
                 — keeps EF Core out of Modulus.Platform).
  observability/ Modulus.Observability (Diagnostics + OpenTelemetry merged)
  cli/           Modulus.Cli (Spectre.Console.Cli scaffolding tool)
tests/
  unit/          xUnit + NSubstitute + FluentAssertions (7 projects)
  integration/   xUnit + Testcontainers (2 projects)
```

## Module system

Modules implement `IModule` or inherit from `ModulusModule`. Dependencies are
declared via `[DependsOn(typeof(OtherModule))]` attributes (ABP-style).
`AddModulus<TStartupModule>(configuration)` auto-discovers the full module graph
via topological sort.

```csharp
[DependsOn(typeof(IdentityModule), typeof(DataModule))]
public sealed class ShopModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection s, IConfiguration c)
    {
        // Register module-specific services
    }
}

// Program.cs
builder.Services.AddModulus<AppHostModule>(builder.Configuration);
```

Service registration runs in three ordered phases across all modules (each in
dependency order): `PreConfigureServices` for every module, then
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
| `modulus app <name>` | Creates a solution: `src/API/{App}.Api` host + `src/Shared/{App}.Shared.*` kernel (4 projects) + example `Catalog` module (4 projects) + top-level tests |
| `modulus module <name>` | Creates a blank 4-layer business module |
| `modulus add-module <name>` | Adds a module to an existing app + wires `[DependsOn]` + Host `ProjectReference`s |
| `modulus generate-crud <Entity>` | Generates entity, repo, DTOs, command/query handlers across the module's layers |
| `modulus migrate add <Name>` | Scaffolds an EF Core migration in each module's Infrastructure project (or one via `--module`) |
| `modulus migrate update` | Applies pending migrations to each module's database (`dotnet ef database update` per module) |

### Generated layout

```
{App}/
├── {App}.slnx
├── Directory.Build.props        # net10.0, Nullable, CPM off (explicit Versions)
├── .editorconfig
└── src/
    ├── API/{App}.Api/                        # composition root (single executable)
    │   ├── Program.cs                        # AddModulus<>, AddMediator; MigrateModulusDatabasesAsync over all DbContexts
    │   └── Modules/{App}HostModule.cs        # [DependsOn] lists every business module
    ├── Shared/                               # shared kernel (4 projects)
    │   ├── {App}.Shared.Domain
    │   ├── {App}.Shared.Application
    │   ├── {App}.Shared.Infrastructure
    │   └── {App}.Shared.Presentation
    └── Modules/                              # feature modules (4 projects each)
        └── {App}.Modules.{Module}/
            ├── .Domain/          # entity, IRepository
            ├── .Application/     # IUnitOfWork, commands/queries/handlers + Dtos/ + IntegrationEvents/
            ├── .Infrastructure/  # {Module}DbContext, {Module}DbContextFactory (design-time), Migrations/, repository impl, {Module}Module composition root
            └── .Presentation/    # API controllers
```

Each module gets **four** projects named `{App}.Modules.{Module}.{Layer}`:

| Layer | Contains | References |
|-------|----------|------------|
| `Domain` | Entity, `IRepository` | `Shared.Domain`, `Modulus.Core`, `Modulus.Data.Abstractions` |
| `Application` | **`IUnitOfWork`**, commands, queries, handlers, **DTOs** (`Dtos/`), **integration events** (`IntegrationEvents/`) | `Domain`, `Shared.Application`, `Modulus.Mediator`, `Modulus.EntityFrameworkCore`, `Modulus.Events` |
| `Infrastructure` | **`{Module}DbContext`**, **`{Module}DbContextFactory`** (design-time), `Migrations/`, repository impl, `{Module}Module` composition root | `Application`, `Domain`, `Shared.Infrastructure`, `Modulus.EntityFrameworkCore`, `Modulus.Events`, EF Core provider package |
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

A working example lives at `samples/ModulusSampleErp` (API host + Users module,
SQLite). Because the `Cobytelabs.Modulus.*` packages aren't on nuget.org yet, the
sample ships a `NuGet.config` pointing at the repo's local `nupkg/` feed — run
`dotnet pack modulus.slnx -c Release` first if the feed is empty.

Templates are embedded Scriban resources under `cli/Templates/`
(`app/`, `shared/`, `module/{Domain,Application,Infrastructure,Presentation}/`).

## Known review findings (high-impact, not yet fixed)

These are architectural defects that need design discussion before fixing. See
the review notes and do not silently change them:

- **Transactional outbox (dual-write)** — FIXED: `ModuleDbContext.SaveChangesAsync` now enqueues domain events that implement `IIntegrationEvent` to `IIntegrationEventOutbox` (backed by `EfOutboxWriter`) BEFORE calling `base.SaveChangesAsync`, so the outbox row(s) participate in the same DB transaction. `AddModuleDatabase` registers a `NullIntegrationEventOutbox` by default (no-op); `AddOutbox<TContext>` replaces it with `EfOutboxWriter`. The original bug was worse than "caller must call WriteAsync before CommitAsync": nobody called `IOutboxWriter.WriteAsync` at all — the outbox was registered but completely unwired. `EfOutboxWriter` now implements both `IOutboxWriter` and `IIntegrationEventOutbox`, resolving `DbContext` lazily via `IServiceProvider` to break the circular DI dependency (ModuleDbContext → outbox → DbContext → ModuleDbContext).
- **Inbox (MongoDB)** — FIXED: `AddMongoInbox` now calls `DecorateIntegrationEventHandlers()` (the same decorator wiring as `AddInbox<TContext>`), so all `IIntegrationEventHandler<T>` registrations are wrapped with the idempotent decorator backed by `MongoInboxStore`. The original bug: `MongoInboxStore` was registered but the handler pipeline was never decorated — `AddMongoInbox` provided zero dedup. A common `IInboxStore` interface now backs both EF Core (`EfInboxStore`) and MongoDB (`MongoInboxStore`), so the decorator logic is shared.
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
  when its last connection closes, so the factory opens a **keep-alive per
  context** (from the built host's own connection strings — the per-context map
  only exists after `ConfigureTestServices` runs during host build) and then
  re-runs `EnsureCreated` per context. Isolation is by a unique `Cache=Shared`
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
  A working `samples/ModulusSampleErp` (API host + Users module, SQLite)
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

## Testing notes

- Unit tests use `[Trait("Category", "Unit")]`; integration tests use
  `"Integration"`. Keep this convention so the `--filter` above keeps working.
- Integration tests spin up real containers; prefer `IClassFixture`/collection
  fixtures rather than a container-per-test.
