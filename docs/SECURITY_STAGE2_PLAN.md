# Security stage 2: Company = Tenant

Roadmap for the security foundation required by the ERP Framework Guideline v2 (Part A invariants, Build
Order stage 2): company isolation, explicit membership, a resolved policy for every endpoint, tamper-evident
audit and CI-blocking isolation tests.

Status (2026-10-03): **Phases 1 to 5 are built in the framework**, except the UI items (Files UI tenant roots, the
Security tab of `Modulus.UI.AuditLogging`), which were deferred by request, and the CLI items that need a
`modulus app --multi-tenancy` option (1.8, the per-entity isolation test of 5). The samples (`samples/TradeFlow`)
are not changed by this work. Checkboxes are updated as work lands; where the build differs from the plan, the
original text is kept and the difference is noted inline as **As built:** or **Deviation:**.

## Decisions

- **Company = Modulus tenant** (`ICurrentTenant`, `TenantInfo`). Company isolation is tenant isolation.
- **Branch / Location = org units** (`IOrgHierarchy`, `OrgPlacement`, `IHasOrgUnit`), which are already
  tenant-scoped. Inheritance down the hierarchy happens only through an explicit placement mode
  (`UnitOnly` / `UnitAndDescendants` / `UnitAndAncestors`), never implicitly.
- **Group = a set of tenants** (`TenantInfo.GroupId`). Group-level views go through a federated read path
  (later stage), never a cross-tenant query.
- **Multi-company access: one login + server-side membership check** (guideline B1 option 1). The selected
  company (header, subdomain, switcher) is allowed only when a membership row exists, so revoking a membership
  takes effect immediately. A `tid` claim still pins a token to one company (audience-bound / AI tokens).
- Network trust (B4) and AI / MCP (Part D) are stages 4 and 5; `ISecurityContext` reserves their slots.

## Context (gaps found before this work)

- **Membership.** `ModulusUser.TenantId` holds a single tenant and there is no membership concept. Host accounts
  (no `tid`) can enter any tenant by header unless `RequireHostTenantAccessPolicy` is set (`TenantMiddleware`).
- **Messages and jobs.** Every message/job path trusts the tenant id: `new TenantInfo(id, "")` with no store or
  IsActive check. The paths are `OutboxProcessor`, `EnvelopeAmbientScope`, Sagas `AmbientContextIncomingStep`,
  `ChannelJobQueue` and `QuartzJobAdapter`. In the inbox, `InboxMessage.TenantId` is never set.
- **Writes.** `ModuleDbContext` stamps a tenant on Added entities but accepts an explicit foreign `TenantId`.
- **Endpoints.** No fallback authorization policy is configured. Only REPR endpoints default to authenticated;
  minimal-API `IEndpoint`s have no default, and nothing enumerates `EndpointDataSource`.
- **Audit.** Only `InMemoryAuditLogStore` exists. There is no hash chain, and business and security audit are
  not separated.
- **Storage.** Storage paths are caller-supplied with no tenant prefix (Local, S3, Azure Blobs).
- **Database.** There is no database-enforced isolation on any provider: no RLS, no session context, no raw-SQL
  guard, and MongoDB relies on each repository remembering `MongoTenantFilter`.
- **Tests.** `TestAuthHandler` never emits `tid`, so test principals act as host accounts.

Every phase leaves `dotnet build` at 0 warnings, `Category=Unit` green and `dotnet format` clean, updates
`PublicAPI.Unshipped.txt` and AGENTS.md, and finishes before the next phase starts.

## Phase 1: Tenancy foundation

### 1.1 Tenant membership
- [x] **`ITenantMembershipStore`** (Platform `MultiTenancy/`) with two methods:
  - `IsMemberAsync(userId, tenantId)`
  - `ListTenantIdsAsync(userId)`
- [x] **Implementations:**
  - In-memory default (`InMemoryTenantMembershipStore`, seed with `Add`). It starts empty, so `RequireMembership()` without a real store fails closed.
  - `EfTenantMembershipStore` (`Modulus.MultiTenancy.EntityFrameworkCore`, table `ModulusTenantMemberships`, PK (UserId, TenantId), `IsActive`, `CreatedAtUtc`). It counts only active memberships of active tenants. `TenantManager.AddMemberAsync` / `RemoveMemberAsync` grant and revoke; revoking sets the row inactive, and re-adding re-activates it.
- [x] **`TenantMiddleware`:** when `RequireMembership()` is on, an authenticated caller without `tid` who selects a tenant must be a member, else `403`.
  - `HostTenantAccessPolicy` stays as the break-glass override (logged as a warning; Phase 4 audits it).
  - A `tid` claim keeps pinning the token.
  - A caller with no user id claim (`NameIdentifier` / `sub`) is refused.
- [ ] **CLI templates** turn `RequireMembership()` on; the library default stays off for one release. Moved to 1.8 (see there).

### 1.2 Group
- [x] `TenantInfo.GroupId` (optional, defaulted) and `TenantEntity.GroupId` (indexed).
- [x] `ITenantStore.ListByGroupAsync(groupId)` (default interface method; `EfTenantStore` queries the index).

### 1.3 `ISecurityContext`
- [x] **Core abstraction:** `Kind`, `User`, `CompanyId`, `GroupId`, `BranchId`, `Network` (always `Unverified` for now), `Agent` (always null for now), `CorrelationId`.
  - It composes `ICurrentTenant`, `ICurrentUser`, `ICurrentDataScope` and `ICorrelationContext`.
  - `Kind` is `System` when there is no HTTP request and no authenticated user (jobs, consumers).
- [x] **Branch selection:** optional `X-Branch-Id` (`BranchContextMiddleware`), accepted only when the caller's org scope includes the unit, else `403`. Repeated or malformed headers and anonymous callers are refused too.
- [x] **Registration:** `AddModulusSecurityContext()` + `UseModulusSecurityContext()` (after authentication) in Platform; `NullSecurityContext` is the Core default.

### 1.4 Verified tenant restore for messages and jobs
- [x] **Seam:** `ITenantContextRestorer.VerifyAsync(tenantId)` (Core) returns the verified `TenantInfo`. `AddMultiTenancy` registers `VerifiedTenantContextRestorer`, which resolves the id through `ITenantStore` (30 s cache) and throws `TenantContextRejectedException` for unknown or inactive tenants. Without multi-tenancy the id is applied unverified.
  - **As-built lesson:** verifying and entering are two steps. `sp.EnterTenant(await sp.VerifyTenantAsync(id, ct))` enters the tenant in the frame that runs the work. A first version called `ICurrentTenant.Change` inside the async restorer, and since an `AsyncLocal` written inside an `async` method does not flow back to the caller, the tenant was never ambient. The tests now use a real `AsyncLocal` tenant to catch this.
- [x] **Call sites switched over:**
  - `OutboxProcessor` and `MongoOutboxProcessor`: a rejected tenant dead-letters the row at once (`RetryCount = MaxRetries`).
  - RabbitMQ / Kafka consumers through `EnvelopeAmbientScope.VerifyTenantAsync` + `Restore`: RabbitMQ nacks without requeue, Kafka routes to the DLQ with reason `tenant-rejected`.
  - Sagas (`AmbientContextIncomingStep`): the message ends in the error queue.
  - `ChannelJobQueue` and Quartz: the job is dropped (counted as failed and logged).
- [x] **Inbox:** `EfInboxStore` and `MongoInboxStore` stamp `InboxMessage.TenantId` from the ambient tenant.

### 1.5 Write-side guard
- [x] `ModuleDbContext.SaveChangesAsync` throws `CrossTenantWriteException` (Core; mapped to `403` by `GlobalExceptionHandler`) outside the host context when:
  - an Added `IHasTenantId` is stamped with another tenant (an unstamped one is still stamped with the current tenant)
  - a tracked row's `TenantId` changed
  - a Modified or Deleted row carries another tenant's id (for example an attached stub)
  - no tenant is resolved and the row carries any tenant id (only tenant-less rows may be written)

  Limit: an attached stub that lies about its tenant id still reaches the row by primary key. Closing that is the database's job (Phase 3 RLS / per-tenant databases).

### 1.6 Tenant-scoped storage
- [x] `TenantScopedFileStorage` decorator prefixes `tenants/{id:N}/` (host: `host/`), behind `StorageOptions.IsolateTenants` (`Storage:IsolateTenants`, library default off).
  - Rooted paths and `.` / `..` segments are rejected (the local provider canonicalizes `..`, which would otherwise reach a sibling tenant's folder).
  - No tenant resolved means every call throws (fail-closed).
  - Every provider registers through `UseFileStorageProvider<T>()` (local, S3, Azure Blobs), which wraps at resolution time, so the order of `Configure` and provider registration does not matter.
  - Files UI: still path-addressed, but inside the tenant's prefix once isolation is on.

### 1.7 Testing harness
- [x] `TestAuthHandler`: an `X-Test-TenantId` header becomes the `tid` claim (`TestAuthDefaults.TenantIdHeader`).
- [x] `CreateAuthenticatedClient(..., tenantId, pinTenant: true)` option. A membership is seeded through the app's own store (`InMemoryTenantMembershipStore.Add` or `TenantManager.AddMemberAsync`), so `Modulus.Testing` takes no dependency on `Modulus.Platform`. This means there is no `SeedMembershipAsync` helper.

### 1.8 CLI templates
- [ ] Program template wires `RequireMembership()`, `AddModulusSecurityContext()` and `IsolateTenants`.
- [ ] Identity seeding adds the seeded admin's membership.

  **Deferred:** generated apps do not use multi-tenancy at all today (no `AddMultiTenancy`, no tenant store), so these lines have nothing to attach to. They land with a `modulus app --multi-tenancy` option, which needs its own design (tenant store module, resolver choice, seeding). Until then the library pieces above are opt-in.

  **Migration note:** existing EF tenant-store databases need the new `ModulusTenantMemberships` table and the `ModulusTenants.GroupId` column. The tenant store ships no migrations, so an app that owns that schema adds them.

## Phase 2: Endpoint policy model and startup guard

- [x] **Endpoint security metadata** (Core, `Modulus.Core.Abstractions.Security`, no ASP.NET dependency, so every package can use it):
  - `LoosenedAttribute(reason) { Ticket, Framework }`: why an endpoint is anonymous. It works both as endpoint metadata and as an attribute next to `[AllowAnonymous]`; the attribute alone opens nothing.
  - `EndpointSecurityPolicyAttribute { Classification, Network }` with `DataClassification` (Unspecified/Public/Internal/Confidential/Restricted) and `NetworkRequirement` (Any/CompanyNetwork/BranchNetwork/Internal).
  - Permissions are not duplicated: they stay the endpoint's authorization data.
  - **Deviation:** `RequiresApproval` is not built. There is no approval workflow to enforce it, and a declared but unenforced flag is worse than none.
- [x] **Fallback policy** = authenticated user. `AddModulusAuthorization(o => o.RequireAuthenticatedUserByDefault)` (default `true`) sets it unless the app set its own.
  - It also covers requests that match no endpoint, so unknown routes answer `401` to anonymous callers, and so do static files served after `UseAuthorization`. The CLI's UI wiring therefore puts `UseStaticFiles()` before `UseAuthentication()`.
- [x] **`.Loosen(reason, ticket)`** (`Modulus.AspNetCore.Security.Policy.EndpointSecurityExtensions`, plus a `Loosen(LoosenedAttribute)` overload) and REPR `AllowAnonymous(reason, ticket)` / `Classification(...)`. `.WithSecurityPolicy(classification, network)` covers minimal APIs.
  - **Deviation:** the bare REPR `AllowAnonymous()` is not `[Obsolete]`: under `TreatWarningsAsErrors` that would break every app at upgrade. MOD0001 flags it at build time, and the guard refuses it at boot.
- [x] **Every framework anonymous endpoint carries a reason** (`Framework = true`): health live/ready, `/health/modules`, `/health/graph`, gRPC health/reflection, GraphiQL, BFF login/logout, `/_error/{code}`, the Identity UI's Login/Register/LoggedOut/AccessDenied pages, the `/Account` folder opened by `AddModulusPageAuthorization` (other folders count as app loosenings), and the OpenIddict token/authorize/introspect/end-session endpoints plus the account controller's anonymous actions. `/connect/userinfo` gained `[Authorize]`.
  - **Not loosened on purpose:** GraphQL and Realtime with `RequireAuthenticatedUser: false`. Endpoint-level anonymity overrides the policy of an enclosing route group (a BFF client's), so an app that wants them anonymous calls `.Loosen(...)` on the returned builder.
  - **Changed in phase 5:** `/_ui/menu` was on this list, which made the guard refuse to start the Web host of a `webapp+api` app (no fallback policy there, so the menu was `unpoliced`); the phase 5 end-to-end run found it. `MapModulusUiMenu` maps it in its own group, never inside a BFF client's, and the tree is filtered per caller, so it is now a framework loosening ("anonymous callers get an empty tree"). On a host with a fallback policy an anonymous caller now gets an empty menu instead of `401`.
- [x] **Startup guard** `AddModulusSecurityGuard(configuration)` (settings `Security:Guard`: `Enabled`, `AllowListPath` = `security/loosening-allowlist.json`, `FailOnUnlistedInDevelopment`).
  - It runs in `IHostedLifecycleService.StartedAsync`: the web host builds its pipeline (which hands the mapped endpoints to routing) after the other hosted services' `StartAsync`. Throwing there still fails `Host.StartAsync`.
  - The pure `EndpointSecurityAnalyzer` resolves each endpoint to `Policed` / `Fallback` / `Loosened` / `Unpoliced`.
  - Errors in every environment: `unpoliced`, `anonymous-without-reason`, `anonymous-classified-data` (Confidential/Restricted), `network-not-enforced`.
  - `loosening-not-allow-listed` fails outside Development and warns in Development. Framework loosenings are exempt from the allow-list (still reported); the allow-list governs the app's own decisions.
  - An allow-list entry's reason also explains a third-party bare `AllowAnonymous` (for example a YARP route with `RequireAuthentication: false`).
  - Static-asset endpoints are skipped.
  - The loosening report is logged and kept in `SecurityGuardState.Report` (for Phase 4's audit).
- [ ] GraphQL fields, realtime topics, jobs and consumers in the report. **Not built:** the guard sees HTTP endpoints only. GraphQL and realtime endpoints are covered as endpoints, not per field or per topic.
- [x] **Analyzer `MOD0001`** (`src/analyzers/Modulus.Analyzers`, shipped as `analyzers/dotnet/cs` inside `Cobytelabs.Modulus.AspNetCore`, Warning). It flags anonymous without a reason: a `.AllowAnonymous()` chain without `LoosenedAttribute` metadata, `[AllowAnonymous]` without `[Loosened]`, and the parameterless REPR `AllowAnonymous()`. It is active only where `LoosenedAttribute` resolves.
  - **Deviation:** the planned rule ("raw `Map*` outside `IEndpoint`") would flag every `Program.cs`. With the fallback policy a raw endpoint is closed rather than open, so the analyzer targets the guard's hard error instead.
- [x] **CLI.** Hosts with a sign-in (not `--auth none`, not a web app on an external provider, whose pages have no cookie sign-in yet) call `AddModulusSecurityGuard` and ship `security/loosening-allowlist.json`.
  - API host: lists `/openapi/{documentName}.json`, loosened in Development. Without the example permission it calls `AddModulusAuthorization()` for the fallback policy.
  - Web host of `webapp+api`: lists `/health/live`.
  - **Samples untouched:** an earlier draft gave the TradeFlow sample's anonymous REPR endpoints reasons; that change was reverted (framework-only scope). The sample still uses the bare `AllowAnonymous()`, which MOD0001 flags and the guard refuses if the sample ever calls `AddModulusSecurityGuard`.

**Verified** (packed 1.4.0 to a scratch feed, apps restored with an isolated package cache):
- `modulus app --auth openiddict` for `api` (6/6 tests), `web` + `generate-crud` (3/3) and `webapp+api` + `generate-crud` (9/9). All build with 0 warnings.
- API host log: "20 endpoints checked, 12 anonymous, 0 findings". Anonymous `GET /api/v1/products` and an unknown route both answer `401`.
- Adding `app.MapGet("/raw", ...).AllowAnonymous()` fails boot with `[anonymous-without-reason]`, and MOD0001 reports it at build. `.Loosen("...")` without an allow-list entry fails boot (Testing environment) with `[loosening-not-allow-listed]`.
- Web app in Development: the login page and its theme CSS/JS answer `200` to an anonymous visitor.

**Found on the way, not fixed:** the generic `AccountController<TUser>` uses `[Route("[controller]")]`, so its endpoints are mapped under the generic type name (`AccountController`1/forgot-password`), not `/Account/...`.

## Phase 3: Data isolation for every supported database

Requirement: company isolation must hold on **every** provider Modulus ships (PostgreSQL, SQL Server, MySQL,
SQLite, MongoDB), not only PostgreSQL. Each provider gets the strongest database-enforced control it supports.
Where the engine has no row-level security, the gap is closed by a database boundary and a framework guard.
The EF global filter and the write guard from 1.5 stay in front on every relational provider.

| Provider | Shared database (Tier S) | Database per tenant (Tier M/L) |
|---|---|---|
| PostgreSQL | Native RLS: `set_config` per connection, plus `ENABLE` + `FORCE` RLS policy `modulus_tenant` | `AddModuleDatabasePerTenant` / `TenantInfo.ConnectionString` |
| SQL Server | Native RLS: `sp_set_session_context` per connection, plus inline predicate function and `SECURITY POLICY` (filter + block predicates) | `AddModuleDatabasePerTenant` / `TenantInfo.ConnectionString` |
| MySQL | No RLS. The raw-SQL guard rejects unfiltered and raw SQL on tenant tables. Tier S is supported only with that guard; database per tenant is recommended | `AddMySQLPerTenantDatabase` |
| SQLite | No RLS, single file. **One file per tenant** is the boundary; a shared file is development-only (the guard still applies) | `AddSQLitePerTenantDatabase` (`tenants/{id:N}.db`) |
| MongoDB | `TenantScopedCollection<T>`: every read, update, delete, aggregate and bulk write gets the tenant predicate, inserts are stamped and validated | Not built (see below) |

### Shared
- [x] **Session-context seam.** **As built:** an abstract `TenantSessionInterceptor` (`Modulus.EntityFrameworkCore.Isolation`; providers implement `Supports(DbConnection)` and `Configure(DbCommand, TenantSession)`) declared per context with `AddTenantSessionContext<TContext>(interceptor)`, instead of an `ITenantSessionContext` service.
  - It writes the session on `ConnectionOpened` and remembers what each connection carries (`ConditionalWeakTable`). Before every reader, scalar and non-query command it rewrites the session when the ambient tenant changed since, so a connection held open across a tenant switch follows the switch, and a pooled connection never keeps the previous tenant.
  - It forgets the connection on close and after a rollback (a rolled-back transaction also rolls back the `set_config` on PostgreSQL).
  - It acts only for `ModuleDbContext` contexts and only for contexts that declared it.
- [x] **Raw-SQL guard** (`TenantSqlGuardInterceptor`, added by `AddModuleDatabase` to every module context, every relational provider).
  - Rejected with `CrossTenantSqlException` (`Tables`, `Origin`): raw SQL (`FromSql`, `SqlQuery`, `ExecuteSql`) and `IgnoreQueryFilters()` queries that reference a table of an `IHasTenantId` entity, inside a tenant or with no tenant. The host passes.
  - `IgnoreQueryFilters()` and composed raw-SQL roots are tagged when EF compiles the query (`modulus:unfiltered`, `modulus:raw-sql`), so the decision is taken per execution against the tenant in scope, even though the compiled query is cached.
  - **Deviation:** the opt-in is an ambient scope, `using (CrossTenantSql.Allow(reason)) { ... }`, not a `.AllowCrossTenantSql(reason)` call-site extension (it has to cover `ExecuteSql` and `SqlQuery` too). Both the opt-in and every rejection are security-audited (phase 4).
  - Limit: table references are found by name in the command text, so a statement reaching a tenant table through a view or function is not detected. RLS covers those on PostgreSQL and SQL Server.
  - The testing harness (`Modulus.Testing` SQLite swap) re-adds the guard, so app tests run with it.
- [ ] **Dapper / read models.** **Not built:** there is no `GetTenantConnection()`. Rule meanwhile: open the context's connection through EF (`db.Database.OpenConnectionAsync()`, then `GetDbConnection()`); the session is written on open, so RLS applies. The guard and the per-command refresh do not see commands that EF does not execute. On MySQL / SQLite, keep Dapper to database-per-tenant.
- [x] **Tier check at startup.** `AddModulusDataIsolationCheck(configuration)` reads `Security:DataIsolation:Tier`:
  - unset: nothing is checked;
  - `DatabasePerTenant`: every tenant-aware context must be registered per tenant, otherwise startup fails;
  - `Shared`: a shared SQLite file fails outside Development, and a context without row-level security (filter only) logs the residual risk as a warning.
- [x] **Database per tenant (any relational provider).** `AddModuleDatabasePerTenant<TContext>(hostConnectionString, tenantConnectionString, configure)`: the host scope uses the host database and a tenant uses its own. With no tenant in scope, resolving the context throws ("no tenant is in scope") instead of falling back to a shared database.

### PostgreSQL
- [x] **Deviation:** `services.AddPostgreSqlRowLevelSecurity<TContext>()` next to `AddPostgreSQLDatabase`, not a `UseTenantRowLevelSecurity()` option on it. It runs `SELECT set_config('modulus.tenant_id', @tenant, false), set_config('modulus.is_host', @host, false)`.
- [x] Migration helpers `migrationBuilder.EnableTenantRowLevelSecurity(table, tenantColumn = "tenant_id", schema)` / `DisableTenantRowLevelSecurity`: `ENABLE` + `FORCE` RLS and the `modulus_tenant` policy (`USING` and `WITH CHECK`: the tenant column equals the session tenant, or the session is the host).
  - Also `PostgreSqlRowLevelSecurity.Script(model)` (every tenant table of a model) and `EnsureAsync(context)` for apps on `EnsureCreated` or for the migrator job.

### SQL Server
- [x] **Deviation:** `services.AddSqlServerRowLevelSecurity<TContext>()` (an `EXEC sp_set_session_context` of `modulus.tenant_id` as `uniqueidentifier` and `modulus.is_host`), not an option on `AddSqlServerDatabase`.
- [x] Migration helpers `EnableTenantRowLevelSecurity(table, tenantColumn = "TenantId", schema = "dbo")` / `DisableTenantRowLevelSecurity`. They create schema `modulus` and the inline TVF `[modulus].[fn_tenant_predicate]` when missing, then one policy per table, `[modulus].[tenant_{schema}_{table}]`, with a `FILTER` predicate and `BLOCK` predicates (AFTER INSERT, AFTER UPDATE). `SqlServerRowLevelSecurity.Statements(model)` and `EnsureAsync(context)` too.

### MySQL and SQLite
- [x] Raw-SQL guard on (shared, above).
- [x] `AddMySQLPerTenantDatabase<TContext>(hostConnectionString, tenantConnectionString, configure?)`.
- [x] `AddSQLitePerTenantDatabase<TContext>(directory = "./tenants")`: `{directory}/{tenant:N}.db`, host `{directory}/host.db`.

### MongoDB
- [x] **`TenantScopedCollection<T>`** (`Modulus.Data.MongoDB`) wraps `IMongoCollection<T>`:
  - reads and deletes (`Find`, `CountDocuments`, `DeleteOne/Many`, `FindOneAndDelete`) are scoped;
  - `InsertOne/Many` and `ReplaceOne` stamp the tenant and reject a foreign one (`CrossTenantWriteException`); with no tenant resolved an insert is refused;
  - updates (`UpdateOne/Many`, `FindOneAndUpdate`) are scoped and may not touch the tenant field (`$set` / `$unset` / `$rename`, or `$replaceRoot` / `$replaceWith` in a pipeline), except in the host scope;
  - `Aggregate` prepends a `$match`, and `BulkWrite` rewrites every model; an upsert inherits the tenant from the scoped filter;
  - with no tenant nothing matches; the host sees everything.
- [x] `ModuleMongoContext` gains a constructor taking `ICurrentTenant` and `GetTenantCollection<T>(name)`; `MongoRepository` stamps on add and update.
  - **Deviation:** `GetCollection<T>` (the raw collection) is kept, with a documentation warning; removing it would break every existing Mongo module. `TenantScopedCollection.Unscoped(reason)` is the explicit opt-out, and it is **not** audited (the wrapper has no service provider).
- [ ] Database-per-tenant resolver for Mongo. **Not built.**

### Roles and docs
- [x] [`docs/security/database-roles.md`](security/database-roles.md): a migration role that owns the schema and an application role subject to RLS, with scripts for PostgreSQL, SQL Server and MySQL, plus SQLite notes.
- [x] AGENTS.md: provider × tier × enforcement table.

### Tests
- [x] **Unit:** `TenantDataIsolationTests` (`Modulus.EntityFrameworkCore.Tests`):
  - the guard rejects raw queries, raw commands and `IgnoreQueryFilters()` per execution, and the opt-in lets them through;
  - the session interceptor is applied on open, refreshed on a tenant switch, forgotten after a rollback, and only on declared contexts;
  - per-tenant databases and SQLite file names;
  - the tier check matrix;
  - the PostgreSQL and SQL Server scripts.

  `TenantScopedCollectionTests` (new `Modulus.Data.MongoDB.Tests`, 10).
- [x] **Integration (Testcontainers), written and compiling, not run on the build machine (no Docker there):**
  - PostgreSQL: the app connects as a non-owner `modulus_app` role. `IgnoreQueryFilters()` and `FromSql` in tenant B still see only B's rows. A raw cross-tenant `UPDATE` fails with `42501`. With no tenant nothing is visible, and the host sees everything.
  - SQL Server: the same, plus the block predicate and a tenant switch on one open connection.
  - MySQL: database per tenant.
  - MongoDB: wrapped find, update, delete, aggregate and bulk calls plus an upsert.

  **Run them** with `dotnet test --filter Category=Integration` on a machine with Docker before relying on them.

## Phase 3b: Isolation of the other data layers (guideline A6)

An audit of every non-database store found the following:

| Layer | Before this work | Status now |
|---|---|---|
| Cache (memory, Redis, FusionCache, `[CacheFor]`) | `CacheKeys.Entry/Tag` → `modulus:entry:{tenant:N}:…` in every service and the mediator behaviours | OK (no change) |
| **BFF composer cache** | `bff:section:{client}:{key}:{sub}`; one user in two companies got company A's section in company B | **Fixed:** the selected company is part of the key |
| **File storage** | Caller-supplied paths | **Fixed in 1.6** (`TenantScopedFileStorage`); the Files UI is deferred (UI) |
| **Notifications** | `tenantId == null` meant "all tenants" in `ListAsync` / `MarkAllAsReadAsync` | **Fixed:** exact tenant match, null = tenant-less notifications only |
| Settings | Keyed by (name, tenant, user) | OK |
| SignalR hub groups | `tenant:{id}:` prefix | OK: already refused without a tenant (host included); now tested |
| Realtime, Webhooks | Per tenant | OK; restore paths go through 1.4 |
| Idempotency keys | Scoped by `ICurrentTenant` | OK |
| Outbox / inbox | Inbox never set `TenantId` | Fixed in 1.4 |
| Localization | Global texts | OK (by design) |
| **Logs** | No tenant in the log scope, no redaction | **Scope fixed**; redaction deferred |
| Exports | None in the framework | Design note below |
| Search / vector | Dropped from the framework | Rule for the AI stage: namespace per tenant + permission filter at retrieval |

- [x] **BFF.** The composer's section cache key is `bff:section:{client}:{company or -}:{key}[:{sub}]` (`BffOptions.TenantHeader`, default `X-Tenant-Id`; the company is the `tid` claim, else that header). `BffTenantHeaderHandler`, added by `AddBffUserAccessToken()`, forwards the selected company to upstream calls. The upstream still checks membership: the BFF never picks a company itself. YARP routes forward the header as part of the request. Test: `Cached_sections_are_kept_apart_per_selected_company`.
- [ ] Storage: Files UI tenant-rooted paths. **Deferred (UI).** The storage layer itself is isolated by 1.6.
- [x] **Notifications.** `InMemoryNotificationStore.ListAsync` / `MarkAllAsReadAsync` match the tenant exactly; `INotificationStore` documents the rule for other implementations (`NotificationTenantIsolationTests`).
- [x] **SignalR.** `ModulusHub.JoinTenantGroupAsync` / `LeaveTenantGroupAsync` already threw without a tenant (the host scope too, which has no company). `ModulusHubTenantGroupTests` pins it.
- [x] **Log scope.** `CorrelationIdMiddleware` opens a `CorrelationId` scope, and `TenantMiddleware` opens `TenantId` + `UserId` (ids only, never names or e-mail addresses). Providers that include scopes (console with `IncludeScopes`, OpenTelemetry logs) carry them on every line.
- [ ] **Redaction. Deferred.** `Microsoft.Extensions.Compliance.Redaction` (MIT) redacts only data annotated with a data-classification taxonomy and logged through source-generated `[LoggerMessage]` / `[LogProperties]`, so it would not catch `[ProtectedPersonalData]` values written through ordinary log calls. It needs a classification design first (reuse `DataClassification` from phase 2). Until then, framework code logs ids, not personal data, and the security audit never records user names, passwords, tokens or SQL text.
- [x] **Exports (design note).** When an export feature lands it is an exfiltration channel, so:
  - each export is its own endpoint with a permission (not the list endpoint's), and it is classified (`EndpointSecurityPolicyAttribute`);
  - it reads through the tenant-filtered context only; no `CrossTenantSql.Allow`, and group-level exports go through the federated read path;
  - every export is recorded in the security audit (`data` category: who, which entity set, row count, filter summary, never the rows);
  - files are written through `TenantScopedFileStorage` with a short-lived download link.

## Phase 4: Security audit, hash-chained

- [x] **`ISecurityAuditLog`** (Core, `Modulus.Core.Abstractions`): `Record(SecurityAuditEvent)` never blocks and never throws, so synchronous sources (EF command interceptors) and `403` paths can call it. `SecurityAuditEvent` carries:
  - `Category` (`SecurityAuditCategories`: tenancy, authorization, identity, data, configuration)
  - `Action`
  - `Outcome` (`success` / `denied` / `overridden`)
  - `TenantId` (null = host chain)
  - `Actor`, `Target`, `CorrelationId`, `OccurredAt` and `Details`

  `NullSecurityAuditLog` is the default (`AddModulus` registers it).
- [x] **Chain** (`Modulus.Platform`, `Modulus.AuditLogging.Security`):
  - **One chain per company**, plus the host chain (`Guid.Empty`).
  - `SecurityAuditChain.ComputeHash` is SHA-256 over canonical JSON: keys in ordinal order (details too), no whitespace, UTC timestamps truncated to milliseconds so every database round-trips them, and the previous hash included. The first entry links to 64 zeros.
  - `Verify(records, anchor?)` checks contiguous sequences from 1, every link and every hash. It reports the first broken sequence and why: missing/reordered, unlinked, or edited.
- [x] **`AddModulusSecurityAudit(configuration)`** (settings `Security:Audit`):
  - It replaces the no-op log with `SecurityAuditLog`, a bounded in-memory queue (`QueueCapacity`, default 10 000) drained in order by the `SecurityAuditWriter` hosted service. A failed append is retried (`MaxAppendAttempts`), then reported lost at Critical. The queue is drained on shutdown.
  - The store is `ISecurityAuditStore` (in-memory by default).
  - **Deviation:** events are queued, not written in the request, so events recorded just before a crash can be lost, and a full queue drops the new event with an error log rather than blocking the request.
- [x] **Anchors.** `AuditAnchorService` writes every chain head to each `IAuditAnchorSink` every `AnchorInterval` (default 1 h), only when a head moved.
  - `FileAuditAnchorSink` appends JSON lines, configured through `Security:Audit:AnchorFile`; with no file set, nothing is written. Object-storage sinks are the app's own `IAuditAnchorSink` (no `IFileStorage` sink is shipped).
  - `VerifyChainAsync(tenantId, anchor)` also catches a chain recomputed from scratch after an edit, and a truncated chain.
- [x] **`Modulus.AuditLogging.EntityFrameworkCore`** (new package): `ModulusAuditDbContext` (derive the app's own context; it has no tenant filter, on purpose) and `AddModulusAuditStore<TContext>()`.
  - Business log: `EfAuditLogStore`, table `modulus_audit_logs`.
  - Security chain: `EfSecurityAuditStore`, table `modulus_security_audit` with **primary key (ChainId, Sequence)**. An append reads the head, links after it and inserts; a writer that loses the race to another replica gets a key violation, re-reads and retries. One process serializes its own appends.
  - SQLite stores `DateTimeOffset` as binary so queries can sort it.
  - `SecurityAuditDatabaseScripts.PostgreSql / SqlServer / MySql(role, ...)` make the table append-only: the app role gets `SELECT, INSERT` only (`REVOKE` / `DENY UPDATE, DELETE`), and a trigger refuses `UPDATE` / `DELETE` (and `TRUNCATE` on PostgreSQL) for every role. Identifiers are validated, never interpolated raw.
- [x] **Sources:**
  - **Tenancy:** `TenantMiddleware` records `tenant.claim-unresolved`, `tenant.claim-mismatch` and `tenant.not-a-member` (denied) and `tenant.break-glass` (overridden), in the chain of the company that was reached. `TenantManager` records `membership.added/removed` and `tenant.activated/deactivated`.
  - **Authorization:** `AuthorizationSecurityAuditHandler` turns the relayed `AuthorizationAdministrativeChangeEvent` (grants, roles, org units, entitlements, delegations) and the audited `AccessDecisionAuditEvent`s into chain entries.
    - **Deviation:** they arrive through the existing outbox relay, so they need `AddEfCoreAuthorizationAudit`; with the no-op authorization writer nothing is relayed.
  - **Identity:** `IdentityPasswordGrantValidator` records why a password sign-in was refused: unknown user (host chain), disabled, not allowed, locked out, or wrong password. `ModulusTokenController` records issued and refused `token.password`, `token.refresh`, `token.authorization-code` and `token.client-credentials`. No user names, passwords or tokens are recorded.
    - **Not built:** `/connect/revoke` is handled entirely by OpenIddict, so revocations are not recorded.
  - **Data:** `TenantSqlGuardInterceptor` records each rejection (denied) and each `CrossTenantSql.Allow` use (overridden, with the reason). It never records the SQL text.
  - **Configuration:** the startup guard records `startup.loosening-report` at every start in the host chain: endpoint, anonymous and finding counts, the anonymous routes, and a SHA-256 fingerprint of them, so a new loosening shows up in the audit.
- [ ] **UI:** Security tab in `Modulus.UI.AuditLogging` with chain status. **Deferred (UI).**
- [x] **Tests:**
  - `SecurityAuditChainTests` (Platform): per-company contiguous chains; an edited, removed or rewritten entry is detected (the anchor catches the rewrite and truncation); canonical hashing; 200 concurrent appends; the writer delivers in order; anchors are written only when a head moves.
  - `EfSecurityAuditStoreTests` (new `Modulus.AuditLogging.EntityFrameworkCore.Tests`, SQLite file):
    - two "nodes" appending 40 events at once keep both chains contiguous and valid;
    - a row edited with `ExecuteUpdate` breaks verification at that row;
    - a deleted last row is caught by the anchor;
    - the business log round-trip;
    - the script identifier check.
  - Source tests: middleware rejection and break-glass (`TenantMiddlewareTests`), and the guard's denied and overridden records (`TenantDataIsolationTests`).

## Phase 5: A9 isolation test generator

- [x] **`SecurityProbeSuite.RunAsync(factory, options?)`** (`Modulus.Testing.Security`), also `RunAsync(services, client, options?)` for a hand-built TestServer host. It enumerates every route endpoint and method (`Skip` leaves endpoints out) and probes:
  - **anonymous:** `401`, or a redirect whose location contains `LoginPathFragment` (default `login`);
  - **no permission:** `403` for a signed-in caller without permissions or roles, on endpoints with a policy, permission or role (a bare sign-in requirement gets no such probe);
  - **foreign tenant** (when `ForeignTenantId` is set): `403` for a caller holding the endpoint's permissions and roles who selects a company it is not a member of.

  Loosened endpoints are listed, not probed (the startup guard and the allow-list review them). Route parameters get placeholders by constraint (`RouteValue` overrides that), and unsafe methods send `{}`. Responses are read to the headers only, so event streams do not hang. `report.EnsureNoFailures()` throws `SecurityProbeException` with one line per failure.
  - **As built:** `AnonymousStatusOverrides` lists routes refused with another code by design. Its default holds OpenIddict's `/connect/userinfo`, which answers `400 invalid_request` to a request with no token before ASP.NET authorization runs (found by the end-to-end run). Accepting any 4xx would let an unpoliced endpoint that validates its body first pass with `400`.
- [x] **Helpers** (`TenantIsolationAssertions`):
  - `services.AssertTenantIsolationAsync<TContext, TEntity>(entity, owner, other)` saves the entity as one company. As the other company it then checks that the entity is not found by key, not listed, and not returned by `IgnoreQueryFilters()` (the guard must refuse that query).
  - `AssertCrossTenantReadIsDeniedAsync(clientA, clientB, create)`: an HTTP resource created by A answers `404` / `403` to B.
  - **Not built:** cache key, storage path and "consumer rejects an unknown tenant" helpers. Those properties are covered by the framework's own unit tests (`CacheKeys`, `TenantScopedFileStorage`, the 1.4 restorer tests), so an app has nothing app-specific to assert there.
- [x] **CLI:** the app template emits `SecurityProbeTests` (in the generated `ModulePipelineSmokeTest.cs`) for every API host with the security guard (`expose_api && use_security_guard`): `SecurityProbeSuite.RunAsync(factory)` + `EnsureNoFailures()`. Covered by `Guarded_API_hosts_ship_the_security_probe_test`.
- [ ] `generate-crud` per-entity isolation test. **Deferred:** generated entities are not `IHasTenantId` and generated apps have no multi-tenancy (see 1.8), so there is nothing to isolate yet. It lands with `modulus app --multi-tenancy`.
- [x] **Tests:** `SecurityProbeSuiteTests` (`Modulus.Testing.Tests`) cover three cases:
  - a policed host passes every probe;
  - a `DELETE` without a policy fails the suite with `expected 401, got 204`;
  - the isolation helper passes for a tenant entity and reports an entity whose filter was removed.

## Verification

1. **Done (2026-10-03):**
   - `dotnet build modulus.slnx`: 0 warnings, 0 errors.
   - `dotnet test modulus.slnx --filter "Category=Unit"`: every unit test project passes.
   - `dotnet format modulus.slnx --verify-no-changes`: clean.
2. **Not run:** the phase 3 integration tests for every provider (PostgreSQL, SQL Server, MySQL, MongoDB via Testcontainers), because there is no Docker on the build machine.
3. **Done** with the framework packed as 1.4.0 to a scratch feed and the CLI installed from it (isolated package cache):
   - `modulus app Demo --kind api --auth openiddict`: 7/7, including `SecurityProbeTests`.
   - `--kind api --auth keycloak`: 6/6.
   - `--kind webapp+api --auth openiddict`: 10/10.
   - `--kind web --auth openiddict`: 3/3.

   The probe run found the `/connect/userinfo` behaviour (phase 5) and the `/_ui/menu` guard failure on the split Web host (phase 2 note).

   **Still to do by hand** once an app has multi-tenancy:
   - a member of A only gets `403` on `X-Tenant-Id: B` (the middleware test covers it in the framework);
   - a hand-edited audit row fails `VerifyChainAsync` against a real database (the SQLite test covers it).

## Remaining work (resume here)

State on 2026-10-03: phases 1–5 are built in the framework and verified (build 0/0, all unit tests green, format
clean, four generated apps pass their tests). Nothing is committed yet. `samples/` was deliberately not changed.
Open items, roughly in priority order:

1. **Run the integration tests on a machine with Docker:** `dotnet test modulus.slnx --filter "Category=Integration"`.
   - `TenantIsolationIntegrationTests`: PostgreSQL RLS, SQL Server RLS, MySQL per tenant.
   - `TenantScopedCollectionIntegrationTests`: MongoDB.

   They compile but have never run. Fix any provider surprises, for example SQL Server RLS DDL permissions or the
   PostgreSQL `set_config` type casts.
2. **`modulus app --multi-tenancy` option** (needs its own design: tenant store module, resolver choice, seeding). It
   unblocks:
   - 1.8: `RequireMembership()`, `AddModulusSecurityContext()`, `IsolateTenants` and the seeded admin's membership;
   - the phase 5 `generate-crud` per-entity isolation test (`AssertTenantIsolationAsync`);
   - `SecurityProbeOptions.ForeignTenantId` in the generated `SecurityProbeTests`.
3. **Security audit wiring in generated apps:**
   - `AddModulusSecurityAudit(builder.Configuration)`;
   - optionally `AddModulusAuditStore<TContext>()` with an audit store module;
   - a `Security:Audit:AnchorFile` setting.
4. **UI (deferred by request):**
   - Files UI paths relative to the tenant root;
   - Security tab in `Modulus.UI.AuditLogging` (chain list, `VerifyChainAsync` status per company, anchor comparison).
5. **Log redaction:** design a classification taxonomy (reuse `DataClassification`) before adopting
   `Microsoft.Extensions.Compliance.Redaction`.
6. **Smaller gaps:**
   - audit token revocation (an OpenIddict event handler on `/connect/revoke`);
   - audit `TenantScopedCollection.Unscoped(reason)`;
   - Mongo database-per-tenant resolver;
   - `GetTenantConnection()` for Dapper / read models;
   - GraphQL fields, realtime topics and jobs in the guard report;
   - a durable option for the audit queue (events recorded just before a crash can be lost).
7. **Found on the way, not fixed:** the generic `AccountController<TUser>` route maps under `AccountController`1/...`
   (phase 2 note).

How to re-verify end to end (what was done on 2026-10-03):

```
dotnet pack modulus.slnx -c Release -o <feed>
dotnet tool install Cobytelabs.Modulus.Cli --tool-path <tools> --add-source <feed> --version 1.4.0
<tools>/modulus app Demo --kind api --auth openiddict --package-source <feed>
cd Demo && NUGET_PACKAGES=<fresh-cache> dotnet test
```

Use a fresh `NUGET_PACKAGES` folder (or delete its `cobytelabs.*` entries): the version number stays at 1.4.0, so a
cache from an earlier pack silently serves stale framework packages.
