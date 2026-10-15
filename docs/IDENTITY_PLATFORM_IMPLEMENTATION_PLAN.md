# Modulus Identity Platform Implementation Plan

**Version:** 1.3.1 — source-verified architectural baseline
**Status:** Approved for implementation; canonical living plan
**Date:** 2026-10-15
**Scope:** Reusable Modulus framework and optional platform modules; first production consumer is the Garments ERP
**Supersedes:** the v1.2 draft plan and the pre-verification v1.3 text (both preserved in Git history)
**Companion:** [`docs/AUTHORIZATION_CONFORMANCE.md`](AUTHORIZATION_CONFORMANCE.md) — BRS §16.3 truth-table conformance and traceability specification

This plan converts the Identity, Authentication & Authorization Platform BRS into an implementation
roadmap for Modulus while keeping Modulus a generic, reusable .NET modular-monolith framework —
ERP-specific concepts and workflows belong in application modules, never in the framework security
kernel. Version 1.3.1 is the approved v1.3 architecture baseline **reconciled with a source-level
audit of this repository** (2026-10-15): every "current state" claim below carries file:line
evidence from that audit. When a phase completes, archive its record into
`docs/DOCUMENTATION.md` per repo convention; while work is in progress this file is canonical and
deviations are recorded inline as **As built:** entries with date, reason, impact and approval.

**Standing verification caveat:** absence from a source search is evidence of a likely gap, not
proof that a capability is absent from every integration path. Each reported gap is independently
confirmed by implementation inspection and its conformance fixture before the work item is
declared complete; each AUTH row's status flips only when its fixture runs.

---

## 1. Guiding principles

1. **Generic framework primitives only.** Identity, tenant membership, permission definitions,
   scope enforcement, field authorization, audit integrity and authorization contracts are generic
   capabilities. Buyer, warehouse, garment, purchase order and other business concepts remain in
   application modules or are represented through configured generic dimensions.
2. **One authorization decision model.** HTTP, mediator, resource, data-access, export, job,
   integration and agent paths preserve the same authorization semantics through suitable adapters.
3. **Deny by default.** Missing required information, invalid context, retired permissions and
   failed policy evaluation never grant access. Explicit denies and hard security boundaries
   cannot be overridden by ordinary grants, record shares, delegation or agent access.
4. **Optional capabilities stay optional.** Basic authentication and authorization must not
   require the full Notification, Workflow, Organization, Governance or UI module ecosystem.
5. **Security is enforced at every supported path.** A protected UI or API endpoint is not
   sufficient if Dapper, raw SQL, imports, bulk operations, reports, exports, background jobs or
   integrations can bypass the same guarantees. Unsupported direct paths must be detectable
   (analyzers, architecture rules, review policy) — never silently claimed as covered.
6. **Backward compatibility is intentional.** Changes to public contracts, package dependencies,
   database schemas, authorization semantics or defaults require compatibility review, migration
   notes and upgrade tests.
7. **Tests are part of the product.** The BRS authorization truth table is a controlled
   conformance artifact; no requirement row may be removed or weakened without formal change
   control (BRS AS-09).
8. **Framework and AI platform remain separate.** Modulus exposes generic principal-aware
   authorization and integration contracts; agent runtime, RAG, orchestration and AI UI stay in
   the standalone AI platform.

## 2. Architecture decisions (approved baseline, as verified)

### ADR-01 — Global identity and tenant memberships

**Decision:** one canonical identity per person; separate tenant-specific memberships and
authorization contexts. A person may belong to multiple tenants without duplicating the identity;
membership status, employment context, tenant attributes and authorization stay independent per
tenant; a shared identity never implies cross-tenant data access; a person key is an
identity-linking mechanism, not an authorization grant; identity linking/merging requires verified
evidence, a controlled workflow and audit records.

**As verified (2026-10-15):** the structural model **already exists** — host-context
`ModulusUser : IdentityUser<Guid>` (`src/identity/Modulus.Identity/Abstractions/ModulusUser.cs:9`)
plus per-tenant `TenantMembershipEntity { UserId, TenantId, IsActive, CreatedAtUtc }`
(`src/platform/Modulus.MultiTenancy.EntityFrameworkCore/TenantMembershipEntity.cs:8-20`), with
soft-remove, seat limits, audit and `IAccessChangeObserver` notification in `TenantManager.cs:130-189`.
**This is an additive extension, not an identity-model replacement.** The feared "tenant-local
account rows" state does not exist; the destructive-rewrite warning from earlier drafts is moot.
Remaining gaps are per-membership lifecycle granularity (beyond the `IsActive` bool), principal
kinds, person keys and sponsor/expiry metadata — all additive (work item A1).

### ADR-02 — Separate account lifecycle, administrative restriction, authentication protection

**Decision:** three logical dimensions — lifecycle (`Invited`, `PendingActivation`, `Active`,
`Disabled`, `Archived`), administrative restriction (`None`, `Suspended` with reason/actor/period),
authentication protection (normal, temporary lockout, required challenge, credential/session
validity). Lockout never deactivates an identity; suspension never erases grants or history;
disabled/archived accounts cannot initiate new access and have sessions/credentials invalidated;
reactivation never automatically restores revoked grants; rehire is an explicit reviewed process;
human-only fields are never imposed on service accounts or system jobs. Append-only transition
history (actor, reason/reference, timestamp, prior/next state, session/credential action), also
recorded in the security-audit chain.

**As verified:** four separate concerns already exist and must be **preserved, not conflated** —
user `IsActive` (enforced at the password grant, `IdentityPasswordGrantValidator.cs:58`), standard
ASP.NET lockout (`LockoutEnd`/`AccessFailedCount`, checked at `IdentityPasswordGrantValidator.cs:75-76`),
membership `IsActive`, and company-level `TenantStatus` (`Trial/Active/Suspended/Closed`;
Suspended → read-only 423, Closed → unresolvable; `src/core/Modulus.Core/Abstractions/Tenancy/ICurrentTenant.cs:64-77`).
The new lifecycle/history model (A2) is a *fifth* concern layered additively beside these; the
existing semantics of each stay unchanged.

### ADR-03 — One authorization engine with multiple enforcement adapters

**Decision:** one decision model accounting for effective principal, active tenant/membership,
permission, resource, scope, conditions, explicit denies, delegation/agent context and policy
version — exposed through adapters for every supported execution path. Explicit deny overrides
ordinary grants; hard tenant boundaries cannot be overridden by sharing, delegation or agent
context; missing required attributes fail closed with a controlled reason; lists, search, counts,
aggregates, dashboards, related lookups, exports and bulk operations use equivalent scope rules;
creation and updates validate the resulting resource scope; revocation propagates within the
approved latency target.

**As verified:** the engine and deny precedence are solid
(`PermissionResolver.Resolve`, `src/platform/Modulus.Platform/Authorization/Grants/PermissionResolver.cs:44-79` —
deny-override applied after implication closure at :74-76; unscoped deny → `ResolvedScope.None`,
`PermissionScopeResolver.cs:95-97`; a store deny beats token claims, `PermissionRequirement.cs:98-116`).
Enforcement exists for query lists/probes (`ScopeEnforcer.Apply/IsInScope`,
`ScopeEnforcer.cs:85-123`) and the tenant dimension of writes (`ModuleDbContext.cs:535-563`).
Gaps: resulting-scope validation for creates/updates (A3), missing-attribute explicitness (A4),
dimensions beyond org units (A5), jobs (A6), field paths (A7), sharing (A8), agents (A9).
EF Core interceptors alone do not secure arbitrary Dapper or raw SQL: explicitly supported
data-access patterns are guarded and documented; unsupported paths are detectable by extending
`Modulus.Analyzers` (MOD000x) and `Modulus.Testing.Architecture` (currently only
`ModuleBoundaryRules`) — no new detection mechanism is invented.

### ADR-04 — Field security: projection first, policy enforcement throughout

**Decision:** layered field security — authorized projections where practical; reusable
read/write policy contracts across application services and data access; a classified
response/export validation safety net with explicit reject-or-redact semantics; negative tests
across nested DTOs, related lookups, imports, bulk writes, alternate endpoints and exports.
Unauthorized writes are rejected, never silently ignored; no general-purpose bypass attribute;
narrow exceptions carry documented purpose, permission requirement and audit behavior.

**As verified:** `IFieldAuthorizer` (`MaskFor`/`Redact`/`AuthorizeWriteAsync`,
`src/platform/Modulus.Platform/Authorization/Fields/FieldAuthorizer.cs:16-44`; memoized, fail-closed
implementation :54-123) exists but is consumed **only** by the AI connector
(`AiResultProjector.cs:22,33`, `AiEntityHistoryCapability.cs:89`). There is no pipeline redaction,
no output safety net and no binding-time write rejection (A7 wires the existing contract in; it
does not invent one).

### ADR-05 — Append-only audit evidence with verifiable retention and disposal

**Decision:** audit integrity and retention enforcement stay in the framework; retention
administration and compliance workflows live in an optional module (B5). Committed records are
append-only; segment/archive integrity is independently verifiable via protected anchors;
retention schedules are configurable per tenant and category; legal holds block conflicting
disposal/anonymization; disposal and anonymization are themselves auditable; archival, purge,
restore and anonymization preserve the documented integrity guarantees. Historical authorization
evidence is only claimed reconstructable when the necessary historical state is actually retained.

**As verified:** the integrity core is solid and is **extended, never rebuilt** — per-tenant
SHA-256 hash chain with anchor-tamper detection (`SecurityAuditChain.cs:20-118`), write-ahead
spool (`SecurityAuditSpool.cs:8-45`), single-writer with retry
(`SecurityAuditLog.cs:96-160`), hourly anchors (`AuditAnchors.cs:54-103`), EF append-only store.
Retention schedules, legal holds, disposal, archive and anonymization are verified absent (A11).
A technical spike precedes schema approval and any production purge enablement.

### ADR-06 — Small security kernel; composable optional modules

**Decision:** reusable enforcement primitives in Modulus; business workflows, administration UX,
delivery channels and specialized reporting in optional modules. Basic authentication and
authorization work without the full Notification/Workflow/Organization/Governance/UI ecosystem;
framework events and contracts stay stable enough for optional modules to subscribe without
becoming hard runtime dependencies.

**Placement decision (approved):** ERP-agnostic optional modules live **in this repository** as
independently registered, independently testable optional packages, following the
`Modulus.Authorization.Management` pattern. Product-specific screens and workflows, and any
AI-platform implementation, stay out of the security kernel; the UI (B7) stays in the separate
`modulus-ui` repository.

### ADR-07 — Security-first delivery; Inventory pilot

**Decision:** P1a → P1b → P1c → P2 → P3 milestones with explicit dependency and release gates.
First pilot is the garments ERP Inventory module, subject to confirming it satisfies the BRS pilot
criteria; Procurement is the next candidate (purchase approvals and authority limits).

## 3. Compliance baseline and source-verified gap matrix (2026-10-15)

The framework covers ~60–70% of the BRS P1 scope. Area-level coverage from the 2026-10-15
assessment (kept as the planning baseline):

| BRS area | Coverage | Key existing assets | Key gaps (→ work item) |
|---|---|---|---|
| ID lifecycle | ~45% | Invitations, bulk import, positions, party users + ceilings, integration clients, SCIM, shop-floor | Principal kinds, person key, state model, JML, dormancy, rehire, merge (A1, A2, B1) |
| AU authentication | ~60% | Password policy + history/expiry, reset, lockout, TOTP/recovery/passkeys, step-up (RFC 9470), sessions + back-channel logout, 6 OIDC adapters + LDAP + SAML + SCIM, shop-floor, client credentials | Breached passwords, assurance matrix, progressive lockout, absolute timeout, network/time rules, impersonation, risk-based (A12) |
| OR tenancy/org | ~85% | Tenant isolation + memberships + groups, DAG org hierarchy, placements, branch scope, per-tenant DBs + RLS, write/SQL guards, suspended read-only | Effective-dated org history, calendars (B6) |
| RP catalog/roles | ~55% | Registry with sensitivity + `Requires`, deny-by-default, role inclusions, scoped/dated grants | Lifecycle/versioning, owner, impact + four-eyes, dynamic groups, rename migration (A10, A15) |
| DS scope/fields | ~60% | Tenant/Own/OrgUnit/Assigned scopes, AND/OR, scope maps, SQL enforcement + probes + export helpers, `[Classified]` field security | Creation guard, attribute dimensions, record sharing, masked references, automatic masking (A3, A4, A5, A7, A8) |
| CT conditions | ~65% | `ResourcePolicy` conditions, maker-checker, reason codes | Cumulative limits, back-dating config, rule builder (A15) |
| DN deny/precedence | ~85% | Deny grants with reason/validity winning over everything, explain, trace-id errors | Legal holds, not-found config (A11, A15) |
| DG delegation | ~80% | Time-boxed delegations with caps + live refresh, access requests → auto-expiring grants, break-glass with review | On-behalf-of on business records, JIT privilege (A15) |
| AA approval authority | ~80% | Authority by role/user/org/doc/currency, largest-limit + reason codes, multi-level approval workflow | Cumulative limits, simulation, "my authority" (A15) |
| GV governance | ~50% | Access requests with SoD pre-check, recertification campaigns, SoD preventive + detective | Exceptions with compensating control, auto-revoke, last-used, findings (A13, A15, B2) |
| AZ central decision | ~75% | One engine across HTTP/mediator/resources, explain + effective-access, policy fingerprints, next-request propagation | Job principals + re-auth, agent wiring (A6, A9) |
| AD audit | ~55% | Hash-chained per-tenant audit (append-only, spool, anchors), entity change history, login history, redaction, PII encryption | Retention/disposal/holds/anonymization, watermarking, alerts, export (A11, A14, B4, B5) |
| AX console/UX | partial | Rich admin HTTP API | Console/self-service portal (B7) |
| NT notifications | ~15% | In-app in-memory publisher, identity email queue | Channels/templates/preferences/tracking (B3) |
| IN integration | ~50% | SCIM, federation, directory group sync | HR JML contract, BI scope contract (B1, B6) |
| RE reports | ~0% | — | All reports (B4) |
| MC module contract | ~70% | `AddPermissions`, scope maps, `IScopeEnforcer`, `SecurityProbeSuite`, architecture tests | Release-gate tooling (P1c) |

Source-verified work-item level findings (full evidence in §4):

| Item | Verified current state | Gap verdict |
|---|---|---|
| A1 | Canonical identity + memberships exist (`ModulusUser.cs:9`, `TenantMembershipEntity.cs:8-20`, `TenantManager.cs:130-189`) | Partial — additive metadata only |
| A2 | `IsActive` + standard lockout + company `TenantStatus`; **no** transition history (`verified absent`) | Full gap (additive) |
| A3 | **Zero** creation/resulting-scope validation; `OUT_OF_SCOPE` exists only as a reason constant (`Resources/AccessDecision.cs:47`) | Full gap |
| A4 | `METADATA_MISSING` exists (`AccessDecision.cs:80`); `MISSING_ATTRIBUTE` and finding sink absent | Full gap |
| A5 | Typed assignment keys + effective-dated assignments exist (`ScopeMap.cs:109-122`, `Assignments.cs:21-31`); hierarchy roll-up is org-units only (`ScopeEnforcer.cs:34-41`) | Partial |
| A6 | Envelope carries `TenantId`/`CorrelationId` only (`ChannelJobQueue.cs:14-20`); no principal, no re-auth, no SystemJob | Full gap |
| A7 | `IFieldAuthorizer` exists (`FieldAuthorizer.cs:16-44`), AI-connector-only usage | Partial — wiring + safety net |
| A8 | No share primitives anywhere | Full gap |
| A9 | No agent/delegated-principal context | Full gap |
| A10 | `PermissionDefinition` has no lifecycle metadata (`IPermissionRegistry.cs:20-27`) | Full gap (cheap) |
| A11 | Chain/spool/anchors solid; retention/hold/disposal verified absent | Full gap (extends core) |
| A12 | Idle timeouts + concurrent caps + password history/expiry + session admin **service** exist; absolute lifetime, progressive lockout, breach check, impersonation, risk signals absent; session admin has **no HTTP endpoint** (`ModulusAccountController.cs:195-268` is self-service only) | Mixed |
| A13 | SoD rules block; no exception records | Full gap |
| A14 | `IAccessChangeObserver` + audit chain exist; no typed severity alert events | Full gap |
| A15 | Existence-hiding absent (only `FeatureDisabledException`→404, `ModulusErrorCatalog.cs:54`); no usage tracking; no role-impact calc | Full gap |
| Propagation | Per-request freshness by design — **no cross-request decision cache to invalidate** (`PermissionRequirement.cs:13-18`); no measured propagation mechanism | Measure, don't build a cache |
| Conformance suite | `Modulus.Testing.Authorization` does not exist; `SecurityProbeSuite` + `TenantIsolationAssertions` confirmed as extension base | New project |

## 4. Layer A — framework capabilities

Names are work-item labels, not a requirement to create one package per item. Every item follows
the completion-evidence template: **Current** (verified file:line) → **Gap** → **Change** →
**Conformance** → **Tests** → **Migration** → **TFMs** → **Exit criterion**. Checkboxes are ticked
as work lands; deviations recorded as **As built:**.

### A1. Principal kinds and person linkage — P1a

BRS: ID-01/02/04, ID-13, ID-17, ID-19, BR-25, D3.

- **Current:** canonical identity + memberships exist (ADR-01 evidence); `ExternalId` is
  SCIM/HR-provisioning only (`ModulusUser.cs:36`); no principal kinds, sponsor/expiry metadata,
  person keys or per-membership lifecycle.
- **Gap:** kind model, linkage keys, expiry processing.
- **Change:** `PrincipalKind` (`Employee`, `Contractor`, `PartnerUser`, `ServiceAccount`,
  `SystemJob`) + `SponsorId` + `ContractEndsAt` on the user; kind-specific mandatory-attribute
  validation (non-human principals never forced to supply human employment/contact fields);
  `ModulusPersonKeys` + `IPersonDirectory` — one immutable person key, login-identifier
  tombstones blocking reuse after archival (ID-19, BR-21); membership status extension lands with
  A2; expiry worker (reuse the `DelegationRoleRefresher` timer pattern) deactivates eligible
  accounts at end date with advance notice via `IIdentityEmailQueue` and `IAccessChangeObserver`
  events (ID-13, ID-17). Identity linking/merge = controlled workflow with verified evidence +
  audit (framework contract; the workflow UX is B1).
- **Conformance:** ID-area rows in the P1a set (registry in the conformance spec).
- **Tests:** kind validation matrix; person-key lookup; login-identifier reuse refused; membership
  isolation negatives; expiry deactivation + notification.
- **Migration:** additive columns (backfill `PrincipalKind = Employee`) + new table — existing
  deployments need a migration.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** fixtures green; existing membership/seat tests unchanged and passing.

### A2. Formal lifecycle and transition history — P1a

BRS: ID-05, §17.1, BR-25.

- **Current:** `IsActive` bool, standard ASP.NET lockout, membership/company states (ADR-02
  evidence); no lifecycle enum, no transition history (verified absent).
- **Gap:** explicit states + append-only history + transition matrix.
- **Change:** explicit lifecycle states on the user (`Invited`, `PendingActivation`, `Active`,
  `Disabled`, `Archived`) mapped from today's implicit ones without losing their meaning
  (`LockoutEnd` stays authentication protection, `IsActive=false` maps to Disabled);
  administrative suspension as its own dimension (reason, actor, optional effective period) —
  never a lifecycle state; `ModulusUserTransitions` append-only table (actor, reason/reference,
  timestamp, prior/next state, session/credential action), each transition also recorded in the
  security-audit chain; §17.1 transition matrix enforced (no Archived→Active; rehire is an
  explicit reviewed process; deactivation never auto-restores revoked grants; disable/archive
  invalidates sessions and credentials).
- **Conformance:** ID rows in the P1a set.
- **Tests:** full legal-transition matrix; illegal transitions refused; transition audit written;
  session/credential invalidation on disable/archive; rehire behavior.
- **Migration:** status column (backfill mapping implicit states) + transitions table — additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** matrix tests green; existing lockout/`IsActive` behavior unchanged.

### A3. Creation and resulting-scope guard — P1a (step 2 of the P1a order)

BRS: DS-15, AUTH-12.

- **Current:** no creation/resulting-scope validation anywhere (verified); query-side enforcement
  exists (`ScopeEnforcer.Apply` `ScopeEnforcer.cs:85-110`, `IsInScope` :113-123,
  `EnsureAllInScope` `ScopeEnforcerExtensions.cs:34-54` — existing records only); the write guard
  covers the tenant dimension only (`ModuleDbContext.cs:535-563`).
- **Gap:** the resulting scope of new/changed records is never validated.
- **Change:** `IScopeEnforcer.EnsureCanCreateAsync(entity, permission, ct)` evaluating the new
  record's tenant, org unit and configured dimension values against the caller's effective scope;
  equivalent validation for updates changing ownership/org/dimension values; wired into
  `ModuleDbContext.SaveChangesAsync` for entities whose scope map declares the guard — same
  interceptor pattern as the cross-tenant write guard — throwing `OutOfScopeCreateException` →
  403, reason `OUT_OF_SCOPE` (constant already reserved, `Resources/AccessDecision.cs:47`), mapped
  across the error catalog (HTTP/gRPC/GraphQL) like its sibling; supported Dapper/repository, bulk
  and import paths guarded explicitly; unsupported paths detectable via analyzers/architecture
  rules (ADR-03). Controlled denial leaks no protected resource details.
- **Conformance:** AUTH-12.
- **Tests:** in/out-of-scope create and update for org and dimension scopes; composition with the
  tenant write guard; imports and bulk writes; direct-read and list bypass attempts.
- **Migration:** none (engine only).
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-12 fixture green; no supported write path can introduce out-of-scope
  records.

### A4. Missing-attribute denials and data-quality findings — P1a (step 3)

BRS: AS-05, AUTH-08/09, BR-23.

- **Current:** `METADATA_MISSING` exists for "no policy registered for a type"
  (`Resources/AccessDecision.cs:80`, emitted by `ResourceAuthorizer.cs:105,122`); scope evaluation
  on a record with a missing required attribute currently just fails closed without an explicit
  reason; no finding sink. `IAccessChangeObserver` (`AccessChange.cs:14-18`) is the pattern to
  follow for the sink.
- **Gap:** explicit `MISSING_ATTRIBUTE` denial + structured findings + narrow declared exception.
- **Change:** scope/condition evaluation returns an explicit deny with reason
  `MISSING_ATTRIBUTE` (+ attribute name) when a required scope attribute is absent — fail-closed
  as today, but explicit and explainable; `IDataQualityFindingSink` (Core abstraction, Platform
  default writer) records tenant, resource type, record reference, missing attribute and safe
  diagnostics — never sensitive record data — surfaced through a management query; the only grant
  shape that matches the missing-attribute case is a per-resource declared
  `CoversMissingAttribute = true` (AUTH-09); ordinary wide grants never match; any exception is
  resource-specific, narrowly declared and independently tested — never an unbounded grant.
- **Conformance:** AUTH-08, AUTH-09.
- **Tests:** AUTH-08 (deny + finding raised, no data leakage); AUTH-09 (declared grant allows only
  within its scope); narrow-exception behavior; no ordinary broad grant matches.
- **Migration:** findings table — additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-08/09 fixtures green.

### A5. Generic attribute dimensions — P1a core (step 4 dependency), P1b full

BRS: DS-01/02/03.

- **Current:** typed assignment keys exist (`ScopeMap.AssignedKey`, `ScopeMap.cs:109-122`;
  `IScopeMapRegistry.AssignmentTypes` :57); assignments are effective-dated (`Assignments.cs:21-31`,
  `AuthorizationStoreDbContext.cs:397-405`); the only value hierarchy is org units
  (`ScopeEnforcer.cs:34-41`); `Assigned` scopes are flat Guid membership (:146-149).
- **Gap:** named typed dimensions with value hierarchies beyond org units, dimension metadata and
  full query-surface coverage.
- **Change:** `AddDimension("warehouse", ...)` generalizing assignment keys with typed accessors;
  per-user dimension-value assignments (`ModulusDimensionAssignments`, effective-dated, reusing
  the existing assignment shape); optional value hierarchies (`ModulusDimensionHierarchies`, a
  parent grants children, cycle prevention); dimension predicates join `ResolvedScope` clauses
  with the existing algebra (AND within a clause, OR across clauses — unchanged); `Apply` emits
  query predicates for dimension-carried properties; `IsInScope` probes single records; the
  creation guard (A3) checks them; tenant ownership, type validation, null/missing-value semantics
  and query behavior defined; management API `dimensions` endpoints under `authorization:manage`;
  compiled predicates benchmarked (NF-03) on representative high-cardinality data before broad
  rollout.
- **Conformance:** AUTH-29 (all list surfaces).
- **Tests:** union across grants, intersection within a clause, hierarchy roll-up, missing value →
  A4 finding, list/search/count/aggregate/dashboard/export all filtered.
- **Migration:** `ModulusDimensionAssignments`, `ModulusDimensionHierarchies` + dimension registry —
  additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-29 fixture green; NF-03 bench on representative data.

### A6. Job principal and execution-time authorization — P1b

BRS: AZ-04/18/19/20/21, AUTH-23/24.

- **Current:** `JobEnvelope` carries `TenantId`/`CorrelationId` only (`ChannelJobQueue.cs:14-20`;
  `Delay` is declared but never set); the worker verifies/restores tenant
  (`ProcessLoopAsync` :296-311) then executes with **no** authorization check; the Quartz variant
  is identical (`QuartzJobScheduler.cs:37-42`); no SystemJob identity (verified absent).
- **Gap:** initiating principal, execution-time re-authorization, system identities.
- **Change:** envelope + Quartz job data gain initiating principal id, `UserInitiated`
  classification, original request/decision reference + enqueue time (AZ-18); `IExecutionAuthorizer`
  hook in both runners — user-initiated work re-checks permission + membership + scope + sensitive
  policy at execution; sensitive deferred operations re-authorize before sensitive stages, result
  publication or download (marker checked by the runner, AZ-21); initiator disabled or no longer
  authorized ⇒ job cancelled safely and audited (`job.cancelled.authorization`, AZ-19); system-owned
  work runs under a named `SystemJob` identity (principal kind from A1) with explicit grants and
  scope via `RunAsSystem(name)` — a job never inherits a person's authority implicitly (AZ-20);
  the existing `modulus:machine` claim + `integration:` role machinery is the reuse seam for
  non-human principals; idempotency, retries, duplicate delivery, partial completion and safe
  cancellation semantics defined.
- **Conformance:** AUTH-23, AUTH-24.
- **Tests:** queued export after access loss cancelled + audited; scope change during execution;
  system identity boundaries; sensitive-operation re-check; step-up permissions refused without a
  principal (existing rule).
- **Migration:** none (envelope serialization only).
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-23/24 fixtures green.

### A7. Field authorization and output safety net — P1b

BRS: DS-07/10, AUTH-21/22.

- **Current:** `IFieldAuthorizer` exists with `MaskFor`/`Redact`/`AuthorizeWriteAsync`
  (`FieldAuthorizer.cs:16-44`, fail-closed memoized impl :54-123) but is consumed only by the AI
  connector; no pipeline redaction, no binding-time write rejection, no output safety net
  (verified).
- **Gap:** wiring the existing contract into supported read/write paths + safety net + exhaustive
  leakage tests.
- **Change:** response/export pipeline step applying `Redact` to serialized types carrying
  classified fields — masking stops being call-site-optional for APIs and generated exports; opt-out
  requires a `[Loosened]`-style reason and is audited; write side: binding validation rejects
  unauthorized fields in create, update, patch, bulk and import (reject the request, never silently
  ignore); authorized projections preferred where practical; no general bypass attribute.
- **Conformance:** AUTH-21, AUTH-22.
- **Tests:** AUTH-21 (unauthorized field write rejected, not ignored); AUTH-22 (a masked field does
  not leak through any endpoint, export, bulk path, related lookup or nested DTO).
- **Migration:** none.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-21/22 fixtures green including nested and alternate output paths.

### A8. Record sharing and masked references — P1b, only after core scope gates pass

BRS: DS-05, DS-09, AUTH-31, BR-10.

- **Current:** no share primitives anywhere (verified).
- **Gap:** new capability.
- **Change:** `IRecordShareStore` (`ModulusSharedRecords`: resource type, record id, grantee user
  or group, mode View/Edit, expiry, revocation, reason, granted by) consulted by
  `IsInScope`/`Apply` as a record-level union — a share widens visibility for that record only,
  never overrides an explicit deny and never crosses a tenant boundary; creating a share requires
  ownership or an explicit permission; audited; re-sharing, ownership, revocation propagation and
  active-cache behavior defined before implementation; masked references (DS-09): lookup/projection
  helper returning id + generic label for out-of-scope referenced records (never 500, never
  details), per-resource opt-in; partner isolation — an external partner reaches another partner's
  records only through an explicit valid share (AUTH-31).
- **Conformance:** AUTH-31.
- **Tests:** share grants view/edit; expiry + revoke; counts/totals unaffected by invisible shares;
  direct reads, lists, search, aggregates, exports; partner isolation.
- **Migration:** `ModulusSharedRecords` — additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-31 green; no tenant-boundary or deny bypass via shares. **Do not start
  until the A3/A4/A5 gates pass.**

### A9. Agent and delegated-principal authorization — P1b

BRS: AZ-17/22, BR-22, AUTH-25.

- **Current:** no agent/delegated-principal context (verified; the AI connector's envelope-user is
  the nearest analogue); `amr`/`auth_time` and step-up machinery exist.
- **Gap:** agent context + rights intersection at every decision point.
- **Change:** populate the reserved `ISecurityContext.Agent` (RFC 8693-style `act`): agent
  id/kind + represented principal; effective-permission resolution **intersects** the agent's own
  grants with the represented principal's effective grants — never exceeds either (BR-22) — in
  `PermissionRequirementHandler`, the mediator gate and `ResourceAuthorizer`; every protected tool
  read and tool execution passes the engine at the call boundary (AZ-22); agent context bound to
  tenant, purpose and auditable correlation; caller-supplied identity claims validated at source,
  never trusted raw; AI runtime/RAG/orchestration stay outside Modulus.
- **Conformance:** AUTH-25.
- **Tests:** agent call beyond the principal's effective scope denied; intersection never exceeds
  either side; direct tool bypass denied; every operation auditable.
- **Migration:** none.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-25 green.

### A10. Permission catalog lifecycle — P1a (step 4)

BRS: RP-13/14, AUTH-30, BR-18.

- **Current:** `PermissionDefinition(Permission, Description, Requires, Sensitivity)` — no
  lifecycle metadata (`IPermissionRegistry.cs:20-27`); `PermissionRegistry.Freeze()`
  (`PermissionRegistry.cs:46`) is the natural validation boundary; `AccessChangeKinds.Feature`
  exists (`AccessChange.cs:40-65`); deny-by-default already keeps new permissions inert.
- **Gap:** lifecycle states, rename migration, change notice.
- **Change:** `Lifecycle` (`Active`/`Deprecated`/`Retired`) + `ReplacedBy` mapping on definitions:
  deprecated is visible for migration but not newly assignable; retired fails checks (deny); a
  renamed permission keeps existing grants via the mapping; duplicate/incompatible definitions
  validated at `Freeze()`; `PermissionInitHostedService` computes a boot-time catalog diff
  (new/changed/retired) and publishes a `PermissionCatalogChanged` platform event + administrative
  notice through the optional notification integration (B3 when present) — the engine never
  depends on delivery success; new permissions stay inert unless assigned by approved default-role
  policy; deployment rollback behavior defined.
- **Conformance:** AUTH-30.
- **Tests:** AUTH-30 green (visible, ungranted, notice issued); rename preserves grants; deprecated
  assignment refused; retired denies; rollback.
- **Migration:** catalog lifecycle columns — additive, no data migration, defaults preserve
  behavior.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** AUTH-30 green; catalog changes never silently grant access.

### A11. Audit retention and evidence engine — P1c engine; P2 administration (B5)

BRS: AD-03/05/11, §16.4, BR-14.

- **Current:** integrity core solid (ADR-05 evidence); retention/holds/disposal/archive verified
  absent.
- **Gap:** retention schedules, legal holds, integrity-protected disposal, anonymization,
  point-in-time evidence.
- **Change (extends the existing chain, never rebuilds it):** retention schedules per tenant ×
  audit category (`Security:Audit:Retention`); legal holds (`ModulusAuditLegalHolds`: scope,
  reference, held by, release) suspending disposal and anonymization for their documented scope;
  disposal never edits committed entries — it closes a chain segment writing an
  integrity-protected `DisposalRecord` with its own anchor, then archives/purges per policy, and
  the disposal event itself is audited (BR-14); anonymization via `AuditIdentityMap`
  (person ↔ stable pseudonym) whose erasure/restriction never touches committed events (§16.4);
  point-in-time evidence `WhoHadAccessAsync(userId, at)` joining effective-dated
  grants/assignments/authorities/position holdings with the audit chain (AD-11); restore
  validation and purge semantics defined. **Technical spike (chain verification across
  archival/disposal/restore scenarios) precedes schema approval and production purge enablement.**
- **Conformance:** AD rows (P1c/P2 sets).
- **Tests:** chain verification across disposal; hold blocks disposal; pseudonym stable after map
  erasure; evidence query reproduces a past state and discloses its limits; restore validation.
- **Migration:** schedules, holds, identity map, disposal records — additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** spike approved; integrity tests green across archive/disposal/restore.

### A12. Authentication hardening — P1c slice; P2 slice

BRS: AU-03/05/06/08/09/12, §18.1.

- **Current (verified):** session registry with idle timeouts (per role) + concurrent caps + end
  reasons (`Sessions.cs:18-60`, entity :162-202) but **no absolute lifetime** (token expiry is the
  only cap); standard flat lockout 5 attempts/5 min (`ModulusIdentityOptions.cs:209-218`) — not
  progressive; password history + expiry exist (:178-204); `IUserSessionService` admin capability
  exists at service level (`UserSessionService.cs:22-36`) but only self-service HTTP endpoints
  exist (`ModulusAccountController.cs:195-268`); impersonation and risk signals verified absent;
  `amr`/`auth_time` + step-up exist.
- **Gap:** absolute lifetime, progressive lockout, breach check, admin endpoints (P1c); assurance
  matrix, network/time rules, impersonation, risk signals (P2).
- **Change (P1c):** absolute session lifetime (`MaxLifetimeMinutes`) alongside idle timeouts;
  progressive-delay lockout (escalating schedule, documented reset behavior) replacing the flat
  window; `IBreachedPasswordChecker` abstraction — off by default, no vendor lock-in; local-list
  or k-anonymity adapter app-side, with privacy, offline behavior, update cadence and
  operational-failure policy evaluated (dependency policy §6); permissioned administrative HTTP
  endpoints to list/end any user's sessions (extends the existing service).
- **Change (P2):** MFA factor assurance matrix per §18.1 (email/SMS never sole MFA for privileged
  roles); network/time-window login policies after B6 calendars; impersonation — dedicated
  permission, mandatory reason, time-boxed session, visible indication, `act` claim pair
  (actor/represented), both identities audited, blocked for approvals/payments/security changes
  unless explicitly approved by owners; risk signals (new device, unusual location/time, impossible
  travel) with explicit allow/challenge/deny/alert outcomes, provider-failure policy (never a
  silent downgrade for privileged authentication) and false-positive monitoring.
- **Conformance:** AU rows per registry.
- **Tests:** timeout math; escalation schedule; breach-check hook incl. provider failure; assurance
  matrix; impersonation blocked-action list; risk-signal outcomes.
- **Migration:** options-led; minimal schema (impersonation audit rides the security audit).
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** P1c session/lockout fixtures green; P2 items land only under approved
  requirements.

### A13. SoD exceptions — P2

BRS: GV-10, BR-16.

- **Current:** SoD rules block preventively (grant/scoped-grant/access-request/break-glass flows);
  no exception records (verified).
- **Gap:** time-limited exceptions with compensating controls.
- **Change:** `ModulusSodExceptions` (rule, affected users/roles, justification, **compensating
  control**, independent approver — never the grantor, expiry, review metadata); preventive checks
  consult active exceptions and flag the decision + audit `ExceptionApplied`; expiry restores
  blocking; periodic review surfaced to B2 campaigns.
- **Conformance:** GV rows (P2 set).
- **Tests:** exception allows a grant and flags it; expiry re-blocks; self-approval of an exception
  refused; revocation.
- **Migration:** table — additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** GV fixtures green.

### A14. Security alert events — P2

BRS: AD-08, CT-10.

- **Current:** `IAccessChangeObserver` + hash-chained audit exist; no typed severity alert events
  or thresholds (verified).
- **Gap:** classified alert stream.
- **Change:** severity-tagged events (privileged grants, break-glass, repeated denials per
  principal, off-hours administration, lockout patterns, SoD violations, unusual extraction);
  export-volume thresholds tenant-configurable with defined alert/challenge/block behavior
  (`ForExport` helpers); routing through the optional notification contract with digest support
  for low severity — **enforcement never depends on delivery success**; event idempotency,
  deduplication, retry and delivery-failure behavior defined.
- **Conformance:** AD/CT rows (P2 set).
- **Tests:** exactly one classified event per trigger; threshold math; dedup; delivery outage.
- **Migration:** none (rides audit + events).
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** alert fixtures green.

### A15. Governance engine completions — P2

BRS: GV-05/06/07, CT-03/07/08, AA-06, RP-11, DN-10, RE-10.

- **Current:** existence-hiding absent (only `FeatureDisabledException`→404,
  `ModulusErrorCatalog.cs:54`); no permission usage tracking; no role-change impact calculation
  (`Explain`/available-actions exist as building blocks); no bulk preview contracts (verified).
- **Gap:** engine-level calculations for governance, separate from governance workflows (B2).
- **Change:** last-used tracking — nightly rollup of the decision-audit stream into
  `ModulusPermissionUsage`; campaign auto-revoke — deadline policy (`AutoRevokeAfterDays`) executed
  by a hosted service with eligible grant types, exclusions, notification, emergency-access
  treatment and recovery defined **before** enabling; cumulative approval limits — per-authority
  accumulators (`ModulusAuthorityUsage` ledger, concurrency-safe) checked by
  `IApprovalAuthorityEvaluator`; role change impact — affected users, newly effective sensitive
  permissions, SoD conflicts introduced (+ optional four-eyes on Critical role changes); bulk-op
  preview contracts + policy fingerprints; existence-hiding (DN-10) — per-resource `HideExistence`
  config → out-of-scope direct reads answer 404 instead of 403.
- **Conformance:** GV/RP/DN rows (P2 set).
- **Tests:** rollup correctness; auto-revoke fires with exclusions; accumulator blocks over-limit
  approval under concurrency; impact report matches a seeded setup; 404-vs-403.
- **Migration:** `ModulusPermissionUsage`, `ModulusAuthorityUsage` — additive.
- **TFMs:** net8.0;net10.0.
- **Exit criterion:** P2 engine fixtures green.

## 5. Layer B — optional platform modules (ERP-agnostic)

Built as optional packages **in this repository** (ADR-06 placement), with standard module
conventions: each registers its permissions, applies scopes via `IScopeEnforcer`, ships
allow + deny security tests for every secured resource, and consumes framework contracts rather
than reimplementing authorization logic.

| Module | Delivers (BRS) | Built on |
|---|---|---|
| **B1 `AccessLifecycle`** | JML orchestration, templates, tracked leaver process, dormancy, rehire, dedupe, partner self-admin | A1/A2 person + state model, `ITenantMembershipStore`, `ISecurityAuditLog`, B3 |
| **B2 `AccessGovernance`** | Request catalog UX flows, campaign scheduling, SoD exception admin, findings register, orphan/peer reports, birthright baselines | Framework access-request/recertification/SoD endpoints, A13/A15 |
| **B3 `Notification`** | Channels (email/SMS adapters), templates, localization (EN/BN), preferences, delivery tracking + retry, alert routing + digests | `INotificationPublisher` seam, `IIdentityEmailSender`, outbox |
| **B4 `ComplianceReporting`** | RE-01..12, export governance (masking, watermarking, row limits, approval, download expiry) | Scoped queries (`ForExport`), `ISecurityAuditStore.QueryAsync`, A11 evidence query |
| **B5 `AuditAdministration`** | Retention schedule admin, legal holds, disposal workflow (four-eyes), anonymization/DSAR intake, audit search/export API | A11 engine |
| **B6 `OrganizationProjection`** | Effective-dated org/position sync from the ERP Organization module; calendars/shifts for time rules | `EfOrgHierarchy` (+ effective dating), integration events |
| **B7 Admin & self-service UI** (`modulus-ui` repo) | Console, user 360, role designer (impact preview), scope designer, request/review pages (mobile-friendly), self-service portal, onboarding wizard, i18n EN/BN | Layer A/B APIs |

Key flows:

- **B1 leaver (ID-08, BR-09, D6):** at the effective time the module calls the framework
  revoke-all path (sessions, tokens, security stamp — within the NF-09 target), then enumerates
  affected assignments (owned records via scope maps, pending approvals via the workflow inbox,
  delegations, scheduled jobs via A6 metadata) and opens a **tracked leaver case** with one task
  per owning module; each module applies its own reassignment rules. Revocation never waits for
  reassignment.
- **B1 mover (ID-07):** effective-dated revocation list for the previous area + a confirmation task
  for the new manager; both audited.
- **B6 projection (D2):** the ERP Organization module remains the master of hierarchy, positions
  and reporting lines; B6 syncs them into the framework's authorization projection
  (`EfOrgHierarchy`, placements, org profiles) as effective-dated changes (OR-04) and publishes
  calendars for A12's time windows (OR-10). A stale projection never permits crossing a tenant
  boundary; freshness requirements and fail-safe behavior are defined for security-critical
  updates.

## 6. Mandatory repository gates (definition of done for every affected work item)

1. **Target frameworks:** every affected Layer A/B library preserves `net8.0;net10.0`
   multi-targeting — per-TFM dependency versions in `Directory.Packages.props`, conditional
   compilation for net8 gaps, the `build/Polyfills/` polyfills, and the existing net8 test
   exceptions (`-p:ModulusTestTfm=net8.0`).
2. **Public API compatibility:** new public contracts go in `PublicAPI.Unshipped.txt`
   (`build/Modulus.PublicApi.props`; RS0016/RS0017 effectively run on net10, the reference
   surface) and follow the normal shipped-API process.
3. **In-memory store detection:** new stores are named and defaulted consciously —
   `AddModulusInMemoryStoreCheck` flags any `InMemory*` implementation of a `Modulus.*` `*Store`
   contract outside Development (`InMemoryStoreCheck.cs:99-141`); no misleading name and no
   production fallback that weakens the guard.
4. **SecurityGuard and generated hosts:** changes to enforced authorization paths, 404/403
   behavior or middleware defaults are validated against the host startup guard
   (`SecurityGuardExtensions.cs:30-48`), the loosening allowlist, the CLI templates and the
   generated-host test suites (`ApiPermissionTests`, `SecurityProbeTests`, template tests).
5. **Dependencies:** Central Package Management + the open-source-only license policy. For
   breached-password checking, evaluate a local list or a k-anonymity range API — privacy,
   offline behavior, update cadence and operational-failure policy included.
6. **Documentation:** one canonical active implementation plan; the `docs/DOCUMENTATION.md`
   registration pointer is updated in the same change.

Plus the standing repo conventions: `TreatWarningsAsErrors` (fix root causes, never suppress),
`Category=Unit`/`Integration` traits, `dotnet format modulus.slnx --verify-no-changes` clean,
architecture tests green, and every new table called out as "existing deployments need a
migration".

## 7. Delivery phases and the approved P1a order

| Step | Framework (A) | Modules (B) | Exit criteria |
|---|---|---|---|
| **P1a** core enforcement | A1, A2, A3, A4, A5 core, A10 | B1 basic (manual/bulk/SCIM joiner + leaver revocation), admin essentials | AUTH-01–02, 04–14, 26, 30, 34 green in the conformance suite; tenant isolation + deny-by-default demonstrated on the pilot module; creation/resulting-scope enforcement covers all supported write paths; missing-attribute denials explicit and never broad grants; permission catalog changes never silently grant; every high-risk operation audited |
| **P1b** cross-channel | A5 full, A6, A7, A8, A9 | B6 projection, B3 minimum (invitation/reset/alert mail) | AUTH-21–25, 27–29 green; no bypass through UI/API/query/export/job/agent paths on the pilot module; field leakage tests pass across nested and alternate output paths; jobs re-authorize at execution and sensitive stages; revocation propagation and authorization latency measured against approved targets (NF-03, NF-09) |
| **P1c** ERP rollout | A11 engine, A12 P1c slice | B1 full, B4 (RE-01/05/07), IN-10 import/export | Every module passes MC-01..06 (security suite + `SecurityProbeSuite` + architecture checks); migration from a real prior schema + rollback rehearsal complete; performance acceptance on representative data; audit integrity verified across approved archive/restore scenarios; session lifetime, lockout and admin session controls pass; operational runbooks ready |
| **P2** governance | A13, A14, A15, A12 P2 slice | B2, B3 full, B5, B7 console | AUTH-15–20, 31–32 green; first review campaign completed in a pilot tenant; SoD clean or excepted with approvals; break-glass review and impersonation controls demonstrated; retention/legal-hold workflows approved; auto-revoke (where enabled) tested with exclusions and recovery |
| **P3** intelligence | — | Role mining, anomaly detection, large-scale what-if, JIT privilege (DG-11), kiosk/badge sign-in (AU-10), partner federation (IN-09) | Per-feature effectiveness, false-positive, privacy, safety and rollback criteria defined and met; no P3 capability required for the P1 baseline |

**Approved P1a implementation order** (establish the proof harness before touching identity
persistence):

1. **Authorization conformance skeleton** — create `Modulus.Testing.Authorization` + the AUTH-row
   traceability map (per [`docs/AUTHORIZATION_CONFORMANCE.md`](AUTHORIZATION_CONFORMANCE.md));
   cover allow, deny, missing metadata, tenant isolation, scope enforcement and regression
   behavior.
2. **A3** — creation and resulting-scope validation across supported write paths.
3. **A4** — missing-attribute handling with structured, non-leaking diagnostics.
4. **A10** — permission catalog lifecycle metadata + validation + change notice.
5. **A1/A2** — identity and lifecycle extensions (only after the harness passes): minimal
   principal-kind/linking metadata + lifecycle/history support, additive migrations, membership +
   seat tests passing, rollback and compatibility verified.
6. **B1 minimum** — lifecycle operations exercising invitation, activation, disabling, suspension
   distinctions and session revocation; transitions audited and authorization-protected.

**Scope discipline:** P1a is not expanded into the whole identity platform. A6, A7, A8, A11
retention, advanced A12 and A13–A15 stay in their planned phases unless a dependency or a
demonstrated security risk requires pulling one forward (recorded as an As-built deviation).
A1/A2 must not be postponed indefinitely — they start once steps 1–4 prove the invariants and the
migration assumptions.

**Row coverage note:** AUTH-03 and AUTH-33 were not assigned by the earlier tables; the conformance
registry assigns every row 01..34 a phase and fixture when it is authored (first task of step 1).

## 8. Provisional policy defaults (unapproved owner decisions)

Unresolved policy inputs — retention durations by category/jurisdiction, privacy/anonymization and
DSAR obligations, exact NFR targets, MFA assurance requirements (incl. shop-floor compensating
controls), tenant-specific IdP/credential policy, record-sharing scope in the first release,
pilot acceptance scenarios and named approvers, and any impersonation exception — are **never
invented by engineering**. Until an owner approves them:

- each policy is a configuration option with a documented provisional default and a `Provisional`
  marker;
- outside Development, unapproved provisional policies surface a startup warning listing the
  policy, its default and the owner who must approve it — production deployment never silently
  treats a placeholder as approved compliance policy;
- approvals are recorded in the BRS document-control record and the decision log (§10).

## 9. Test and acceptance strategy

- **Truth-table conformance suite** — `Modulus.Testing.Authorization` (new, spec:
  [`docs/AUTHORIZATION_CONFORMANCE.md`](AUTHORIZATION_CONFORMANCE.md)): one traceable fixture per
  AUTH-01..34, deterministic test world (≥2 tenants, ≥2 org nodes, two ownership values, one
  record with a missing required attribute, active + expired grants, direct + role grants, a
  delegate; agent context added with A9). Runs in CI on every engine change; the suite is
  change-controlled (AS-09) — fixture weakening requires a BRS change-record reference.
- **Per-app security probes** — `SecurityProbeSuite` + `TenantIsolationAssertions` (shipped,
  `src/testing/Modulus.Testing/Security/`) run against every generated app; the CLI `generate-crud`
  scaffold gains an MC-05-style security test class (direct read, list, search, aggregate, export,
  create, update, field write — allow and deny).
- **Negative testing is mandatory for acceptance** (BRS §15): API bypass of hidden UI, jobs,
  reports, exports, imports and integrations.
- **Concurrency and propagation** — simultaneous role/grant changes, revocation, session
  termination, cached decisions, job execution and membership suspension; propagation measured
  from accepted security change to protected operation against the approved target (numeric target
  pending D9; the current design is next-request freshness by design,
  `PermissionRequirement.cs:13-18` — there is no cross-request decision cache to invalidate, so
  the measurement validates rather than builds).
- **NFR benches** (`tests/benchmarks`): decision p95 incl. scope resolution (NF-03), end-to-end
  propagation incl. live sessions and jobs (NF-09), audit search on retained volume (NF-19),
  multi-tenant fairness (NF-06) — representative garment ERP volumes and high-cardinality
  dimensions, not only small unit fixtures.

## 10. Decisions D1–D10 (BRS §20) and owner confirmations

| Decision | Position taken by this plan | Status |
|---|---|---|
| D1 credential/token authority | Framework as-is: local OpenIddict authority; external IdPs federate, assurance mapped (already built) | Proposed; needs sign-off |
| D2 organization master data | B6: ERP Organization module is master; framework store is the read-only effective-dated authorization projection | Proposed; needs sign-off |
| D3 tenant/identity boundary | Additive extension of the existing canonical-identity + membership model (ADR-01 as verified); person-key semantics documented as the equivalent guarantee of the BRS's global-person wording | Proposed; needs sign-off |
| D4 workflow routing | Workflow module owns routing/reminders/escalation; platform supplies eligibility, limits, delegation resolution (already the built seam) | Proposed; needs sign-off |
| D5 notification delivery | B3 owns channels/templates/preferences/tracking; platform emits events | Proposed; needs sign-off |
| D6 leaver process | B1: revocation at effective time, tracked reassignment, never delayed | Proposed; needs sign-off |
| D7 pilot module | Inventory first (subject to BRS pilot criteria), Procurement next | Open — needs business approvers |
| D8 audit retention/privacy | A11 + B5; durations and obligations are provisional (§8) until compliance owners approve | Open |
| D9 NFR targets | §8 provisional; validated at P1b benches before baseline | Open |
| D10 MFA assurance | A12 factor matrix per §18.1, incl. shop-floor compensating controls; provisional until approved | Open |

Owner confirmations still required (never invented by engineering): the §8 list. Names and dates
are recorded in the BRS document-control record at baseline.

## 11. Migration notes (framework)

New schema introduced by Layer A — **existing deployments need a migration** when adopting; each
lands as its own migration so tenants adopt incrementally; all are additive:

- A1: `PrincipalKind`/`SponsorId`/`ContractEndsAt` on the user table; `ModulusPersonKeys`.
- A2: user status column (+ backfill mapping existing implicit states); `ModulusUserTransitions`.
- A4: data-quality findings table.
- A5: `ModulusDimensionAssignments`, `ModulusDimensionHierarchies` (+ dimension registry).
- A8: `ModulusSharedRecords`.
- A10: catalog lifecycle columns (no data migration; defaults preserve behavior).
- A11: retention schedules, `ModulusAuditLegalHolds`, `AuditIdentityMap`, disposal records.
- A13: `ModulusSodExceptions`.
- A15: `ModulusPermissionUsage`, `ModulusAuthorityUsage`.

Before each migration: capture the current schema and production-relevant data shape; define
additive/backfill strategy, mapping, indexes, constraints and tenant ownership; define upgrade,
rollback and recovery behavior (a destructive down migration is not automatically a safe rollback
after new data exists); test against a representative prior-version database and a populated
tenant; document package/API compatibility and configuration defaults.

## 12. Risks

| Risk | Mitigation |
|---|---|
| Identity model migration breaks existing accounts | ADR-01 is additive; preserve identifiers/mappings, staged migration, reconciliation reports, rollback rehearsal; A1/A2 start only after the conformance harness passes |
| Scope checks exist only in EF Core | A3 defines supported guarded data-access patterns; Dapper, bulk, import, export and direct paths tested; unsupported paths detectable via analyzers + architecture rules |
| Stale permission/scope cache after revocation | No cross-request cache exists by design (next-request freshness); propagation measured, bounded staleness documented, concurrency tests |
| Field masking misses an alternate output path | Projection-first policy, output safety net, exhaustive leakage tests (AUTH-22) |
| Record sharing becomes a tenant-boundary bypass | Tenant-bounded shares, hard-deny precedence, explicit resource opt-in, independent aggregate tests; gated behind core scope gates |
| Jobs continue after access loss | Execution-time and sensitive-stage re-authorization, safe cancellation and publication checks (A6) |
| Audit purge damages integrity evidence | Technical spike first, protected anchors, archive verification, legal-hold tests, controlled purge enablement (A11) |
| Risk-based authentication blocks legitimate users or weakens on provider failure | Explicit provider-failure policy, challenge/block rules, false-positive monitoring, no silent downgrade (A12) |
| Governance features become mandatory dependencies | Stable contracts/events; optional module composition tests (ADR-06) |
| Dimension predicates slow large lists | Compiled predicates + NF-03 benchmark early in A5; representative data in P1b |
| BRS and implementation plan drift | Traceability matrix (conformance spec), controlled decision log, review at each phase completion |

## 13. Traceability summary

| BRS area | Work items |
|---|---|
| ID | A1, A2, B1 |
| AU | A12 (+ existing sessions/step-up/federation), B7 |
| OR | B6 (+ existing tenancy/org engine) |
| RP | A10, A15, B2, B7 |
| DS | A3, A4, A5, A7, A8 (+ existing scope engine) |
| CT | A15 (+ existing ResourcePolicy/authority engine) |
| DN | A11 (holds), A15 (DN-10) (+ existing deny engine) |
| DG | A15, B1/B2 (+ existing delegation/access-request/break-glass) |
| AA | A15 (+ existing authority + approval workflow) |
| GV | A13, A15, B2 |
| AZ | A6, A9 (+ existing decision engine) |
| AD | A11, A14, B4, B5 (+ existing audit chain) |
| AX | B7 |
| NT | B3, A14 |
| IN | B1 (HR), B6 (org), existing SCIM/federation |
| RE | B4 |
| MC | §9 test strategy + CLI scaffolding |

## 14. Definition of done for every work item

A work item is not complete merely because code compiles. It must include, as applicable:

- BRS requirement IDs and an updated traceability record (conformance registry).
- Architecture decision or contract changes documented; **As built** entries for deviations.
- **Completion evidence:** source location, intended change, conformance tests, migration impact,
  affected target frameworks, and a measurable exit criterion — "implemented" means the tests
  prove the behavior, not that a service or endpoint exists.
- Positive, negative, tenant-isolation and regression tests.
- The §6 repository gates (dual targeting, PublicAPI, InMemoryStoreCheck, SecurityGuard/generated
  hosts, dependency policy, documentation pointer).
- Performance evidence for query/authorization changes; safe diagnostics and audit behavior.
- Migration and existing-deployment notes.
- Documentation and operator guidance; verified behavior in CI; a clear statement of remaining
  limitations.

At phase completion, archive the completed phase record into `docs/DOCUMENTATION.md` per repo
convention. While work is in progress, this file is the canonical living plan.
