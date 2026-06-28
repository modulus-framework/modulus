# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed — Package consolidation (55 → 23)

The framework was consolidated from 55 packages down to 23 focused packages.
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
