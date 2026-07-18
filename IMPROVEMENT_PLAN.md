# Modulus Improvement Plan

Derived from the 2026-07-12 architectural review. P0 items block any 1.0 / public
NuGet release; they are ordered by execution order (cheap trust fixes first,
restructures last). Each item records the design decision so future sessions
don't re-litigate it.

## Status

**All six P0 items are complete** (2026-07-12), building with 0 warnings / 0 errors
and 126 passing unit tests (regression tests added for each fix). Details below.

## P0 — Critical (this pass) — ✅ DONE

### P0-3. Module bootstrap: eager graph build + single instantiation
**Problem.** (a) `ModuleLifecycleHostedService` initializes from `ModuleLoader._sorted`,
which is only populated when the app remembers to call `UseModulus()` — forgetting it
boots cleanly and silently skips every module's `InitializeAsync`. (b)
`ModulusBuilder.GetDeclaredDependencies` `Activator.CreateInstance`s every module just
to read the `DependsOn` property, then `AddModule` instantiates it again.

**Decision.** `AddModulus` builds the graph eagerly and registers a pre-built
`ModuleLoader` singleton instance; module instances are created once during discovery
and reused for registration. `UseModulus()` stays as a source-compatible no-op
(validation only). The `IModule.DependsOn` property remains supported but discovery
reads each instance once — `[DependsOn]` attributes are the recommended source.

### P0-6. Production-safe defaults
- Identity: `AddDevelopmentEncryptionCertificate`/`AddDevelopmentSigningCertificate`
  applied only when environment is Development or `Identity:UseDevelopmentCertificates`
  is explicitly true. ROPC password flow becomes opt-in
  (`Identity:AllowPasswordFlow`, default **false**) — removed in OAuth 2.1.
- `MigrateModulusDatabasesAsync` default mode changes `MigrateOrCreate` → `Migrate`
  (fail loudly in production); the app template passes `MigrateOrCreate` explicitly
  in Development only.
- Rename public API typo `DecororateIntegrationEventHandlers` →
  `DecorateIntegrationEventHandlers`.
- Verify `PermissionRegistry.Freeze()` is actually invoked after module registration.

### P0-1. Fail-closed multi-tenancy
**Problem.** Query filter `TenantId == null || e.TenantId == TenantId` means an
*unresolved* tenant (missing header, misconfigured resolver, background job that
forgot `Change(...)`) reads **every** tenant's rows.

**Decision.** Introduce an explicit host context: `ICurrentTenant.IsHost`.
- `NullCurrentTenant` (multi-tenancy not configured) ⇒ `IsHost = true` ⇒ match-all —
  single-tenant apps are unaffected.
- `CurrentTenant` (multi-tenancy configured): unresolved ⇒ `IsHost = false`,
  `TenantId = null` ⇒ filters match **nothing** (fail-closed).
- `Change(null)` (or resolver returning host) ⇒ explicit host scope ⇒ match-all.
- Filters become `e => currentTenant.IsHost || e.TenantId == currentTenant.TenantId`
  (a null parameter compares false against every row — fail-closed by construction).
- `OutboxProcessor` wraps host-written rows (`TenantId == Guid.Empty`) in an explicit
  `Change(null)` scope during dispatch.
- Mongo tenant filter gets the same semantics.

### P0-4. Transaction scoping
**Problem.** `TransactionBehavior` opens a transaction on **every** registered
`DbContext` for **every** command — O(modules) connections/transactions per request,
and the multi-context commit loop is pseudo-atomic anyway.

**Decision.** New `[Transactional(typeof(CatalogDbContext), ...)]` attribute declares
the contexts a command touches. Defaults: exactly one registered context ⇒ wrap it
(previous behaviour for the common case); multiple contexts + no attribute ⇒ **no
wrapping** (each `SaveChangesAsync` is still atomic); `MediatorOptions.TransactionMode
= TransactionMode.AllContexts` restores the old fan-out for apps that want it.
Attribute lookups in Transaction/Caching/Authorization behaviors are cached in static
fields of the closed generic (they currently reflect per call).

### P0-2. Stable event wire format
**Problem.** Outbox rows and broker envelopes identify events by
`AssemblyQualifiedName` — an assembly version bump or type move breaks every pending
message; `Type.GetType` on stored strings is also a deserialization surface. The
envelope drops `TenantId`/`CorrelationId` at the broker boundary.

**Decision.** Stable event names are the wire contract:
`[IntegrationEventName("catalog.product-created.v1")]`, defaulting to the type's
**full name without assembly**. `IIntegrationEventRegistry` maps name ⇔ type and is
the only resolver on the consume path (legacy AQN fallback: `Type.GetType` restricted
to already-loaded assemblies). `IntegrationEventEnvelope` gains `TenantId` and
`CorrelationId`; outbox rows store the stable name and correlation id and both are
restored on dispatch.

### P0-5. Split Modulus.Platform
**Problem.** Wanting multi-tenancy forces AWSSDK.S3 + Azure.Storage.Blobs + Minio +
StackExchange.Redis + Azure SignalR into the dependency tree.

**Decision.** Platform keeps only shared-framework-dependency features
(MultiTenancy, Authorization, BackgroundJobs, Http resilience, memory caching,
storage abstractions + LocalFileStorage). New packages: `Modulus.Storage.AwsS3`,
`Modulus.Storage.AzureBlobs`, `Modulus.Storage.Minio`, `Modulus.Caching.Redis`,
`Modulus.SignalR`. Namespaces unchanged where possible.

## P1 — in progress

### P1-1. Module lifecycle phases — ✅ DONE
`IModule` gains `PreConfigureServices` and `PostConfigureServices` (default
interface methods, no-op; virtual on `ModulusModule`). `ModulusBuilder.Complete()`
now runs three ordered phases across all modules — Pre for all, then Configure for
all, then Post for all — each in dependency order. Registration is deferred from
`AddModule` into `Complete()` (always called by `AddModulus`), so a module can seed
shared state in Pre that a later module refines in Configure, and finalize in Post
once every registration is visible. Direct `IModule` implementers are unaffected
(DIM no-op). Regression: `ModuleLifecyclePhasesTests` (phase order + per-phase
dependency order + bare-impl default). 129 unit tests green.

### P1-2. EF-backed tenant store — ✅ DONE
New package `Modulus.MultiTenancy.EntityFrameworkCore` (references Modulus.Platform
+ EF Core.Relational — keeps EF Core out of Platform). Ships `TenantEntity`,
`TenantStoreDbContext` (unique `Slug` index, `ModulusTenants` table), `EfTenantStore
: ITenantStore` (only **active** tenants resolve — a deactivated tenant fails closed
to null), `TenantManager` (create/set-active provisioning), and
`AddEfCoreTenantStore(configure)` which supersedes `NullTenantStore` via
`RemoveAll` + scoped registration. Deliberately registered only as
`TenantStoreDbContext` (never as `DbContext`) so it stays out of the module
transaction fan-out (P0-4) and module migration loop; a dedicated
`MigrateTenantStoreAsync` initialises its schema. Regression: `EfTenantStoreTests`
(7, SQLite in-memory) — supersedes null store, find by id/slug, unknown→null,
deactivated→fail-closed, duplicate-slug throws. 136 unit tests green.

### P1-3. Registration-time entity→context map — ✅ DONE
`EfRepository<T>` previously resolved its `DbContext` by calling
`GetServices<DbContext>()` and scanning every module context's `.Model` on each
repository's first access — which forced instantiation of **every** registered
module context in the scope just to find the one owning entity `T`. New
`IEntityContextMap` (in `Modulus.EntityFrameworkCore`) records, per
`AddModuleDatabase<TContext>` call, which context owns which entities; the map is
built **once** for the app lifetime (singleton, lazy — one throwaway scope reads
each context's metadata model, no DB connection) and then serves O(1) lookups.
`EfRepository<T>` now resolves exactly the owning context and only falls back to
the runtime scan for contexts registered outside `AddModuleDatabase`. First-match
semantics preserved for framework entities shared across every context (e.g.
`OutboxMessage`). Regression: `EntityContextMapTests` (3) — routes each entity to
its owning context type, unknown→null, and **resolving a repository instantiates
only the owning context** (a construction counter proves the unrelated module
context is never built). 139 unit tests green.

### Authorization framework — design blueprint (2026-07-12)
`docs/architecture/authorization-framework-blueprint.md` is the governing
functional/architectural spec for Modulus authorization (benchmarked against SAP,
Salesforce, Dynamics 365, Workday, ServiceNow, Oracle/NetSuite/Odoo). It defines a
**PBAC-under-an-RBAC-surface** model layered as: tenant isolation → feature gate →
capability (RBAC) → org scope → data/row scope → resource/workflow policy → field
projection, evaluated by a single PDP with many enforcement points, fail-closed
throughout. §22 maps it onto what Modulus already has (permission registry, policy
provider, `AuthorizationBehavior`, `ICurrentUser`/`ICurrentTenant`, EF query
filters) and fixes the delivery sequence. The **permission grant store** is
increment 1 of that sequence and unblocks the org/data/field/workflow layers.

### P1-4. Permission grant store — ✅ DONE (auth blueprint §22, increment 1)
Grants are now editable/revocable **data** resolved **server-side**, closing the
"effective permissions arrive only as issuer-asserted JWT claims" gap. In
`Modulus.Platform` (`Authorization/Grants/`): `PermissionGrant` (allow/deny,
role/user holder) + `PrincipalGrantQuery`; `IPermissionGrantStore` with the default
`InMemoryPermissionGrantStore` (grant/deny/revoke for roles & users; empty ⇒
fail-closed deny-all; dynamic — mutable at runtime, unlike the frozen catalog);
`IPermissionResolver`/`PermissionResolver` computing a principal's **effective set**:
deny-override (explicit deny beats allow, applied *after* the closure), `Requires`
**implication** closure (approve ⇒ update ⇒ read, transitive, cycle-safe via a
lazily-snapshotted Requires index), and `module:group:*` **wildcard** expansion over
the registry (unknown prefix ⇒ nothing). `AddModulusAuthorization` registers store +
resolver via `TryAdd` (an EF-backed store can supersede later); `AddPermissionGrants`
seeds grants (accumulating seed pattern mirroring `IPermissionRegistration`). In
`Modulus.Identity`: `GrantStorePermissionChecker : IPermissionChecker` (opt-in via
`AddGrantStorePermissionChecker`) resolves the current principal from **user id + role
claims** (not fine-grained permission claims), scoped so the set is computed once per
request; `ClaimsPrincipalCurrentUser` already consults `IPermissionChecker`, so
`ICurrentUser.HasPermission` (and thus `[RequirePermission]`/`AuthorizationBehavior`)
now flows through the grant store. Fail-closed throughout (unauthenticated ⇒ empty).
Regression: `Modulus.Platform.Tests` (new project — 19: resolver allow/deny/
implication/wildcard/case/fail-closed + store combine/revoke/dynamic) and
`GrantStorePermissionCheckerTests` (5, DI end-to-end through the public checker seam).
**163 unit tests green**, 0 warnings / 0 errors.

### P1-5. Organizational scope — ✅ DONE (auth blueprint §22, increment 2)
The org layer of the pipeline (blueprint §5.4, §7, §8): given a user *can* act, it
answers *where in the organization* they may. In `Modulus.Platform`
(`Authorization/Organization/`): `OrgUnit` (stable id + `ParentIds` — a DAG, so
matrixed functional+geographic structures work) with `OrgScopeMode` (UnitOnly /
UnitAndDescendants / UnitAndAncestors — traversal is a property of the *placement*,
not a global switch) and `OrgPlacement` (user↔unit↔mode). `IOrgHierarchy` +
`InMemoryOrgHierarchy` hold the tree/DAG with **memoised transitive closures**
(`Descendants`/`Ancestors`, cycle-safe), runtime-mutable via `AddUnit`/`MoveUnit`
(reorg moves a subtree's effective access because placements reference the stable
unit id, never a path). `IOrgPlacementStore` + `InMemoryOrgPlacementStore`
(`Place`/`Remove`, empty ⇒ fail-closed). `IOrgScopeResolver`/`OrgScopeResolver`
computes an `OrgScope` = **union of every placement expanded by its mode over the
closure**; anonymous or no-placement ⇒ `OrgScope.None`; a placement at an unknown
unit still scopes to that unit (explicit data) but traverses to nothing
(fail-closed). `AddModulusAuthorization` `TryAdd`s hierarchy + placements + resolver
(EF-backed stores can supersede); `AddOrganization(hierarchy, placements)` seeds
both (accumulating). No enforcement point yet — `OrgScope` is the input the **data/row
scope** layer (increment 3) composes into a query predicate (`record.OrgUnitId ∈
scope`); that increment also wires current-user → scope at the edge (mirroring the
increment-1 `IPermissionChecker` bridge). Regression: `InMemoryOrgHierarchyTests`
(9: transitive down/up, DAG multi-parent, reorg via MoveUnit, cycle termination,
unknown→empty, self-parent rejected) + `OrgScopeResolverTests` (9: fail-closed
anon/no-placement, each traversal mode, default mode, multi-placement union, unknown
unit, runtime replacement). **181 unit tests green**, 0 warnings / 0 errors.

### P1-6. Data / row scope — ✅ DONE (auth blueprint §22, increment 3)
The enforcement point for org scope: turns the resolved `OrgScope` into an EF **query
predicate** so `record.OrgUnitId ∈ scope` is applied to every read — list and
single-item alike, because both flow through the global query filter (blueprint tenet:
row-scope-as-query-predicate, never in-memory; list & single use the *same* rule). New
`IHasOrgUnit` entity marker (`Modulus.Core.Abstractions.Entities`, mirrors
`IHasTenantId`). New `ICurrentDataScope` seam (`Modulus.Core.Abstractions`) exposing
`IsUnrestricted` + `OrgUnitIds`, with `NullCurrentDataScope` (unrestricted ⇒ filter is
a no-op when scoping isn't configured, mirroring `NullCurrentTenant.IsHost`). The edge
bridge `CurrentDataScope` (`Modulus.Platform`, scoped) resolves the current user →
`OrgScope` via `ICurrentUser` + `IOrgScopeResolver`, request-consistent (memoised);
`IsUnrestricted` gated by the `data:scope:bypass` grant (deny-by-default); opt-in via
`AddOrgDataScope()`. `ModuleDbContext.ApplyQueryFilters` refactored from a
per-combination switch to a **compositional builder** (`SetCombinedFilter<T>`): AND-
combines the layers an entity opts into — soft-delete ∧ tenant ∧ org-scope — via
`EF.Property<T>` sub-predicates + a `ParameterReplacer` that unifies only the entity
parameter, keeping each layer's context-rooted closure intact so EF re-roots it per
query (no frozen-model leak). Fail-closed: once scoping is wired, an unauthenticated or
unplaced principal sees no `IHasOrgUnit` rows; cross-org visibility requires the bypass
grant. Regression: `CurrentDataScopeTests` (4: fail-closed anon, resolved units, bypass,
request-consistent memoisation) + `OrgScopeFailClosedTests` (2: fail-closed/unrestricted
with list+single same-rule + cross-scope re-rooting; org ∧ tenant ∧ soft-delete
compose). **187 unit tests green**, 0 warnings / 0 errors.

### P1-7. Resource / workflow policy — ✅ DONE (auth blueprint §22, increment 4)
Instance-level authorization (blueprint §5.7 resource-based + §5.8 workflow-aware):
given a user *can* act and is *in scope*, may they perform *this action* on *this
specific record right now*? Two new attribute markers in `Core.Abstractions.Entities`:
`IHasOwner` (creator/assignee) and `IHasWorkflowState` (free-form lifecycle string —
the framework is workflow-*aware*, not an engine; state is a domain-owned attribute
policies read). In `Modulus.Platform` (`Authorization/Resources/`): `ResourceAttributes`
(normalized owner/org-unit/state snapshot, `From(object)` reads whichever markers a
record implements), `ResourceRequest` (pure eval input exposing composable condition
probes `OwnedByCaller`/`CallerHasPermission`/`InState`/`InCallerScope`), `AccessDecision`
(allow/deny + reason). Policies are **declarative rules-as-data**: `ResourcePolicy` +
fluent `ResourcePolicyBuilder` (`Allow`/`Deny`/`Transition(from→to)`), surfaced via
`Rules` for admin matrix review — no `switch` in handlers. `Evaluate` is
**deny-by-default with deny-override**, matching the grant resolver so the stack fails
closed consistently. Transitions are permissioned actions guarded by source state. The
enforcement point ships this increment: `IResourceAuthorizer`/`ResourceAuthorizer`
(scoped) builds the request from `ICurrentUser` (identity, not trusted claims) +
`ICurrentDataScope` (the `InCallerScope` probe **reuses increment 3**, so single-item
mirrors the bulk list filter — a record hidden in a list can't be opened by id).
`IResourcePolicyRegistry` maps type→policy (last wins); `AddResourcePolicy<T>(policy)`
registers it. Fail-closed: unregistered type or ungranted action denies; call
`authorizer.Authorize(record, "approve")` in a handler. Regression:
`ResourcePolicyTests` (10: owner-edits-draft, locked-after-submit, stranger denied,
edit-any override, approve needs perm+state, transition state-guard, deny-override on
archived, unknown action denied, anonymous never owner, rules exposed) +
`ResourceAuthorizerTests` (5: no-policy deny, owner-via-identity, non-owner deny,
anonymous deny, scope-gated read reusing `ICurrentDataScope`). **202 unit tests green**,
0 warnings / 0 errors.

### P1-8. Field-level security — ✅ DONE (auth blueprint §22, increment 5)
Field-level security (blueprint §5.9, §11): two users who both legitimately open the
*same record* may be entitled to *different fields* of it — a recruiter sees the profile
but not the salary. Fields carry a **classification** declared on the model:
`FieldClassification` enum (Public < Internal < Confidential < Restricted) + a
`[Classified(...)]` attribute on properties (`Core.Abstractions.Entities`); unannotated =
Public. In `Modulus.Platform` (`Authorization/Fields/`): `FieldClassificationMap` reflects
a type's property→classification once and caches it (process-wide) — per-field checks
resolve from cache, never per-row reflection. `FieldSecurityProfile` + fluent
`FieldSecurityProfileBuilder` map classification (and per-field overrides) to read/write
`FieldRequirement` (Open | Permission | Closed), **deny-by-default**: a sensitive field
with no configured clearance is Closed, so *classifying a field on the model protects it
even before a profile exists*; Public is open; per-field override wins over the class
rule; read and write are configured independently (opening read never opens write).
Enforcement ships both boundaries the blueprint requires via `IFieldAuthorizer`/
`FieldAuthorizer` (scoped, resolves a per-request memoised `FieldMask` from
`ICurrentUser` permissions): `Redact<T>(dto)` masks unreadable fields on the read
projection (settable props reset to default — apply to the response DTO, not a tracked
entity), and `AuthorizeWrite(type, attemptedFields)` returns an `AccessDecision`
rejecting writes above the caller's clearance (unknown field names fail closed). A type
with no registered profile still resolves against `FieldSecurityProfile.Empty`, so
classification alone is fail-closed. `FieldMask.Fields` exposes resolved per-field access
for audit/matrix. `IFieldSecurityRegistry` maps type→profile (last wins);
`AddFieldSecurity<T>(profile)` registers it. Regression: `FieldSecurityProfileTests`
(8: public open, sensitive-unconfigured closed, class rule gates, read-only leaves write
closed, read/write independent, field override wins, partial override falls back, blank
permission rejected) + `FieldAuthorizerTests` (7: mask unreadable + keep public, preserve
when cleared, write rejects protected/allows public, write allows cleared field, unknown
field fail-closed, classified-without-profile fail-closed, mask exposed for audit).
**217 unit tests green**, 0 warnings / 0 errors.

### P1-9. Feature entitlements — ✅ DONE (auth blueprint §22, increment 6)
Feature/entitlement gating (blueprint §5.11, §14): a dimension *above* per-user
permissions — is a capability *available to this tenant at all* (licensing, plan,
add-on, jurisdiction)? A feature disabled by entitlement is invisible to **everyone** in
the tenant, admins included; the gate sits *outside and before* the permission check.
Seam `IFeatureGate` (`Core.Abstractions`, `bool IsEnabled(feature)`) + `NullFeatureGate`
(everything on ⇒ no-op when feature mgmt off, mirrors `NullCurrentTenant`) +
`FeatureDisabledException`. In `Modulus.Platform` (`Authorization/Features/`): a
module-declared **catalog** (`FeatureDefinition` + `IFeatureCatalog`, parallel to
`IPermissionRegistry`, for admin discovery — not enforcement); the entitlement **store**
`IFeatureEntitlementStore`/`InMemoryFeatureEntitlementStore` (plans = named feature
bundles, tenant→plan assignment, per-tenant enable/disable overrides; runtime-mutable for
billing events); and the **resolver** `IFeatureEntitlementResolver`/
`FeatureEntitlementResolver` computing effective availability with hierarchical defaults
**plan → tenant override** (override wins both ways: add-on on / jurisdictional block
off), fail-closed — plus `EnabledFeatures(tenant)` for feature-aware menu building. The
enforcement point is the scoped `FeatureGate` bridge reading `ICurrentTenant`: host sees
every feature (deliberate all-tenant scope), a resolved tenant defers to its entitlements,
**no tenant resolved ⇒ fail-closed** — exactly mirroring the tenant query filter. Wired
as a PEP through the mediator: `[RequireFeature("…")]` + `FeatureGateBehavior` registered
**before** `AuthorizationBehavior` (availability decided ahead of capability), no-op under
`NullFeatureGate` until `AddFeatureGate` opts in. DI: `AddModulusAuthorization` TryAdds
catalog+store+resolver; `AddFeatures(...)` declares catalog entries; `AddFeatureEntitlements(seed)`
seeds plans/assignments; `AddFeatureGate()` turns on enforcement. Regression:
`FeatureEntitlementResolverTests` (8: plan grants, outside-plan fail-closed, enable add-on,
disable plan-feature, unknown tenant/feature, EnabledFeatures fold, runtime mutation) +
`FeatureGateTests` (3: resolved tenant, host all-features, no-tenant fail-closed) +
`FeatureGateBehaviorTests` (4: no-attr passthrough, disabled short-circuits before handler,
enabled proceeds, NullFeatureGate no-op). **232 unit tests green**, 0 warnings / 0 errors.

### P1-10. Delegation + governance — ✅ DONE (auth blueprint §22, increment 7 — FINAL)
The last authorization layer (blueprint §5.13/§15 delegation, §5.6/§13 SoD, §5.14/§16
audit-governance), all in `Modulus.Platform` (`Authorization/Governance/`).
**Delegation / temporary access:** `Delegation` (from-user + roles snapshot → to-user, a
permission subset, a validity window, revoked flag; `IsActiveAt(now)` fail-closed) +
runtime-mutable `IDelegationStore`/`InMemoryDelegationStore` (`Delegate`/`Revoke`/`ActiveFor`).
`IDelegationResolver`/`DelegationResolver` computes a delegate's in-force permissions with
four non-negotiables: **expiry enforced at decision time** (via `TimeProvider`, never a
cleanup job), **revocation immediate**, **capped by the delegator's own DIRECT authority**
(re-resolved through the concrete `PermissionResolver` using the roles snapshot — "cannot
delegate what you do not have"), and **sub-delegation bounded** (the cap ignores the
delegator's *own* delegated authority, so delegated-through permissions are not
re-delegable). `DelegatedPermission` carries **on-behalf-of** provenance for audit. Made
**live** by `DelegationAwarePermissionResolver`, a decorator over `IPermissionResolver`
installed by `AddDelegation` — so delegated authority takes effect at
`ICurrentUser.HasPermission` with **zero change to the Identity checker**.
**Segregation of duties:** `SodConstraint` (a set of mutually-exclusive permissions,
"maker cannot be checker") + `ISodPolicy`/`SodPolicy` evaluating a principal's effective
set — a violation is holding ≥2 of a constraint's permissions — as an *analyzable standing
control* (run over any user for attestation, or over a proposed set to prevent creating a
toxic combination). **Effective-access reporting:** `IEffectiveAccessService`/
`EffectiveAccessService` composes direct (concrete resolver) + delegated (with provenance)
+ SoD violations into an `EffectiveAccessReport` — the "what could this user access?"
auditor/breach snapshot. **Recertification:** `RecertificationCampaign` expands access
snapshots into per-(user,permission) review lines (Direct/Delegated source), tracks
certify/revoke, exposes the revoked lines as actionable output, and completes when nothing
is pending. DI: base `AddModulusAuthorization` now registers the concrete `PermissionResolver`
once + maps the interface to it (so the decorator and reporter can depend on *direct*
resolution), and TryAdds `TimeProvider.System` + empty delegation/SoD defaults + the
reporter; `AddDelegation(seed?)` wires the store + resolver + `Replace`s `IPermissionResolver`
with the decorator; `AddSegregationOfDuties(constraints)` supersedes the empty SoD default.
Regression (+23): `DelegationResolverTests` (7), `DelegationAwarePermissionResolverTests`
(3), `SodPolicyTests` (6), `EffectiveAccessServiceTests` (3), `RecertificationCampaignTests`
(4). **255 unit tests green**, 0 warnings / 0 errors.

**Authorization framework blueprint delivery sequence (§22): COMPLETE — all 7 increments
shipped** (grant store → org scope → data/row scope → resource/workflow policy →
field-level security → feature entitlements → delegation + governance).

### Remaining P1 (not started)
Docs site + samples restore; API baselines + package validation in CI; test coverage
push (regression test per fixed defect; fakes package). Authorization-adjacent follow-ups
now that the 7-layer PDP is complete: **durable audit emission** (administrative-change +
scoped decision auditing over the existing outbox transport, blueprint §5.14/§16 — the one
governance piece modelled but not yet wired to a transport); an **EF-backed** grant/org/
entitlement/delegation store set (the in-memory stores were built TryAdd-superseded for
exactly this); and wiring increment-1's `ICurrentUser.Permissions` through the grant store
(still reads permission claims).

## P2 / P3
Localization, audit module, persistent scheduler (Quartz/Hangfire), distributed
locking, cache invalidation + unified caching, convention DI registration +
Decorate(), domain-event dispatch timing options, optimistic concurrency conventions,
framework metrics, prune unused package pins, source-generated dispatch (AOT),
architecture-test package.

## Working agreements
- `TreatWarningsAsErrors` is global: every step must build with 0 warnings and pass
  `dotnet test modulus.slnx --filter "Category=Unit"`.
- Pre-1.0: breaking changes are allowed and preferred over compat shims, but wire
  formats (outbox rows, envelopes) get legacy fallbacks.
- No commits unless explicitly requested.
