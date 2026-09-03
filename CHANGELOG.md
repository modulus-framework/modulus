# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed — inbox dedup was silently broken in both registration orderings the framework ships (breaking, migration required)

`AddInbox<TContext>`/`AddMongoInbox` decorated `IIntegrationEventHandler<T>`
registrations by mutating `IServiceDescriptor`s at the moment they ran — which
only worked if every handler was already registered first, and only if
`AddInbox` ran once. Neither held:

- **CLI-generated apps** call `AddModulus(...)` (which runs every module's
  `AddInbox`) *before* `AddModulusEvents(...)` registers any handlers — the
  decorator ran against zero handlers, so the inbox silently provided **no
  deduplication at all**.
- **Multi-module apps calling `AddModulusEvents` first** (e.g. TradeFlow, 15+
  modules each calling `AddInbox`) re-wrapped the already-wrapped descriptor on
  every subsequent call, nesting up to N decorators per handler. The outer
  claim always deferred on the inner claim for the same EventId, so every
  integration event dead-lettered without the real handler ever running —
  logged and counted as a dedup hit (`modulus.inbox.dedup_hits`),
  indistinguishable from healthy deduplication.
- Independently, the claim key was the bare EventId with no handler
  discriminator: an event with **more than one** handler had the first to
  claim mark it `Processed`, silently skipping every other handler forever.

**Fixed** by moving the wrap from DI-registration time to dispatch time.
`AddInbox`/`AddMongoInbox` now register a stateless
`IIntegrationEventHandlerDecorator` (new seam in `Modulus.Events.Abstractions`,
`TryAddSingleton` — idempotent across repeated `AddInbox` calls);
`IntegrationEventDispatcher` and `InProcessModuleBus` both wrap each handler
they resolve, at the moment they dispatch — after every handler *and* every
inbox registration has run, regardless of which came first in `Program.cs`.

- **Breaking schema change**: `InboxMessage`/`MongoInboxMessage` gain a
  `HandlerName` column (the wrapped handler's `Type.FullName`); the EF Core
  primary key becomes `(Id, HandlerName)` and the Mongo claim key becomes a
  unique compound index on `(EventId, HandlerName)` instead of relying on
  `_id` alone. **Apps must run `modulus migrate add` per module using
  `AddInbox`** before deploying this version.
- **Migration safety**: rows written before this column existed are honoured
  for *any* handler claiming that EventId (a legacy `Processed`/dead-lettered
  row is skipped for every handler; a legacy row still eligible to claim is
  "adopted" by the first handler that claims it) — an in-flight upgrade
  neither reprocesses an already-handled event nor drops one still mid-flight.
- `IInboxStore.TryClaimAsync`/`MarkProcessedAsync`/`MarkFailedAsync` all gained
  a `handlerName` parameter — a breaking change to the (rarely
  directly-implemented) `IInboxStore` interface.

### Fixed — Quartz delayed/recurring jobs ran with no ambient tenant

`QuartzJobScheduler.EnqueueAsync` populated `tenantId`/`correlationId` in the
job's `JobDataMap`, but `ScheduleAsync` (delayed) and `AddRecurringAsync`
(cron) built theirs with only `["args"]` — `QuartzJobAdapter` silently opens
no tenant scope when those keys are absent, so on the framework's only
production-durable scheduler, every delayed or recurring job ran outside
tenant isolation while immediate jobs worked correctly, with nothing logged.
All three scheduling paths now build their `JobDataMap` through one shared
helper. New `Modulus.BackgroundJobs.Quartz.Tests` project (none existed) adds
regression coverage for all three paths.

### Fixed — `MemoryCacheService` had no tenant scoping, unlike `RedisCacheService`

`RedisCacheService.TagKey` prefixes tag keys with the ambient tenant id so two
tenants sharing a tag name (e.g. `"catalog"`) can't invalidate each other's
entries; `MemoryCacheService`'s tag index was a flat dictionary keyed on the
raw tag with no tenant awareness at all. In a multi-tenant app running the
in-memory cache — the default — two tenants using the same tag name could
invalidate each other's cache entries. `MemoryCacheService` now applies the
identical `TagKey` scoping as `RedisCacheService`, so the two
`ICacheService` implementations behave the same regardless of which one an
app has wired up.

### Fixed — background-job/lock failures were compiled out of Release builds

Three catch blocks reported failures via `System.Diagnostics.Debug.WriteLine`,
which is `[Conditional("DEBUG")]` and therefore removed entirely from Release
builds: `QuartzJobScheduler`'s recurring-job schedule/remove failures, and
`RedisDistributedLock`'s failed lock-release. All three ran on fire-and-forget
paths with nothing else to surface the error, so a production failure to
schedule a recurring job or release a distributed lock was silently invisible
— no log, no metric, no exception. Both types now take an injected `ILogger`
and log at `LogError`/`LogWarning`, matching `ChannelJobQueue`'s existing
pattern.

### Changed — explicit module registration (breaking)

The `[DependsOn]` module-dependency mechanism has been removed. Modules are now
registered explicitly in `Program.cs`; registration order is authoritative for
every lifecycle phase (config phases, `InitializeAsync`; `ShutdownAsync` runs in
reverse).

- **Removed**: `[DependsOn]` attribute, `IModule.DependsOn`, `ModuleGraph`
  (topological sort / cycle detection), `ModuleDependencyNotFoundException` /
  `ModuleDependencyResolutionException` / `ModuleCycleException`, and the
  `AddModulus<TStartupModule>(configuration)` startup-module overload.
- **Added**: `AddModulus(IConfiguration, Action<ModulusBuilder>)` +
  `ModulusBuilder.AddModule<T>()` / `AddModule(Type)` — Program.cs is the
  composition root; duplicate registrations are ignored (idempotent). Generated
  apps no longer ship an `{App}HostModule`.
- **CLI**: `modulus add-module` now wires `modules.AddModule<{Module}Module>()`
  into Program.cs instead of `[DependsOn]` on a host module (detects the
  `AddModulus(` anchor; refuses with migration guidance on legacy
  `AddModulus<` apps).
- **Observability**: `GET /health/graph` now returns an ordered module
  inventory (`name`, `type`, `initOrder`) instead of a mermaid dependency graph.
- Migrating: replace `builder.Services.AddModulus<HostModule>(config)` with an
  explicit `AddModulus(config, modules => modules.AddModule<A>()...)` listing
  every module (the old host module's `[DependsOn]` list is the source of
  truth for the order), then delete the host module class.

### Fixed — CLI template & testing-harness bugs (found by regenerating an app e2e)

- **`NuGet.config` template generated invalid XML** — the guidance comment
  contained `--package-source`, and `--` is illegal inside XML comments, so
  every freshly generated app failed restore with "NuGet.Config is not valid
  XML". Comment reworded to avoid the double dash.
- **`Program.cs` template missed namespace imports** — the host usings never
  included `Modulus.AspNetCore.{Correlation,Cors,FeatureFlags,HealthChecks,
  Idempotency,OpenApi,RateLimiting,Security,Versioning}`, so every generated
  app failed to compile its own middleware wiring (`AddModulusCorrelation`,
  `AddModulusRateLimiting`, …). All nine imports added.
- **`ModuleBoundaryRules` was inoperative** — it scanned only `Modulus.*`
  framework assemblies (never the app's own modules/events) and resolved
  `typeof(IModule)` against a local placeholder interface nothing implements,
  so `FindModuleTypes()` always returned empty and app-owned integration
  events were never name-checked. Now scans all non-dynamic assemblies
  (ReflectionTypeLoadException-safe), skips abstract bases (no more
  `IntegrationEventBase` false positive), and uses the real
  `Modulus.Core.Abstractions.IModule`.

### Fixed — Production-hardening pass

Security & correctness fixes across transports, identity, tenancy, and the
request pipeline:

- **Repo hygiene** — tracked `build/*.props` (imported unconditionally by
  `Directory.Build.props` but previously gitignored); removed
  `.claude/settings.local.json` from the index.
- **Kafka consumer** — failed deliveries are re-seeked with capped exponential
  backoff (`MaxDeliveryAttempts`, default 5); exhausted messages are committed
  past and logged instead of hot-looping the partition.
- **Broker dispatch context** — RabbitMQ and Kafka consumers restore the
  ambient tenant/correlation scope around handler invocation via the shared
  `EnvelopeAmbientScope`, matching HTTP-request semantics downstream.
- **`TransactionBehavior`** — dedupes resolved contexts by runtime type and
  begins transactions inside the try block, so a failure while starting a
  transaction rolls prior contexts back cleanly.
- **Identity token endpoint** — the password-grant subject-activity check now
  inspects the principal produced by sign-in (previously the ambient
  controller user, anonymous during token issuance); default granted scopes
  include `openid` and `offline_access`.
- **External IdP token validators** — OIDC discovery validators are cached per
  metadata-address/audience set; all five adapters (Auth0, Okta, AzureAd,
  Duende, Authentik) gained optional `Audience` validation; fixed the AzureAd
  v2.0 authority's token-endpoint path; Duende userinfo calls authenticate with
  client credentials; subject lookups are URL-escaped.
- **CLI templates** — `Modulus.Data.Abstractions` package references follow the
  framework version instead of a hardcoded `1.0.0`; generated apps can embed a
  local package feed via `modulus app --package-source`; `--database` /
  `--migration-engine` values are validated up front with clear errors.
- **Query cache isolation** — `CachingBehavior` includes the ambient tenant id
  in cache keys, closing a cross-tenant response leak between tenants whose
  requests serialise identically.
- **Rate limiting** — a custom evictable fixed-window limiter plus background
  sweeper removes idle partitions, bounding memory under per-user/per-IP churn;
  options are bound once so `IOptions` and middleware behaviour cannot diverge.
- **HTTP idempotency** — replay keys are scoped by tenant *and* authenticated
  caller; bodies larger than `IdempotencyOptions.MaxResponseBytes` (default
  1 MB) are executed but not cached; `Date` and `Set-Cookie` headers are never
  replayed.
- **Subdomain tenant resolver** — host matching requires the dot boundary
  (`.baseDomain`), blocking spoofed hosts such as `attacker-modulus.app`;
  requests to the bare domain resolve to no tenant instead of throwing; slugs
  are length- and charset-validated before hitting the store.
- **Outbox writer routing** — direct `IOutboxWriter.WriteAsync` callers resolve
  the owning module context through `IEntityContextMap` instead of whichever
  `DbContext` happened to be registered last.
- **SQLite in-memory provider** — each context gets its own uniquely named
  shared-cache database (sharing one name made second+ modules' schema setup
  silent no-ops), and a real opened keep-alive connection replaces the
  lazily-created keyed singleton nothing ever resolved.
- **Role claims** — server-side permission resolution accepts both
  `ClaimTypes.Role` and short-form `role`, so tokens validated with inbound
  claim mapping disabled authorise correctly.

### Added — Production-hardening pass

- **Outbox purge** — EF Core and MongoDB processors delete dispatched and
  dead-lettered rows older than `OutboxOptions.PurgeAfterDays` (default 7,
  0 disables) in bounded batches, running before the empty-poll short-circuit
  so steady-state tables stay bounded.
- **dbsh migration engine** — SQL-first alternative to EF Core migrations.
  Each module can independently use EF Core or dbsh; the CLI auto-detects
  the engine per module and dispatches `dotnet ef` or `dbsh` accordingly.
  `--migration-engine dbsh` on `modulus app` / `modulus add-module` generates
  `Database/Config/migration.json` (provider + `${VAR}` connection) and
  `Database/Migrations/{Module}/` for hand-written `.sql` files.
  `ExternallyManaged<TContext>()` marks the context so startup skips it.
  `modulus doctor` validates `dbsh` availability. `modulus migrate add`
  scaffolds a SQL stub; `modulus migrate update` runs `dbsh init && dbsh migrate`.
- **Release workflow gates** — tag-triggered releases run unit *and*
  integration tests before packing/publishing.
- Package `<Description>` metadata filled in for every library; README and
  AGENTS.md project counts/layout brought in line with the actual tree
  (31 src projects).

### Changed — Package consolidation (55 → 23)

The framework was consolidated from 55 packages down to 23 focused packages
(the solution has since grown back to 31 as new opt-in providers landed).
Namespaces are preserved — types keep their original namespaces (e.g.
`Modulus.Core.Abstractions.IModule`) even when compiled into a different
assembly. Only `<ProjectReference>` / `<PackageReference>` names changed.

- **Merged abstractions into implementations:**
  `Core.Abstractions` → `Core`, `EFCore.Abstractions` → `EFCore`,
  `Mediator.Abstractions` → `Mediator`, `Events.Abstractions` → `Events`,
  `Inbox.Abstractions` → `Inbox`, `SignalR.Abstractions` → `Platform`,
  `Identity.Abstractions` → `Identity`.
- **`Outbox.Abstractions` kept separate** — it is the seam that prevents a
  circular dependency (`EFCore` → `Outbox.Abstractions`, `Outbox` → `EFCore`).
- **Merged platform services:** `MultiTenancy`, `Authorization`,
  `BackgroundJobs`, `Caching`, `Storage`, `SignalR` → `Modulus.Platform`.
- **Merged identity adapters:** 6 external IdP validators + EF Core mapping →
  `Modulus.Identity`.
- **Merged observability:** `Diagnostics` + `OpenTelemetry` →
  `Modulus.Observability`.

### Removed — Dropped stubs

The following stub/unmaintained packages were removed (can be re-added as
needed): Cassandra, CosmosDB, DynamoDB, Elasticsearch, Redis, Dapper,
`EventBus.ServiceBus`, `EventBus.Sqs`, `SignalR.Azure`, `SignalR.Redis`,
`BackgroundJobs.Hangfire`, `BackgroundJobs.Quartz`, and `Modulus.Benchmarks`.

### Added

- **CLI scaffolding tool** (`Modulus.Cli`) — Spectre.Console.Cli + Scriban
  `dotnet tool` that generates complete solutions, modules, and CRUD code
  (`modulus app`, `modulus module`, `modulus add-module`, `modulus generate-crud`),
  replacing the previous `dotnet new` templates and `Modulus.App` sample.

### Fixed — Architectural defects

- **Transactional outbox (dual-write):** `ModuleDbContext.SaveChangesAsync`
  now enqueues domain events that implement `IIntegrationEvent` to
  `IIntegrationEventOutbox` (backed by `EfOutboxWriter`) *before* calling
  `base.SaveChangesAsync`, so the outbox row(s) participate in the same DB
  transaction. The outbox was previously registered but completely unwired.
  `EfOutboxWriter` now implements both `IOutboxWriter` and
  `IIntegrationEventOutbox`, resolving `DbContext` lazily via
  `IServiceProvider` to break the circular DI dependency.
- **Outbox row-locking & retries:** `OutboxProcessor` claims rows atomically
  via an `ExecuteUpdateAsync` whose `WHERE` re-checks `LockedUntil`
  (provider-agnostic `FOR UPDATE SKIP LOCKED` equivalent), so multiple app
  instances no longer duplicate-dispatch every event. Failed dispatches
  schedule exponential backoff (`NextAttemptAt`) and dead-letter after
  `MaxRetries` instead of being silently dropped. `OutboxProcessor` is now
  registered in DI.
- **Inbox dedup (EF Core & MongoDB):** `AddInbox<TContext>` and
  `AddMongoInbox` now decorate all `IIntegrationEventHandler<T>`
  registrations with an idempotent decorator backed by a common
  `IInboxStore` (`EfInboxStore` / `MongoInboxStore`). Previously the inbox
  stores were registered but the handler pipeline was never decorated —
  providing zero dedup. The EF Core decorator also resolves the inner handler
  via `ActivatorUtilities.CreateInstance` instead of
  `GetRequiredService(ImplementationType)`.
- **`IdempotentIntegrationEventHandler`:** claims the row atomically via the
  EventId PK (the loser defers via `DbUpdateException` →
  `InboxDeferralException`). No longer double-executes on redelivery and
  dead-letters after `InboxOptions.MaxRetries` instead of hot-looping.
- **`TransactionBehavior`:** now starts an explicit `BeginTransactionAsync`
  on *every* resolved `DbContext`. `AddModuleDatabase<TContext>` now also
  registers the context as `DbContext` so the behavior can discover it (the
  previous `GetServices<DbContext>()` returned zero items). Each context runs
  in its own independent DB transaction; for cross-module consistency prefer
  the transactional outbox.
- **Multi-tenancy query filter:** `ModuleDbContext` now captures the
  `ICurrentTenant` service field (not a value), registers the filter
  unconditionally, and degrades to match-all when no tenant is in scope (no
  more `Guid.Empty` leak). Soft-delete + tenant predicates are combined to
  honour EF's one-filter-per-entity rule.
- **`ICurrentTenant` async flow:** backed by a static
  `AsyncLocal<TenantInfo?>` with a `Change(...)` scope API, so tenant context
  flows into background jobs / message consumers / hosted services.
- **Identity password grant (auth bypass):** the token endpoint previously
  minted tokens for *any* username with zero credential check. It now
  delegates to an `IPasswordGrantCredentialValidator`;
  `AddModulusOpenIddict` registers a `NullPasswordGrantCredentialValidator`
  (deny-by-default) until `AddModulusIdentity` replaces it with
  `IdentityPasswordGrantValidator<TUser>` (SignInManager +
  `CheckPasswordSignInAsync`, honours `IsActive` and lock-out). Granted scopes
  are intersected with a registered allow-list. The refresh-token branch now
  returns a proper `invalid_grant` error instead of a bare `Forbid()`.
- **External IdP token validation:** the Auth0, Okta, Azure AD, Duende, and
  Authentik adapters previously validated bearer tokens by GETting the
  userinfo endpoint and treating `200` as valid. They now use a shared
  `OidcDiscoveryValidator` that fetches the provider's JWKS via OIDC
  discovery and locally checks the signature, issuer, and lifetime
  (1-min clock skew). Audience validation is opt-in. Keycloak is unchanged
  (already used RFC 7662 introspection).
- **NoSQL tenant fallback:** `MongoTenantFilter` and `ElasticRepository` no
  longer filter on `Guid.Empty` in host context; they return match-all.
- **Other defects:** `LocalFileStorage` path traversal;
  `GlobalExceptionHandler` caught the wrong `ValidationException` type;
  `OutboxPollingService` aborted on any non-OCE exception;
  `NullCurrentUser`/`NullPermissionRegistry` were fail-open;
  `PagedList.TotalPages` divide-by-zero; `ModuleNotFoundException` literal
  message; SignalR `EnableDetailedErrors` shipped to all clients.

## [1.0.0] - 2025-01-01

### Added

- Initial framework release.
- Modular monolith architecture with topological module loading.
- DDD building blocks (`AggregateRoot<TId>`, `IDomainEvent`, `IModule`).
- CQRS mediator with pipeline behaviors (logging, validation, authorization,
  transaction).
- Event bus with InMemory, RabbitMQ, and Kafka providers.
- Transactional Outbox and Inbox patterns.
- Multi-tenancy with header, JWT claim, and subdomain resolution.
- OpenIddict identity with 6 external IdP adapters.
- Data providers: SQL Server, PostgreSQL, MySQL, SQLite, MongoDB.
- OpenTelemetry observability.
