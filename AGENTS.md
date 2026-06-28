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
| Install CLI tool | `dotnet tool install -g --add-source ./nupkg Modulus.Cli` |
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
                 BackgroundJobs + Caching + Storage + SignalR merged),
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

## CLI tool

The `modulus` CLI (Spectre.Console.Cli + Scriban) is a `dotnet tool` that
generates complete solutions, modules, and CRUD code:

| Command | Description |
|---------|-------------|
| `modulus app <name>` | Creates a modular-monolith solution with Host + example module |
| `modulus module <name>` | Creates a new business module project |
| `modulus add-module <name>` | Adds module to existing app + wires `[DependsOn]` + `ProjectReference` |
| `modulus generate-crud <Entity>` | Generates entity, repo, DTOs, command/query handlers |

Templates are embedded Scriban resources under `src/cli/Modulus.Cli/Templates/`.

## Known review findings (high-impact, not yet fixed)

These are architectural defects that need design discussion before fixing. See
the review notes and do not silently change them:

- **Transactional outbox (dual-write)** — FIXED: `ModuleDbContext.SaveChangesAsync` now enqueues domain events that implement `IIntegrationEvent` to `IIntegrationEventOutbox` (backed by `EfOutboxWriter`) BEFORE calling `base.SaveChangesAsync`, so the outbox row(s) participate in the same DB transaction. `AddModuleDatabase` registers a `NullIntegrationEventOutbox` by default (no-op); `AddOutbox<TContext>` replaces it with `EfOutboxWriter`. The original bug was worse than "caller must call WriteAsync before CommitAsync": nobody called `IOutboxWriter.WriteAsync` at all — the outbox was registered but completely unwired. `EfOutboxWriter` now implements both `IOutboxWriter` and `IIntegrationEventOutbox`, resolving `DbContext` lazily via `IServiceProvider` to break the circular DI dependency (ModuleDbContext → outbox → DbContext → ModuleDbContext).
- **Inbox (MongoDB)** — FIXED: `AddMongoInbox` now calls `DecororateIntegrationEventHandlers()` (the same decorator wiring as `AddInbox<TContext>`), so all `IIntegrationEventHandler<T>` registrations are wrapped with the idempotent decorator backed by `MongoInboxStore`. The original bug: `MongoInboxStore` was registered but the handler pipeline was never decorated — `AddMongoInbox` provided zero dedup. A common `IInboxStore` interface now backs both EF Core (`EfInboxStore`) and MongoDB (`MongoInboxStore`), so the decorator logic is shared.
- **`TransactionBehavior` enlists only the first registered `DbContext`** — FIXED: now starts an explicit `BeginTransactionAsync` on *every* resolved `DbContext` (see below). The original bug was actually worse than "first context only": `AddDbContext<T>` does not register the context as `DbContext`, so `GetServices<DbContext>()` returned **zero** items and the behavior silently skipped transaction wrapping entirely. `AddModuleDatabase<TContext>` now also registers the context as `DbContext` so the behavior can discover it. Multi-context caveat: each context runs in its own independent DB transaction (true cross-connection atomicity needs 2PC/MSDTC); for cross-module consistency prefer the transactional outbox.
- 5 empty test projects removed; `Modulus.App` template replaced by CLI
  (`modulus app`).

## Already addressed (recent work)

These were flagged in the initial review and have been fixed; kept here so the
history is discoverable:

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
