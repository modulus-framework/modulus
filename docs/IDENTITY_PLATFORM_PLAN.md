# Modulus Identity Platform Implementation Plan

**Version:** 1.3 (architectural baseline, from the BRS v1.2 review)
**Status:** Gap assessment recorded (2026-10-18). P1a items A10, A4, A3, A7 (partial), A5 (write-side scope and typed dimensions, option 1) and A1/A2 (inventory, schema, lifecycle enforcement, explicit lifecycle API) are built. Remaining work is in section 12; owner decisions are in section 13.
**Scope:** Generic Modulus framework primitives plus optional modules. First consumer: the garments ERP.

This is the living plan. Completed phases move to `docs/DOCUMENTATION.md`. Deviations go in an "As built" section with date, reason, impact and approval.

## 1. Principles

1. Generic framework primitives only. Business concepts (buyer, warehouse, purchase order) live in application modules or in configured generic dimensions.
2. One authorization decision model. HTTP, mediator, resource, data access, export, jobs, integrations and agents keep the same semantics through adapters.
3. Deny by default. Missing attributes, invalid context, retired permissions and failed evaluation never grant access. Explicit denies and tenant boundaries cannot be overridden.
4. Optional capabilities stay optional. Basic auth must work without the Notification, Workflow, Organization, Governance or UI modules.
5. Enforcement at every supported path, not just the UI or API layer.
6. Backward compatibility is deliberate: each schema, package or default change needs a migration note and an upgrade test.
7. Tests are the product. The BRS truth table is a controlled conformance artifact; rows are not weakened without change control.
8. The AI platform stays separate. Modulus exposes principal-aware contracts only.

## 2. Architecture decisions

- **ADR-01 Canonical identity and tenant memberships.** One person, many tenant memberships, each with its own status, assignments, roles, grants and data scopes. A shared identity never implies cross-tenant access. Any move from tenant-local accounts to canonical identity needs a staged, reviewed migration that preserves IDs or maps them explicitly.
- **ADR-02 Lifecycle, restriction and authentication protection are separate.** Lifecycle: Invited, PendingActivation, Active, Disabled, Archived. Restriction: None or Suspended (with reason and actor). Protection: lockout and challenges. Transitions are append-only and audited. Reactivation does not restore revoked grants. Service accounts are not forced through human-only fields.
- **ADR-03 One engine, several adapters.** Explicit deny wins. Tenant boundaries are hard. Missing attributes fail closed. Lists, counts, aggregates, exports and bulk operations use the same scope rules. EF interceptors alone do not secure Dapper or raw SQL; supported patterns must be guarded and the rest detectable.
- **ADR-04 Field security is layered.** Projection first, then read/write contracts, then an output safety net, then negative tests. Unauthorized writes are rejected, not ignored. No general bypass attribute.
- **ADR-05 Audit evidence is append-only and verifiable.** Retention and legal holds are enforced by the framework. Retention administration belongs to an optional module. Retention schema waits for a technical spike.
- **ADR-06 Small kernel, optional modules.** Enforcement primitives live in Modulus. Workflows, UX and delivery live in modules that consume stable contracts.
- **ADR-07 Security-first delivery.** P1a, P1b, P1c, P2, P3 with release gates. Inventory is the garments pilot; Procurement is the next candidate.

## 3. Work items relevant to the current request (permission, data scope, field scope)

| Item | What it requires | Current state (verified 2026-10-18) |
|---|---|---|
| **A10 Permission catalog lifecycle** (P1a) | Active / Deprecated / Retired states, replacement mapping, catalog-change notice, new permissions inert by default, rename handling | **Gap.** `PermissionDefinition` (`Modulus.Core/Abstractions/Permissions/IPermissionRegistry.cs`) has name, description, requires and `Sensitivity` only. No lifecycle, no replacement, no change notice. Duplicate definitions are "last wins" through `Add`. |
| **A3 Creation and resulting-scope guard** (P1a) | `EnsureCanCreateAsync` over tenant, org and dimension values; checks on update when scope-defining values change; `OUT_OF_SCOPE` denial | **Gap.** `ScopeEnforcer` (`Platform/Authorization/Scopes/ScopeEnforcer.cs`) has `Apply` (IQueryable) and `IsInScope` (record probe). No create or update guard and no `OUT_OF_SCOPE` reason. |
| **A4 Missing-attribute denial and findings** (P1a) | `MISSING_ATTRIBUTE` denial, `IDataQualityFindingSink`, no leakage, no broad fallback grant | **Gap.** Nothing named `MISSING_ATTRIBUTE` or `IDataQualityFindingSink` exists. `ResourceRequest` (`Platform/Authorization/Resources/ResourceRequest.cs`) exists but its missing-value path is not verified. Needs a check before implementation. |
| **A5 Generic attribute dimensions** (P1a core, P1b full) | Named typed dimensions (e.g. `warehouse`), effective dating, hierarchies, consistent application across list, count, aggregate, export and create/update | **Partial.** Assignment types and `AddScopeMap<T>` with `AssignedKey` exist; org subtree via `IOrgHierarchy`. No named typed dimension model, no effective dating on assignments as a general rule, no hierarchy on arbitrary dimensions. |
| **A7 Field authorization and output safety net** (P1b) | Read/write field contracts, reject unauthorized writes on every path, output validation, negative tests across nested, bulk, import and export paths | **Partial.** `IFieldAuthorizer` (`Platform/Authorization/Fields/FieldAuthorizer.cs`) has `MaskFor`, `Redact<T>` and `AuthorizeWriteAsync`. Unverified: whether import, bulk, nested DTO and export paths call them. Negative tests for those paths are not confirmed. |
| **A8 Record sharing** (P1b, after core gates) | Tenant-bounded shares, expiry, revocation, masked references | **Gap.** Not present. |
| **A6 Job principal re-check** (P1b) | Initiator re-check at execution and sensitive stages, `SystemJob` identity | **Unverified.** Needs a source check of the job envelope. |
| **A9 Agent and delegated principal** (P1b) | Effective access bounded by agent and represented principal | **Unverified.** Delegation exists (`DelegationRoleRefresher`, delegation store); agent identity not checked. |

Identity items for later (not started in this round):

| Item | Current state |
|---|---|
| **A1 / A2 / ADR-01 / ADR-02** | **Gap.** `ModulusUser.IsActive` is a bool. Lockout is separate. No lifecycle matrix and no transition history. Identity is tenant-local (`TenantId` on the user row); no canonical identity. Needs a schema inventory and a migration design first. |
| **A12 P1c hardening** | Partial: lockout, session timeouts, session listing and termination exist. Absolute session lifetime and breached-password checking are not confirmed. |

Already in place and reusable: the SCIM service is now generic over the role type (step 3, 15/15 SCIM tests pass on net10.0); `ScopeEnforcer` and `PermissionScopeResolver`; `IFieldAuthorizer`; `ICurrentDataScope`; `IDataQualityFindingSink` is not present.

## 4. Proposed P1a slice (next, in order)

1. **A10 permission lifecycle.** Add `PermissionLifecycle` (Active, Deprecated, Retired) and optional `ReplacedBy` to `PermissionDefinition`. Defaults keep existing registrations Active, so this is additive. Duplicate definitions with conflicting metadata fail at startup. Tests: retired denies, deprecated refuses new assignment, rename mapping, new permission grants nothing by default.
2. **A4 missing attributes.** Add the `MISSING_ATTRIBUTE` reason in the `AccessReasonCodes` set and make the resource path deny with it. Add `IDataQualityFindingSink` with a no-op default. Tests: deny plus finding, no value in the finding, no broad grant.
3. **A3 resulting-scope guard.** `EnsureCanCreateAsync<T>` and an update variant that check the resulting scope values, returning `OUT_OF_SCOPE` without revealing the record.
4. **A7 verification.** Audit every write path (import, bulk, nested DTO) for `AuthorizeWriteAsync` calls. Add missing calls and negative tests. This is verification-first: fix only confirmed gaps.
5. **A5 dimensions.** Only after 1 to 4 pass; benchmark the predicates first.

Items that need owner decisions first (BRS section 9): audit retention durations, privacy and DSAR obligations, numeric NFR targets, MFA assurance for shop-floor users, tenant identity-provider policy, whether record sharing ships in the first release, pilot acceptance scenarios, and any impersonation exception.

## 5. Release gates

- **P1a exit:** canonical identity and lifecycle documented and migrated safely; P1a truth-table rows pass; tenant isolation and deny-by-default shown on the pilot; create and update scope enforced on all supported write paths; missing attributes never become grants; catalog changes grant nothing; high-risk operations audited.
- **P1b exit:** API, mediator, data access, import, bulk, export, job and agent paths keep the same guarantees; field leakage tests pass across nested and alternate outputs; jobs re-authorize; revocation latency measured.
- **P1c exit:** pilot module contract and probes pass; migration rehearsed on a real prior schema; performance measured on representative data; audit integrity verified across archive and restore; session and lockout controls tested; runbooks ready.
- **P2 and P3:** governance, SoD exceptions, alerts, advanced authentication, and optional intelligence, each with its own gate (unchanged from v1.3).

## 6. Test strategy

- A conformance suite `Modulus.Testing.Authorization`, one fixture per truth-table row, with at least two tenants, org nodes, missing attributes, active and expired grants, direct and role grants, and delegation or agent contexts.
- Extend `SecurityProbeSuite` for read, list, search, count, aggregate, export, create, update, classified fields, bulk, imports, related lookups, jobs and agent calls.
- Concurrency tests for role and grant changes, revocation, session termination and cache invalidation; measured staleness against the target.

## 7. Migration rules

Before any schema change: capture the current schema; verify the gap; define additive and backfill steps; define rollback (a destructive down migration is not a rollback once new data exists); test on a populated prior-version database; document the package, API and default changes; state whether existing deployments need a migration and whether the feature is opt-in.

Likely schema areas: canonical identity and membership, lifecycle history, data-quality findings, dimension assignments, record sharing, permission lifecycle metadata, audit retention and holds, SoD exceptions, permission and authority usage.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Identity migration breaks accounts | Preserve IDs or map them; staged migration; reconciliation; rollback rehearsal |
| Scope checks only in EF Core | Guard supported patterns; test Dapper, bulk, import, export and direct paths |
| Stale cache after revocation | Explicit invalidation; bounded staleness; propagation tests |
| Field masking misses an alternate output | Projection first; output safety net; leakage tests |
| Record sharing bypasses tenants | Tenant-bounded shares; deny precedence; explicit opt-in |
| Jobs continue after access loss | Execution-time and stage re-authorization |
| Audit purge damages evidence | Spike first; anchors; hold tests |
| Governance becomes a hard dependency | Stable contracts; optional module composition tests |

## 9. Traceability

| BRS area | Work items |
|---|---|
| Identity lifecycle | A1, A2, B1 |
| Authentication | A12 with existing session, factor and federation features |
| Tenancy and organization | ADR-01, A3, A5, B6 |
| Permission catalog | A10, A15, B2, B7 |
| Data scope and field security | A3, A4, A5, A7, A8 |
| Deny and precedence | ADR-03, A4, A8, A15 |
| Delegation and agents | A9, B1, B2 |
| Audit and evidence | ADR-05, A11, A14, B4, B5 |

## 10. Definition of done

Each item: BRS IDs in the traceability record; positive, negative, tenant and regression tests; migration notes; performance evidence for query changes; safe diagnostics and audit; formatter and analyzer clean; operator documentation; a stated list of remaining limitations.

## 11. As built

### A10 permission lifecycle (2026-10-18, P1a)

- **Contract.** `PermissionLifecycle` (`Active`, `Deprecated`, `Retired`) and `PermissionDefinition.Lifecycle` / `ReplacedBy` in `Modulus.Core`. New `IPermissionRegistry.Add(permission, description, requires, sensitivity, lifecycle, replacedBy)`. The interface default throws `NotSupportedException` so a registry that cannot express a retirement refuses instead of ignoring it; `NullPermissionRegistry` treats the call as a no-op like its other methods.
- **Startup.** Redeclaring a permission with a different sensitivity, lifecycle, replacement or prerequisite set throws. A replacement is allowed only on a deprecated or retired permission. Same-meaning redeclaration is accepted.
- **Access.**
  - `PermissionResolver`: retired permissions are removed from the effective set (after implication and deny), are never expanded by a wildcard, and confer no prerequisites.
  - `PermissionRequirementHandler`: a retired permission is refused before any store read, including through a token permission claim (`TrustPermissionClaims`).
  - Deprecated permissions keep working for existing grants.
- **Assignment.** `GrantGuards.Expand` (grants, scoped grants, access requests, break-glass profiles) refuses deprecated and retired permissions as new grant targets, and wildcards never expand to them. The refusal text is now "Not a registered, assignable permission".
- **Catalog-change notice (A10-1, 2026-10-19).** `IPermissionCatalogSnapshotStore` records the catalog at each startup; `PermissionCatalogChanges.Compare` reports lifecycle moves, replacement changes and removals since the last startup, and the startup logs each one as a warning. The EF store (`EfPermissionCatalogSnapshotStore`, table `ModulusPermissionCatalog`, global, no tenant column) is registered by `AddEfCoreAuthorizationStores`. Without a registered store no notice is produced. A new permission and a sensitivity or description change are not reported. A database failure during the check is logged as an error and does not stop startup. Upgrade: the table needs a migration (see VER-2).
- **Not done.** No automatic mapping from a retired permission to its replacement (intentional: the replacement is granted explicitly). The management-level refusal has no direct test yet (`GrantGuards` is internal to its assembly).
- **Verification.** Platform 345/345, Management 85/85, Identity 306/306, Authorization EF 51/51 on net10.0; `dotnet format --verify-no-changes` clean on the touched projects. net8.0 not run. Nothing committed.

### A4 missing-attribute denial and data-quality findings (2026-10-18, P1a)

- **Reason code.** `AccessReasonCodes.MissingAttribute` (`MISSING_ATTRIBUTE`). It is returned only when no rule grants the action and a condition failed because a record attribute was absent. An explicit deny still returns `POLICY_VIOLATION`. A denial that does not depend on a missing value keeps its ordinary code.
- **Recording.** `ResourceRequest` records a missing attribute only when the condition that read it failed: `OwnerId` (ownership and approval-separation checks), `OrgUnitId` (scope check, and only when the caller is not unrestricted), `State` (state check). A passing condition records nothing, so an OR rule that another condition satisfies still allows.
- **Finding.** `IDataQualityFindingSink` / `DataQualityFinding` (`Modulus.Core.Abstractions.DataQuality`): tenant, resource type, record id (the `Id` property when present), action and the attribute names. Never values. The default is `NullDataQualityFindingSink`, registered with `TryAdd`; an application registers its own to keep findings. The sink must not throw.
- **Unset values.** The record marker interfaces return plain `Guid`, so `Guid.Empty` for an owner or org unit, and a blank workflow state, are now treated as unset (missing), not as values.
- **Fail-open fixed.** `NotOwnedByCaller` returned true for a record with no owner, so an unowned record could be approved. It now denies with `MISSING_ATTRIBUTE`.
- **Not done.** No persistent sink (the default discards). Dimension-level missing attributes wait for A5. No API surface for reading findings.
- **Verification.** Platform 353/353 (8 new), Management 85/85, EF authorization 51/51, all on net10.0. Format clean on Core, Platform and Platform tests. net8.0 not run.

### A3 creation and resulting-scope guard (2026-10-18, P1a)

- **Contract.** `IResourceScopeGuard.CheckResultingScopeAsync(resource, action, permission?)` and the throwing `EnsureResultingScopeAsync` (`Modulus.Authorization.Resources`). It checks the record as it will be after the write, so it serves creates and updates alike: an update that moves a record to another unit is refused on the resulting record.
- **Checks, in order.**
  1. Tenant (types implementing `IHasTenantId`): the record must belong to the caller's company. The host context sees all. A caller with no company may not write a company-owned record. Refusal: `OUT_OF_SCOPE`.
  2. Org unit (types implementing `IHasOrgUnit`, non-unrestricted callers): the unit must be in the caller's data scope, the same rule as the list filter. An unset unit is `MISSING_ATTRIBUTE` and is reported as a data-quality finding. Refusal: `OUT_OF_SCOPE`.
  3. Permission scope (when a permission is passed and an `IScopeEnforcer` is registered): the record must be covered by that permission's own scope. Refusal: `OUT_OF_SCOPE`.
- **Refusals** name the type and the rule, never the record's values. The throwing form raises `ForbiddenException` (403) with the reason code.
- **Registration.** `TryAddScoped<IResourceScopeGuard, ResourceScopeGuard>()` in `AddModulusAuthorization`, next to the resource authorizer.
- **Not done.**
  - The generated CRUD create and update handlers and the import and bulk paths do not call the guard yet. Until they do, the guard is a contract, not enforced behaviour. Wiring the generator is the next step.
  - Refusals are not written to the authorization audit.
  - Dimension values (A5) are not checked.
- **Verification.** Platform 364/364 (11 new), on net10.0. Format clean on Platform and its tests. net8.0 not run.

### A3 follow-through: generated CRUD wiring (2026-10-18, P1a)

- The generated Create and Update handlers take an optional `IResourceScopeGuard` and call `EnsureResultingScopeAsync` before the save: Create with action `create`, Update with action `edit` on the changed record. Without authorization registered the guard is absent and the check is skipped (there is then no scope to enforce).
- Generated module Application projects reference `Synvro.Modulus.Platform` (the guard lives there). The check changes behaviour only for records that carry an org unit or a company marker, so existing plain CRUD is unaffected.
- Existing generated handlers are not rewritten: a module generated before this change keeps its handlers until they are regenerated.
- Not verified by building a generated app here. `Modulus.Cli.Tests` (552) covers the generated content; the handlers are not compiled in a generated project.
- Import and bulk paths remain unwired.
- Verification: CLI 552/552 on net10.0, format clean.

- **Inferred command fields (A3-1, 2026-10-19).** `CommandFields.SetBy(command)` (Platform, `Modulus.Authorization.Fields`) reads the fields a command sets from its own parameters, so the generated Create and Update handlers no longer keep a hand-written list. A parameter counts as set unless it is optional and `null`: `ExtraProperties = null` changes nothing, while a required nullable scope field (`Guid? OrgUnitId`) is checked even when it is cleared to `null`. `Id` is never a field; a parameter with no readable property counts as set. Generated output for Name and ExtraProperties is unchanged. Verification: Platform 381/381 on net10.0 (4 new tests in `CommandFieldsTests`). Not done: the rule depends on the parameter being named like the entity property it sets; a command whose parameter names differ from the entity's properties must be checked by hand.

### A4-2 finding read API (2026-10-19, P1a, built)

- **Built.** `GET /authorization/findings` (in the admin group, so `authorization:manage` applies) with optional `tenantId`, `resourceType`, `attribute`, `skip` and `take` (default 100, max 500), newest first.
  - `DataQualityFindingStore.ListAsync(DataQualityFindingQuery)` does the filtering in the database. The attribute filter matches one name exactly: the stored list is wrapped in commas, so `Currency` does not match `OrgUnitId,Currency`.
  - Scope: the host context reads every company, or the one `tenantId` names. A company's administrator reads its own findings only. Naming another company is `403`, and so is a caller with no company in scope.
- **Not audited.** Reads are not written to the audit log, the same as the other admin reads.
- **Upgrade note.** Rows written before this change stored the attribute list without the wrapping commas; they still read back, but do not match the attribute filter. The feature was not released, so no data migration is planned.
- **Verification.** EF authorization suite 79/79 on net10.0 and net8.0 (1 new in `DataQualityFindingTests`: company, resource, attribute, host-level and paging filters). Management 89/89 on both (1 new: host reads all, company reads only its own, another company refused). Build 0 warnings. Format clean on the four projects.

### A4-1 persistent finding sink (2026-10-19, P1a, built)

- **Decision applied.** Retention is 90 days (section 13), purged hourly. Options are `Authorization:DataQuality` (`DataQualityFindingOptions`: `Retention`, `PurgeInterval`, `Capacity`, `BatchSize`).
- **Built** (`Modulus.Authorization.EntityFrameworkCore/DataQuality`).
  - `DataQualityFindingQueue` is the sink. `Record` only enqueues into a bounded channel. A full queue drops the finding and counts it in `Dropped`, so a decision never waits on the database and never fails.
  - `DataQualityFindingService` (hosted) writes queued findings in batches through `DataQualityFindingStore.AddAsync`, and runs `PurgeAsync` on the interval. A failed write is logged and its batch dropped.
  - Table `ModulusDataQualityFindings`: tenant (`Guid.Empty` when none was in scope), created time, resource type, record id, action, missing attribute names (never values).
  - `AddEfCoreAuthorizationStores` replaces the discarding sink with the queue, so the registration order does not matter.
- **Not durable across a crash.** Findings still in the queue when the process stops are lost. A finding is a data-quality signal, not an audit record; the security audit is the durable trail. Say so if a stronger guarantee is needed.
- **Upgrade note.** Existing deployments need a migration that adds `ModulusDataQualityFindings`.
- **Not done.** The read API (A4-2) is the next item.
- **Verification.** EF authorization suite 78/78 on net10.0 and net8.0 (4 new in `DataQualityFindingTests`: enqueue and drop count, the database sink is the queue, written to the database, purge past retention). Management 88/88 on both. Build 0 warnings. Format clean on the EF authorization projects.

### A7-3 extension value classification (2026-10-19, P1a, built)

- **Decision applied.** The owner chose "refuse the write" for unclassified extension keys (section 13). An extension value is stored only under a key a catalog declares with its classification, so every stored extension value has a sensitivity the field-security layer can enforce.
- **Built.**
  - `IExtensionFieldCatalog` and `ExtensionFieldCatalog` (Core, `Abstractions/Entities/ExtensionFields.cs`): `Register<TEntity>(name, classification)` declares a field once (a duplicate is refused at startup); `Find(type, name)` walks base types.
  - `NullExtensionFieldCatalog` (Core, the default): declares nothing, so with no catalog registered every extension value write is refused. Fail closed.
  - `ModuleDbContext.SaveChangesAsync` checks new and changed extension keys before the write. An undeclared key throws `ValidationException` naming the key, never the value. Unchanged keys and removals pass.
- **Upgrade note.** Apps that already write `ExtraProperties` must register their declared fields in an `IExtensionFieldCatalog` before those writes succeed.
- **Not done.** The UI's extension-field registration (`ConfigureEntityUi`, in `modulus-ui`) does not yet feed this catalog; the app registers its fields directly for now.
- **Verification.** EF Core unit suite 61/61 on net10.0 and net8.0 (4 extension tests, including the refusal). Core catalog tests 10/10 on both targets (5 new in `ExtensionFieldCatalogTests`). EF and Core builds 0 warnings. Format clean on the four touched projects.

### A7-1 output safety net (2026-10-19, P1a, built)

- **Validation after redaction.** `FieldOutputGuard.EnsureMasked(authorizer, response)` walks the response the way `Redact` does (nested objects and list elements, each by its own type's mask) and refuses a field the caller may not read that still holds a non-default value. The refusal is an `InvalidOperationException` (a server error) naming the type and field, never the value, so a leak cannot ship.
- **Redacted instances cannot be saved.** `Redact` records the fields it resets on each instance (by name, in a weak table). `FieldOutputGuard.EnsureNotRedacted(entity)` refuses to save an instance whose masked fields were reset, so the reset is not silent in a write. Redaction stays for DTOs, not tracked entities.
- **Interface unchanged.** Both are static helpers over `IFieldAuthorizer.MaskFor`, so existing implementations and the audit decorator need no change.
- **Not wired into the generated handlers or endpoints yet.** Calling them is part of A7-2 (import, bulk and export paths), where the call sites are added with their checks.
- **Verification.** Platform 387/387 on net10.0 and net8.0 (6 new in `FieldOutputGuardTests`). Format clean on Platform and its tests.

### A7-2 export and bulk field checks (2026-10-19, P1a, partial)

- **Verified before changing.** The export and bulk helpers already existed (`ScopeEnforcerExtensions.ForExport`,
  `EnsureAllInScope`, `OutOfScope`), but none of them checked fields. `UserInvitationService.InviteManyAsync` and
  `ShopFloorServices.ImportAsync` are bulk writes called from trusted code with no current user, and no HTTP endpoint calls
  them yet.
- **Built (`Modulus.Authorization.Fields`, `FieldBatchExtensions`).**
  - `RedactForExport(authorizer, rows)` redacts each exported row for the caller and checks it with `FieldOutputGuard.EnsureMasked`,
    so an export carries no field the caller may not read.
  - `EnsureBatchWriteAllowedAsync(authorizer, type, rows)` checks the union of the fields of every row in one decision before any row
    is written. One row that sets a field the caller may not write refuses the whole batch with the `FIELD_PROTECTED` denial.
- **Wired (2026-10-19, owner decision: the import runs as the requesting user).**
  - `UserInvitationService` checks the fields a caller supplies (`Email`, `UserName`, `TenantId`, `FirstName`, `LastName`).
    `InviteAsync` checks one invitation; `InviteManyAsync` checks the whole batch before the first account is created.
    A refusal throws `ForbiddenException` (`FIELD_PROTECTED`), so nothing is created or mailed.
  - `ShopFloorAccountService.ImportAsync` checks the batch the same way, over `EmployeeCode`, `FirstName`, `LastName`,
    `TenantId`, `PinHash` and `MustChangePin`.
  - The check runs as the current caller (`IFieldAuthorizer` reads `ICurrentUser`). A background job with no user passes the
    public fields and is refused for any classified field, the same as a single write.
  - The authorizer is optional, as in the generated handlers: with no `IFieldAuthorizer` registered the import is unchecked.
- **Not covered.** Role assignment in invitations and imports is not a field, so the field check does not gate it; the
  import permission is still open. `ShopFloorAccountService.CreateAsync` (single account, admin path) is not wired.
- **Verification.** Identity 376/376 on net10.0 and net8.0 (3 new: two invitation refusals, one import refusal; the
  invitation and shop-floor fixtures take an optional authorizer). Identity build 0 warnings, 0 errors. Format clean on Identity and its tests.
  Platform's `FieldBatchExtensionsTests` (5) cover the batch helper itself.

### A7 field authorization (2026-10-18, P1b, partial)

- **Verified before changing (2026-10-18).** `IFieldAuthorizer` existed, with `MaskFor`, `Redact` and `AuthorizeWriteAsync`, but nothing in the framework or the generated CRUD called `Redact` or `AuthorizeWriteAsync`. The authorizer is now called by the generated handlers (below). `Redact` only reset top-level properties.
- **Deep redaction.** `Redact` walks nested objects and collection elements, masking each by its own type's mask. A visited set stops cycles. Dictionaries (the untyped `ExtraProperties` values) are not walked: they carry no classification.
- **Write refusal code.** A refused write now carries `FIELD_PROTECTED` (it was `POLICY_VIOLATION`). Unknown field names are refused (fail closed, as before).
- **Throwing form.** `EnsureWriteAllowedAsync(type, fields)` raises `ForbiddenException` naming the fields and the code, never their values.
- **Generated handlers.** Create and Update call the write check for the fields the command sets (`Name`, and `ExtraProperties` when present), with an optional `IFieldAuthorizer`. Public fields stay writable by any caller, so existing plain CRUD behaves as before.
- **Not done.**
  - `Redact` mutates the instance it is given: apply it to a DTO, never a tracked entity.
  - Import, bulk, export and reports do not call the field authorizer.
  - Extension values are not classified.
  - The write check takes the field list from the caller; it does not infer the fields a command sets, so clearing a field to null must be listed explicitly.
- **Verification.** Platform 371/371 (7 new), CLI 552/552, both on net10.0. Format clean on the touched projects. net8.0 not run.

### A5 dimensions: write-side permission scope (2026-10-18, P1a, partial)

- **Verified before changing.** Assignments are already effective-dated (`ValidFrom`/`ValidUntil`). `IScopeEnforcer.IsInScope` fails closed: no scope is false, a tenant-wide scope is true, and a record with no `ScopeMap` is false. The generated create and update handlers never passed a permission to the resulting-scope guard, so the permission's own scope (own, assigned, org unit) was not checked on writes.
- **Change.** The generated Create and Update handlers pass the endpoint's permission to the guard: `required_permission` when the host enforces one, otherwise `null` (the endpoint checks none either). An assigned or org-scoped grant therefore now also limits what the caller can create or change.
- **Not done (A5 proper).**
  - Access keys are `Guid` only (`Expression<Func<T, Guid?>>`), so typed dimensions such as a warehouse code (string) cannot be expressed.
  - No hierarchy beyond the org tree (a warehouse under a region is not modelled).
  - Dimension values are not validated on create/update against their type.
  - Compiled-predicate benchmarks are not done.
- **Caveat.** A permission held only through a token claim (`TrustPermissionClaims`) resolves to no scope and is refused on writes. Hosts keep the grant store as the source by default, so this does not affect generated apps unless claims are trusted.
- **Verification.** CLI 553/553 on net10.0; format clean. No Platform changes in this step. net8.0 not run.

### A5 typed dimensions, option 1 (2026-10-18, P1a)

- **Design.** Scope keys stay `Guid`, so the enforcer's compiled predicates and the assignment table are unchanged. A typed value (a warehouse code such as `WH-01`) is registered once per company and dimension, and resolves to a `Guid` id that assignments and record keys carry.
- **Contract (Platform, `Modulus.Authorization.Dimensions`).** `DimensionValue`, `IDimensionValueRegistry` (`RegisterAsync` is idempotent, `ResolveIdAsync`, `ListAsync`) and `DimensionKeys` (canonical dimension names: lower-case, letters, digits and hyphens; canonical codes: trimmed, upper-case, 1 to 64 characters).
- **Storage (EF).** Table `ModulusDimensionValues`, key `(TenantId, Id)`, unique `(TenantId, Dimension, Code)`, tenant query filter like the other rows. `EfDimensionValueRegistry` is registered by `AddEfCoreAuthorizationStores`.
- **Migration.** Existing deployments need a migration that creates `ModulusDimensionValues` (`modulus migrate add <Name> --module Authorization`, or `dotnet ef migrations add` for the authorization context).
- **Tests.** `DimensionValueRegistryTests` (10): idempotent registration across spellings, resolution, same code in two dimensions, company isolation both ways, ordered listing, label default, invalid names and codes. EF authorization 61/61 and Management 85/85 on net10.0. Format clean.
- **Admin endpoints (A5-1, 2026-10-19).** `GET dimensions/{dimension}` lists a company's values; `POST dimensions/{dimension}` registers a code (`{ code, label }`; registering an existing code returns it unchanged and audits `Dimension.Registered`); `POST assignments/by-code` links a user to a value by its code (404 for an unknown code, 400 for a bad dimension or code). All sit under `authorization:manage`. The assignment-create body moved to a shared helper, so both routes apply the same checks. Without `AddEfCoreAuthorizationStores` the dimension routes answer 501. Verification: Management 87/87 on net10.0 and net8.0; format clean.
- **Concurrent registration (A5-4, 2026-10-19).** `EfDimensionValueRegistry.RegisterAsync` catches the unique-index failure of a racing insert, reads the winning row from a fresh context and returns it, so the code keeps one id and the loser gets the winner's label. A failure that is not the race (no winner row found) is rethrown. Test: `DimensionRegistryRaceTests` (a rival writes the winning row just before the loser saves; the test fails with the catch disabled). EF authorization 65/65 on net10.0 and net8.0; format clean.
- **Not done.**
  - No dimension hierarchy (a warehouse under a region).
  - Record dimension values are not checked against the registry on create and update.
  - Compiled-predicate benchmarks.
  - net8.0 not run.

### A5-3 record dimension validation (2026-10-19, P1a, built)

- **Declaration (`Modulus.Core.Abstractions.Dimensions.DimensionAttribute`).** `[Dimension("warehouse")]` on a `Guid` or `Guid?`
  property holds the id of a value of that dimension. The entity references Core only, so the attribute lives there.
  Decision: the property carries the id the registry returns (what scope assignments and access keys compare), not the code.
- **Check (`DimensionRecordValidation.EnsureRecordDimensionsAsync`).** For each marked property with a value, the id must be
  registered for the current company in that dimension, or the write is refused with a `ValidationException` naming the
  property. A null value is not checked. A marked property that is not a `Guid` is a programming error (`InvalidOperationException`).
  The earlier code-based `RequireRegisteredAsync` stays for inputs that arrive as codes.
- **Wiring.** The generated Create and Update handlers take an optional `IDimensionValueRegistry` and call the check before the
  scope guard and the save. The sample entity has no dimension, so generated output changes only in the new parameter. Generated
  commands do not yet produce dimension properties: a module adds `[Dimension]` to its entity and the id to its command by hand.
- **Not done.** No generator option that adds a dimension property to `generate-crud`. Existing generated apps need their handlers
  regenerated (or the parameter added) before the check runs.
- **Verification.** `DimensionRecordValidationTests`: 10/10 on net10.0 and net8.0 (5 existing, 5 new: registered ids pass, an unset
  optional is skipped, an unregistered id and an id from another dimension are refused naming the property, a non-Guid marker is an
  error). Build 0 warnings for Core and Platform (PublicAPI entries added).

### A5-2 dimension hierarchy (2026-10-19, P1a)

- **Design.** A value gets an optional parent (`DimensionValue.ParentId`), in any dimension, so a region can hold warehouses. An
  assignment to a value covers the values placed beneath it: `ScopeSubject.AssignedTargets` expands the stored targets through
  `IDimensionHierarchy.Expand` once per assignment type per request, so the compiled predicates and the assignment table are unchanged.
  The expansion is synchronous, like the assignment store it sits beside.
- **Placement.** `IDimensionValueRegistry.SetParentAsync(dimension, code, parentDimension, parentCode)` places a value (both parent
  arguments null moves it to the top). It refuses a value under itself (`ArgumentException`), a placement that would close a loop
  (walks up from the parent, bounded at 64 levels), and a parent not registered in the company (`NotFoundException`).
- **Storage (EF).** Column `ParentId` on `ModulusDimensionValues`, indexed on `(TenantId, ParentId)`. The tenant filter keeps a
  company's placements to itself. `EfDimensionHierarchy` reads the children one level at a time and stops at 10,000 values; the rest
  are not granted, so a truncated expansion narrows access. It replaces the framework's `NullDimensionHierarchy` whatever the
  registration order.
- **Admin.** `PUT dimensions/{dimension}/{code}/parent` (`authorization:manage`), body `{ parentDimension, parentCode }`. Unknown values
  are 404, a loop or a self-placement is 400, and the change is audited as `Dimension.ParentSet`.
- **Upgrade note.** Existing deployments need a migration that adds `ParentId` (nullable) and its index to `ModulusDimensionValues`
  (`dotnet ef migrations add` for the authorization context, or `modulus migrate add <Name> --module Authorization`).
- **Not done.**
  - No depth or placement rules beyond loops (a region under a warehouse is allowed).
  - Benchmarks of the expansion on large trees (A5-5).
  - The generated CRUD does not declare dimensions yet (see A5-3).
- **Verification.** Platform 403/403 on net10.0 and net8.0 (3 new in `DimensionScopeSubjectTests`: the expansion through the subject,
  once per type, and the unchanged targets without a hierarchy). EF authorization 74/74 on net10.0 and net8.0 (9 new in
  `DimensionHierarchyTests`: multi-level, loops, self, another company, moving to the top). Management 88/88 on net10.0 and net8.0 (1 new:
  placement route, loop 400, unknown parent 404). Build 0 warnings; format clean on the touched projects.

### A4-3 dimension missing attributes (2026-10-19, P1a, partial)

- **Built (`ResourceScopeGuard`).** When a write's resulting record lies outside the caller's scope and the permission is
  assigned-scoped, a record whose assignment key for that type is unset is denied with `MISSING_ATTRIBUTE` and a finding that
  names the key (for example `warehouse`), the same as a missing org unit. A key the map does not declare is not reported. A
  record that has the key but is outside the scope stays `OUT_OF_SCOPE` with no finding. The guard takes the map registry as an
  optional constructor argument; without one, the old `OUT_OF_SCOPE` behaviour is unchanged.
- **Not done.** The finding sink is still the discarding default (`NullDataQualityFindingSink`). A4-1 (the persistent sink) makes
  these findings durable. Read-side checks (a list or a single record with a missing key) are not changed.
- **Verification.** Platform 400/400 on net10.0 and net8.0 (3 new in `ResourceScopeGuardTests`). Build 0 warnings. Format clean on
  Platform and its tests.

### A12-1 absolute session lifetime (2026-10-19, P1a, built)

- **Verified before changing.** Sessions had only an idle timeout (`IdleTimeoutMinutes`, per role). A session that was used
  regularly never ended, because each request reset its last-seen time. The cookie sign-in (`ConfigureApplicationCookie`) and the
  BFF cookie (`SlidingExpiration = true`, 8 hours) are sliding too, so neither is capped by this change.
- **Built.** `Identity:Sessions:MaxLifetimeMinutes` (0 = none) and `MaxLifetimeMinutesByRole`, measured from sign-in and
  independent of activity. The strictest role limit that applies wins, as for the idle timeout. The check runs in the same place
  as the idle check (touch, list, token validation), before it, so a busy session still ends. `SessionState.LifetimeExpired` and
  the reason `max-lifetime` are new; the token rejection says "reached its maximum lifetime". Refresh cannot extend it, because the
  refresh continues the same session.
- **Scope.** Applies to sessions of the registry (`Identity:Sessions:Enabled`), like the idle timeout.
- **Identity cookie (follow-up, same day).** The authorize step turns the Identity cookie into a code, so a cookie that slides
  forever could mint a new session without a password. `OnSigningIn` now stamps `modulus_signed_in_at` (a cookie-only claim,
  set once; sliding renewals keep it). `/connect/authorize` sends the person to sign in again when the cookie is older than the
  lifetime for its roles, and when a cookie has no stamp (issued before this change). `max_age` and `auth_time` now measure
  from that first sign-in instead of the last sliding renewal.
- **BFF web cookie (follow-up, same day).** `Modulus.Bff` stamps `modulus_signed_in_at` in the web cookie's `OnSigningIn` (both
  the password sign-in and the OIDC callback sign in through that scheme). `OnValidatePrincipal` ends the session `SessionLifetime`
  after the sign-in, even while it is in use. A session without the stamp is ended (fail closed). `SessionLifetime` (default 8 hours)
  is now both the idle window (sliding) and the absolute cap, so an active browser session ends at the cap; no new setting.
  **Upgrade note:** web sessions open before this change have no stamp and end at their next request, so their users sign in again once.
- **Verification.** Identity 373/373 on net10.0 and net8.0 (one cookie test added to `SessionLifetimeHttpTests`). Bff 84/84 on
  net10.0 (net8.0 84 passed, 1 skipped as before) with `A_web_session_ends_its_lifetime_after_sign_in_even_while_it_is_in_use`
  (a `BffTestHost` clock). Format printed nothing on the Identity and Bff projects and their tests. Not tested directly: the
  stamp's survival across a real sliding renewal (the test clock does not renew the cookie).

### A1 / A2 identity: inventory and lifecycle rule (2026-10-18, P1a, partial)

**Inventory (verified in source, no schema change made).**

| Item | Today | Gap against ADR-01 / ADR-02 |
|---|---|---|
| Account state | `ModulusUser.IsActive` (bool, default true). Invited users are also `IsActive`. | No lifecycle states (Invited, PendingActivation, Disabled, Archived). |
| Suspension | None separate. Disabling is `IsActive = false`, with no reason, actor or period. | No restriction dimension. |
| Lockout | ASP.NET lockout (`LockoutEnd`, `AccessFailedCount`), kept separate. | Matches ADR-02. |
| Transition history | None. The security audit records some events (`account.disabled`, `account.locked`) through observers. | No append-only lifecycle history. |
| Session end on disable | `ModulusUserManager.UpdateAsync` notifies `IAccessChangeObserver` (`account.disabled`). | Sessions and tokens are ended through `IUserSessionService`, not as part of the transition. |
| Tenant on the account | `ModulusUser.TenantId` (one home company). Memberships are separate (`ModulusTenantMemberships`). | The account row is still tenant-local: ADR-01's canonical identity is not in place. |
| Principal kind | Humans are `ModulusUser`. Machines are OpenIddict client applications (`integration:` roles). | No `PrincipalKind` (Employee, Contractor, PartnerUser, ServiceAccount, SystemJob). |
| Identifier reuse | Usernames and emails are unique in the identity tables. | No immutable identity-linking key. |

**Done in this step.** `AccountLifecycle` (`Invited`, `PendingActivation`, `Active`, `Disabled`, `Archived`) and `AccountLifecycleRules` (`Modulus.Identity.Abstractions`): the legal-transition matrix, refusal with a 409 that says why, the rehire review for an archived account, and `AllowsNewAccess` (active only). It is pure logic. Persistence still uses `IsActive`, and nothing calls the rule yet. 24 tests in `AccountLifecycleTests`; Identity 329/329 on net10.0.

**Migration design (not built; needs review before a schema change).**
1. Add `Lifecycle` (string, max 16, default `Active`) and `LifecycleChangedAt` to `ModulusUsers`.
2. Add `SuspendedReason`, `SuspendedBy`, `SuspendedUntil` for the restriction dimension (ADR-02 restriction, separate from lifecycle).
3. Add an append-only `ModulusAccountTransitions` table: user, from, to, actor, reason or reference, time, and the session action taken. Insert-only in the application, with the security audit chain receiving each row.
4. Backfill: `IsActive = true` becomes `Active`, `false` becomes `Disabled`. Existing invited accounts stay `Active` until they are reviewed, which is a known limitation.
5. Keep `IsActive` as a derived compatibility flag (`Lifecycle == Active`) for one release, then remove the direct writes.
6. Upgrade note: existing deployments need a migration (`modulus migrate add <Name> --module Identity`) and a data check of the backfill.
7. ADR-01 (canonical identity and memberships) is a larger, separate migration: it would split the account row from tenant-owned attributes and preserve account ids. It is not started.

**Decisions needed before step 1 (owner input).** The principal kinds that ship in the first release; whether invited accounts start as `Invited` or stay `Active`; the reason codes for suspension; and whether ADR-01 is in scope for P1a or moved to P1c.

### Decisions chosen (2026-10-18, owner input delegated)

1. **Principal kinds (first release):** `Employee`, `Contractor`, `PartnerUser`, `ServiceAccount` (`PrincipalKind`, `Modulus.Identity.Abstractions`). A system job is not a stored principal; it runs under a named identity in the job envelope.
2. **Invited accounts:** new invitations start as `Invited`. Existing accounts are backfilled to `Active`, so behaviour is unchanged for them.
3. **Suspension reasons:** `SecurityReview`, `PolicyViolation`, `LeaveOfAbsence`, `AdministrativeHold`, `Other` (`SuspensionReason`). `AdministrativeHold` and `Other` require a free-text note.
4. **ADR-01 (canonical identity and memberships):** moved to P1c, as a separate migration with its own review.

**Schema change (as built, 2026-10-18).** `ModulusUser` gains `PrincipalKind` (string, default `person`), `Lifecycle` (`AccountLifecycle` stored as a string, default `Active`), `LifecycleChangedAt`, `SuspensionReason` (max 64) and `SuspensionNote` (max 512). The defaults are EF `HasDefaultValue`, so an existing row reads back as an active person with no suspension. `ModulusAccountTransition` (table `ModulusAccountTransitions`, `FromState`/`ToState`, `ActorId`, `Reason`, `OccurredAt`) is the append-only history: no foreign key to the user, so it outlives the account, indexed on `(UserId, Id)`. `IsActive` stays for one release; nothing reads the new columns yet.
- **Migration.** The framework ships no Identity migrations; each app writes its own. An existing app runs `modulus migrate add <Name> --module Identity`, then `modulus migrate update`. The generated `AddColumn` calls carry `defaultValue: "Active"` and `defaultValue: "person"`, so existing rows take the defaults.
- **Verification.** `LifecycleSchemaTests` (3, both targets): a new account round-trips kind, lifecycle, suspension and its change time; a row written without the new columns reads back as an active person; transitions read back in order. Identity 346/346 on net10.0 and net8.0; format clean. A generated diff migration (scratch project, outside the repo) contained only the five `AddColumn` calls and the transition table, with the defaults above.
- **Lifecycle wired into the user manager (as built, 2026-10-18).** `ModulusUserManager` resolves every create and update through `LifecycleReconciliation` (internal, `Abstractions/AccountLifecycle.cs`). A lifecycle the caller changed is authoritative and must be a legal transition (`AccountLifecycleRules`); an illegal change fails with `IllegalLifecycleTransition` and nothing is saved. An unchanged lifecycle lets the legacy `IsActive` flag move the account only between `Active` and `Disabled`, so SCIM and existing admin callers keep working; an `Invited`, `PendingActivation` or `Archived` account cannot be reactivated through the flag. `IsActive` is always saved equal to `AllowsNewAccess(Lifecycle)`, so sign-in (which reads the flag) follows the lifecycle.
- **Transitions recorded.** Each lifecycle change adds an `IAccountTransitionStore` row (EF: `EfAccountTransitionStore<TUser>`, registered over the Identity context) in the same save as the account. Actor is the signed-in caller when there is one; the reason is the suspension reason for a disable. Creation is not a transition.
- **Invitations and activation.** An invitation creates the account `Invited` (not active). Sending the link moves it to `PendingActivation`. Accepting the link (`accept-invitation`) moves it to `Active` through the manager, so the invitee can sign in from then on. Rehire (`Archived` to `PendingActivation`) still has no path; it needs the reviewed-rehire flow.
- **Enum values.** `Active` is `0`, so the CLR default matches the database default. With `Invited` as `0`, EF omitted the value on insert when `HasDefaultValue` applied, and invitations were stored as `Active`. The test `Inviting_then_activating_records_each_step_and_keeps_the_flag_in_step` caught it. The stored strings are unchanged, so the schema migration is unaffected.
- **Verification.** `LifecycleEnforcementTests` (truth table for the rule, invitation-to-active with its two transitions, legacy disable recording its reason, refused archive reactivation leaving the stored state and the history unchanged). Identity 361/361 on net10.0 and net8.0; Management 85/85; Platform 371/371; format clean.
- **Explicit lifecycle API (as built, 2026-10-18).** `IAccountLifecycleService.ChangeAsync(AccountLifecycleChange)` with `AccountLifecycleChange(UserId, To, ActorId, Reason, Note, RehireReviewed)`, registered by `AddModulusIdentity`. The actor is the one passed in (null for the system), not the ambient user, so background jobs and administrators acting for others record correctly. It refuses an illegal move with `ConflictException` (nothing changed), an unknown account with `NotFoundException`, and a disable with no reason or without the note a reason requires (`ArgumentException`). Reinstating clears the suspension. Every change writes a transition row with the actor and reason, and an `account.lifecycle_changed` security audit event (from, to, reason; no personal data). The service passes the change to the user manager through a scoped value (`AccountLifecycleScope`), so the transition is still written in one place.
- **Not exposed over HTTP.** Like the invitation service, the lifecycle API is a service for the application's own endpoints or pages. An HTTP surface needs a permission and step-up decision that belongs with the application's admin design.
- **Still to do for A1/A2.** Retiring `IsActive` in the next release, and an application-facing admin surface for lifecycle. (The reviewed-rehire approval flow is built, see A1-1.)

### A5-5 scope predicate benchmarks (2026-10-19, P1a, built)

- **Built.** `tests/benchmarks/Modulus.Benchmarks/ScopePredicateBenchmarks.cs` (BenchmarkDotNet, Release). It measures the compiled
  scope map two ways: `CompileOnFirstUse` (build and compile a map, evaluate one record) and `EvaluateAll` (a warm map over every
  record, the per-record cost every list, count and record check pays). Parameters: 1,000 and 100,000 records; 0, 1 or 3 assignment
  types beyond owner and org unit. A tenth of the key values are unset (seeded data).
- **Results** (`--job short`, Mean, from BenchmarkDotNet 0.x on this machine; the run is noisy, so read the orders of magnitude):

  | Records | Assignment types | Compile + one record | EvaluateAll (warm) | Per record (warm) |
  |---|---|---|---|---|
  | 1,000 | 0 | 63 µs | 27 µs | ~27 ns |
  | 1,000 | 1 | 127 µs | 83 µs | ~83 ns |
  | 1,000 | 3 | 183 µs | 123 µs | ~123 ns |
  | 100,000 | 0 | 63 µs | 4.2 ms | ~42 ns |
  | 100,000 | 1 | 97 µs | 9.6 ms | ~96 ns |
  | 100,000 | 3 | 209 µs | 15.7 ms | ~157 ns |

- **Reading.** Compiling a map costs well under a millisecond and does not grow with the record count, so a map is cheap to rebuild on
  first use. The warm cost is linear in records and grows with each assignment type (about 40 ns per record per extra type at
  100,000). Allocation is about 226 bytes per record with no assignment types, and it scales with the number of types.
- **Gate.** This closes the P1a benchmark item. It does not decide anything for P1b: the numbers show the predicate is fast enough
  for the list and count paths, but a bulk export of 100,000 rows still costs about 16 ms in predicate evaluation alone.
- **Not done.** No benchmark of the dimension hierarchy expansion on large trees (the "Not done" item of A5-3 stays open). The
  `--job short` setting was chosen to finish in reasonable time; a longer job would tighten the intervals.

### A12-2 breached-password check (2026-10-19, P1a, built)

- **Decision used (owner delegated, recommended option).** An offline list. Nothing is sent to an external service.
- **Settings.** `Identity:BreachedPasswords`: `Enabled` (default **off**, so existing apps are unchanged until they opt in) and `ListPath`. When on, a missing `ListPath` stops the host at startup (`ValidateOnStart`).
- **List format.** The public Pwned Passwords range format: one SHA-1 hash per line in hex, optionally `:count`. Lines that are not a hash are skipped. The list is read once and kept in memory. SHA-1 is used only to match the public list, not to store passwords.
- **Where it runs.** `BreachedPasswordValidator<TUser>` is an Identity `IPasswordValidator`, so creation, change, reset and invitation all check the list. The refusal code is `BreachedPassword`, and the message never repeats the password.
- **Not done.** No tool to build the list from a download; the operator supplies the file. A test for the startup refusal on a missing file is not written (the options validation runs only when a host starts).
- **Verification.** Identity 388/388 on net10.0 and net8.0 (3 new in `BreachedPasswordTests`: list parsing, refusal on create and change, check off). Build 0 warnings, 0 errors. Format clean on Identity and its tests.

### A6-1 job initiator re-check (2026-10-19, P1b, built; sensitive stages not yet)

- **Verified first.** The job envelope (`ChannelJobQueue`'s `JobEnvelope` and Quartz's `JobDataMap`) carried the tenant and correlation id but no person, so a queued job ran with no identity at all.
- **Decision used (owner delegated, recommended option).** The initiator is the signed-in person who queued the job. A check that they may still start access runs when the job comes up, and a refused job is skipped and logged, not retried. Recurring jobs carry no initiator and run as the system (this is the A6-2 identity, not yet named).
- **Contract.** `IJobInitiatorGuard.MayRunAsync(initiatorId, ct)` (Platform). The default allows every job, so an application without Identity behaves as before. `AddModulusIdentity` replaces it with `UserJobInitiatorGuard`, which allows only an account that `MayStartNewAccess()` (the A1-2 rule).
- **Both schedulers.** The in-memory queue (`ChannelJobQueue`) and Quartz (`QuartzJobAdapter`) capture and check the initiator.
- **Refusals are audited (2026-10-19, follow-up).** A refused job writes a security audit event: category `authorization`, action `job.refused`, outcome `denied`, actor the queuing person (`person:{id}`), target the job type, company the job's tenant. Both schedulers do this (`ChannelJobQueue` and `QuartzJobAdapter`). There is no jobs category yet, so the authorization category is used.
- **Quartz refusal path is tested (2026-10-19, follow-up).** `QuartzRefusalTests` starts the scheduler and runs a real queued job: a refused initiator's job does not run and is audited; an allowed one runs (control).
- **Not done.** Re-checking the permission at each sensitive stage inside a job (the job has no declared permission yet); the initiator's own permissions are not restored on the job's scope, so handlers still run with no user. The recurring path is not tested in the in-memory queue, since its scheduler fires every 30 seconds (the Quartz recurring path is tested).
- **Verification.** `JobInitiatorTests` (4: refused job skipped, allowed job runs, no person means no check, refusal audited with the person), `QuartzInitiatorTests` (2: initiator carried on enqueue, none on recurring), `QuartzRefusalTests` (2: refused job not run and audited; allowed job runs), `LifecycleEnforcementTests.A_queued_job_stops_running_once_its_initiator_is_disabled`. Platform 421/421, Quartz 7/7, Identity 395/395 on net8.0 and net10.0. Format clean on the changed projects.

### A9-1 agent principal: the access cap (2026-10-19, P1b, partly built)

- **Verified first.** `ISecurityContext.Agent` (documented as "the intersection of the user's and its own" rights) was always null, and nothing issued or read an `act` claim. There was no agent identity and no token exchange.
- **Decision used (owner chose).** An agent is an integration client acting for a signed-in person, as in RFC 8693: the token's `sub` is the person and its `act` claim names the agent's role (`integration:{clientId}` in Identity). Effective access is the person's access intersected with what the agent role holds.
- **Built.** The cap is enforced where every decision path resolves: `PrincipalGrantQuery.AgentRole` (new optional parameter). `PermissionResolver` intersects with the agent role's grants (deny-wins), and `DelegationAwarePermissionResolver` re-caps after it unions in delegated permissions. The HTTP policy (`PermissionRequirementHandler`) and `ICurrentUser` (`GrantStorePermissionChecker`) both read `act`. A permission claim is never trusted for an agent token. Without the grant store, `ClaimsPrincipalCurrentUser` grants nothing to an agent token. A blank `act` grants nothing.
- **Not built.** The token exchange endpoint: nothing issues an `act` token yet, so the cap has no live caller until it does. `ISecurityContext.Agent` is still null. The agent is not on the audit trail. Background jobs and the AI connector do not apply the cap, because they do not resolve a principal from a token.
- **Verification.** `AgentBoundPermissionTests` (6: the person acting directly, capped through an agent, the grants overload, a deny on the agent role, a delegated permission not escaping the cap, and a delegated permission for a person acting directly), `GrantStorePermissionCheckerTests` (4 new: the cap, blank agent, permission claim not trusted without the store, person claims still honoured). Platform 420/420, Identity 395/395, EF authorization 82/82, Management 89/89, on net8.0 and net10.0. Format clean.

### A9-2 delegation bound on the delegator's liveness (2026-10-19, P1b, built)

- **Verified first.** A delegation was capped by the delegator's direct grants and a role snapshot, but nothing checked that the delegator could still act. A disabled, locked-out or deleted delegator kept their delegations. A deleted delegator also lost only their roles and kept direct grants as the cap.
- **Decision used (owner delegated, recommended option).** The delegation resolver stays synchronous, so the store is the decision-time gate. `DelegationRoleRefresher` ends (revokes) every delegation whose delegator may not start access. This runs on each access change through `IAccessChangeObserver`, and on the timer for a change that was not reported. Ending is chosen over suspending: a re-enabled delegator must delegate again rather than have old authority return.
- **Contract.** `IUserRoleDirectory.MayStartAccessAsync(userId)` (Core). The default refuses, so a directory that cannot answer ends delegations instead of keeping them. Identity implements it as an enabled account (`MayStartNewAccess`); an account the store does not know is refused.
- **Not done.** The role snapshot is still up to one timer interval stale for a role change that was not reported; `P1b-GATE` revocation latency measures it. The delegate's own access is not re-checked against this rule. A delegation is not re-validated at each use, only when its delegator changes or on the timer.
- **Verification.** `DelegationRoleRefreshTests` (4 new or changed: unknown delegator loses the delegation, disabled delegator loses it on refresh and at once on an access change, active delegator keeps it), `LifecycleEnforcementTests.The_role_directory_says_a_disabled_or_unknown_account_may_not_start_access`. EF authorization 82/82, Management 89/89, Identity 390/390 (plus the new test), on net8.0 and net10.0. Build 0 warnings. Format clean.

### A6-2 named system actor for jobs (2026-10-19, P1b, built; attribution only)

- **Verified first.** Recurring jobs and ad hoc jobs queued with no person carried no name, so a job that ran as the system left no trace of who it ran as.
- **Decision used (owner delegated, recommended option).** The actor is a name carried with the job, not a stored principal. `JobActor.ForPerson(id)` (`person:{N}`) when a signed-in person queued the job; otherwise `JobActor.ForSystem(name)` (`system:{name}`), where the name is the recurring job id, or the job type for an ad hoc job.
- **Contract.** `JobContext.Actor` (scoped, Platform) is set by the worker before the job starts, and every log line of the job carries a `JobActor` scope key. Both schedulers do this: `ChannelJobQueue` (`JobEnvelope.Actor`) and Quartz (`actor` in the `JobDataMap`, read by `QuartzJobAdapter`).
- **Not done.** No permission is granted to a system actor, so a system job still has no declared authority (that is the sensitive-stage re-check in A6-1). The actor is not yet on the audit trail for business or security events a job writes. The `system:` name is not validated against a registry of known jobs.
- **Verification.** `JobActorTests` (3: person actor, system actor by job type, blank name refused), `QuartzInitiatorTests` extended (actor on enqueue, `system:` on recurring). Platform 414/414, Quartz 5/5 on net8.0 and net10.0. Format clean.

### A1-2 sign-in and SCIM read the lifecycle (2026-10-19, P1a, built; flag removal next release)

- **Decision used (owner delegated, recommended option).** The removal of the `IsActive` column waits for the release after this one, because the lifecycle work has not shipped yet and apps that disable accounts through a plain `UserManager` still rely on the flag. This step makes every read follow the lifecycle now.
- **Rule.** `MayStartNewAccess()` (internal, `Abstractions/AccountAccess.cs`) is `IsActive && Lifecycle == Active`. Both must allow it: the flag can only restrict, so an account disabled through a manager that is not `ModulusUserManager` (which keeps the two in step) still stops, and a flag can never reactivate one.
- **Readers moved.** Password grant, token refresh and revalidation, the cookie login and federated/external sign-in, passkeys, shop-floor PIN sign-in, the password-reset mail queue, the SCIM `active` attribute (inlined in the filter getter, because it is translated to SQL) and the SCIM read paths.
- **Writers kept.** `ModulusUserManager` still resolves the flag through `LifecycleReconciliation` (so SCIM and old callers keep working), and the create paths (invitation, federated, shop-floor) still set the flag next to the lifecycle they need.
- **Known edge.** SCIM can add an operator seat for an invited (not yet active) account when it sets `active: true`. The lifecycle then keeps it invited, so it cannot sign in, but the seat is counted. Not changed here.
- **Not done.** Dropping the `IsActive` column and its migration (next release, with VER-4's upgrade note).
- **Verification.** `LifecycleEnforcementTests.Sign_in_refuses_a_disabled_lifecycle_even_when_the_flag_says_active` (new: a row with lifecycle Disabled and the flag still true is refused, from a fresh scope). Identity 389/389 on net10.0 and net8.0. Build 0 warnings. Format clean on Identity and its tests.

### A1-3 lifecycle admin surface (2026-10-19, P1a, built)

- **Decision used (owner delegated, recommended option).** Permission `identity:accounts:manage`, registered Critical by `AddModulusAccountLifecycleAdmin()`, so step-up applies when `StepUp.RequireForCritical` is on.
- **Routes** (`MapModulusAccountLifecycle()`, default prefix `/identity/accounts`, guarded by the permission):
  - `PUT {id}/lifecycle` with `{ to, reason, note }`: the actor is the caller. Illegal moves are `409`, a suspension without a reason or note is a `400` validation problem.
  - `POST {id}/rehire` with `{ reason }`: the caller approves through `IRehireReviewService` (A1-1), which applies the company and role checks.
- **Not done.** No read route for the state or history (the lifecycle service has no read method yet). The HTTP tests cover the refusals only; the happy path is covered by `AccountLifecycleServiceTests` and `RehireReviewTests`.
- **Verification.** Identity 385/385 on net10.0 and net8.0 (3 new in `AccountLifecycleHttpTests`: anonymous refused on both routes, password-only sign-in stepped up with `insufficient_user_authentication` and nothing changed). Build 0 warnings, 0 errors. Format clean on Identity and its tests.

### A1-1 reviewed rehire (2026-10-19, P1a, built)

- **Decision used (owner delegated, recommended option).** Approvers are holders of `TenantAdministrator` or `HR` in the account's company. Names are configurable: `Identity:RehireReview:ApproverRoles`.
- **Flow.** `IRehireReviewService.ApproveAsync(accountId, approverId, reason)` is the single approval step. An archived account is the request, so no separate request row is stored.
- **Refusals.** A non-archived account (`Conflict`); an approver who is the account; an approver in another company or with no approver role (`Forbidden`, nothing changes); an empty reason (`ArgumentException`).
- **Effect.** Moves the account to `PendingActivation` through `IAccountLifecycleService` with `RehireReviewed: true`, so the transition row records the approver and reason, and the account must still accept its link. Audits `account.rehire_approved`.
- **Not done.** No notification to approvers. The approval is also reachable over HTTP (A1-3).
- **Verification.** Identity 382/382 on net10.0 and net8.0 (6 new in `RehireReviewTests`). Build 0 warnings, 0 errors. Format clean.
- **Verification.** `AccountLifecycleServiceTests` (9): explicit actor, reason and audit on disable; required reason and note refused; reinstatement clears the suspension; illegal move is a conflict with nothing changed; archived accounts return only through a reviewed rehire; no-op for the current state; unknown account; system change with no actor. Identity 370/370 on net10.0 and net8.0; format clean.

### Framework made generic (2026-10-18)

- **Principal kinds and suspension reasons are open vocabularies.** `PrincipalKind` and `SuspensionReason` enums are replaced by `PrincipalKinds` (framework names `person` and `service-account`; `Normalize` enforces a plain lower-case name) and `SuspensionReasons` (framework names `security-review`, `policy-violation`, `absence`, `administrative-hold`, `other`; `RequiresNote` for the two open codes). An application adds its own kinds and reasons as data. The enums were never persisted, so nothing needs a data migration for them.
- **Industry org kinds are obsolete.** `OrgUnitKinds.Factory` and `OrgUnitKinds.Warehouse` are marked `[Obsolete]`; no framework code uses them. Kinds are application data.
- **ERP wording removed from framework docs and messages.** Party links, the management kind message, the scope-map comment and the assignment docs now describe application-defined kinds, not buyers, suppliers or subcontractors. The framework logic was already string-keyed; the change is names and documentation.
- **Schema.** The lifecycle, kind and suspension columns on `ModulusUser` are `string`-backed, as described in the schema change below.
- **Verification.** Identity 343/343, Management 85/85, EF authorization 61/61, all on net10.0; format clean on the changed projects.

## 12. Remaining work (backlog, 2026-10-18)

Everything not yet built, grouped by release gate. Status comes from the as-built notes in section 11. Items marked *verify* were not re-checked against the code when this list was written. Items marked *decision* need an owner choice first (section 13).

### 12.1 P1a: current gate

| ID | Item | Remaining work | Depends on |
|---|---|---|---|
| A5-5 | Benchmarks | Compiled predicate cost on representative data (a P1b gate) | A5-2 |
| A5-5 | Benchmarks | Done (2026-10-19): results recorded in the A5-5 section | done |
| A1-2 | Retire `IsActive` | Done (2026-10-19): sign-in and SCIM read the lifecycle; the column is removed in the next release | done (column removal: next release) |
| A2-1 | Canonical identity (ADR-01) | Moved to P1c by the 2026-10-18 decision (section 13), so not in the P1a gate | P1c |

Done in P1a (2026-10-19): A1-1 reviewed rehire, A1-2 sign-in and SCIM read the lifecycle, A1-3 lifecycle admin surface, A12-2 breached-password check, A7-2 import and invitation wiring.

### 12.2 P1b: after the P1a gate

| ID | Item | Remaining work |
|---|---|---|
| A6-1 | Job principal re-check | Done (2026-10-19) for the initiator at execution, and refusals are audited (`job.refused`); the per-stage re-check inside sensitive jobs is still open |
| A6-2 | `SystemJob` identity | Done (2026-10-19) for attribution: every job carries a named actor (`person:{id}` or `system:{name}`); per-job permissions are still open (see A6-1) |
| A8-1 | Record sharing | Tenant-bounded shares with expiry, revocation and masked references. Not started. Decision: first release |
| A9-1 | Agent principal | Partly done (2026-10-19): the access cap is enforced on the decision paths. Token issuance (token exchange), the agent on `ISecurityContext` and the job and AI paths are not built. See the A9-1 section |
| A9-2 | Delegation bound | Done (2026-10-19) for delegator liveness: a delegation ends when its delegator may no longer start access. Role snapshots still refresh on a timer and on access change, so role staleness is bounded by that interval, not zero |
| A7-4 | Nested and alternate output tests | Leakage tests across nested DTOs, lists, exports and alternate outputs |
| P1b-GATE | Revocation latency | Measured propagation time for role, grant and session changes |
| P1b-GATE | Job and agent tests | Conformance and probe tests for jobs and agents |

### 12.3 P1c: hardening and pilot

| ID | Item | Remaining work |
|---|---|---|
| A11-1 | Audit retention | Retention periods, archive, holds, and purge that keeps the hash chain verifiable. Spike first. Decision: durations |
| A11-2 | Audit archive and restore | Integrity verified across an archive and a restore |
| A14, A15 | Defined in the BRS, not in this file | Scope to be confirmed before work starts (*verify*) |
| ADR-03 | Deny precedence | Confirm and test deny precedence across grants, sharing and delegation |
| ADR-05 | Audit model | Confirm the audit event model against the evidence requirements |
| B1 to B7 | Defined in the BRS, not in this file | Scope to be confirmed before work starts (*verify*) |
| PILOT-1 | Pilot module contract | A pilot module that exercises tenant isolation and deny-by-default end to end |
| PILOT-2 | Migration rehearsal | Migration run on a populated prior-version schema, with a tested rollback |
| PILOT-3 | Performance | Measured on representative data for each query change |
| PILOT-4 | Runbooks | Operator runbooks for lifecycle changes, revocation, audit restore and key rotation |
| TEST-1 | Conformance suite | `Modulus.Testing.Authorization`: one fixture per truth-table row, two tenants, org nodes, missing attributes, active and expired grants, delegation and agent contexts |
| TEST-2 | Probe coverage | `SecurityProbeSuite` extended to read, list, search, count, aggregate, export, create, update, classified fields, bulk, imports, related lookups, jobs and agents |
| TEST-3 | Concurrency tests | Role and grant changes, revocation, session termination and cache invalidation, with measured staleness |

### 12.4 Verification and migration debt

| ID | Item |
|---|---|
| VER-1 | Run net8.0 for: the dimension registry and EF store, the A3 guard, A7 field authorization, lifecycle and transition changes, and `IAccountLifecycleService` |
| VER-2 | Migrations for existing deployments: `ModulusDimensionValues` and `ModulusPermissionCatalog` (authorization context); lifecycle columns and `ModulusAccountTransitions` (identity context). Document the commands in the release notes |
| VER-3 | Generated app compile check after the CRUD template changes (A3, A7) and the Identity lifecycle change |
| VER-4 | Upgrade note for `IsActive`, `PrincipalKind`, the suspension columns, and the enum renumbering (`Active` is now `0`) |

### 12.5 Other tracks (outside this plan)

These are tracked in `docs/DOCUMENTATION.md`, in the section "Tenant secrets, settings and navigation: plan and status". They are not repeated here.

- **OpenBao Phase 0:** the Contents bullet; the stale `/_ui/menu` reference; running the fan-out test against the old code.
- **OpenBao Phase 3:** the existing-deployment `ModulusTenantDataStores` migration (deliberately not shipped).
- **OpenBao Phase 4:** connectivity probe and tenant migration in provisioning; cross-node invalidation through the outbox; admin API; metrics and audit; `modulus add-openbao` and the doctor check.
- **Phase 5:** setting engine. **Phase 6:** navigation engine. **Phase 7:** `modulus-ui` adapter (separate repository).

## 13. Decisions needed from the owner

These block the items marked *decision* above. Each has a recommended default. The item does not start until the owner confirms the choice.

| Decision | Blocks | Recommended default |
|---|---|---|
| Finding and audit retention durations | A4-1, A11-1 | Findings 90 days; audit at the regulator's minimum, with holds |
| Privacy and DSAR obligations | A11-1, A4-1 | Purge and tombstone personal fields; keep hash-chain integrity |
| Numeric NFR targets (latency, staleness) | P1b-GATE, PILOT-3 | Revocation visible within 60 seconds; permission check within 10 ms at p95 |
| Rehire approvers | A1-1 | Tenant administrator plus the HR role |
| Permission name for the lifecycle surface | A1-3 | `identity:accounts:manage` (Critical) |
| Canonical identity scope | A2-1 | One person, one identity across the companies of a group |
| Extension value classification | A7-3 | Unclassified keys refused on write |
| Breached-password data source | A12-2 | Offline list; no external call |
| Record sharing in the first release | A8-1 | No; ship after the P1a and P1b gates |
| MFA assurance for shop-floor users | A12 | Shop-floor sign-in stays PIN-only; step-up for Critical permissions |
| Tenant identity-provider policy | A2-1 | A tenant may bring its own provider; the platform keeps the account record |
| Pilot acceptance scenarios | PILOT-1 | Owner supplies three scenarios |
| Impersonation exception | ADR-03 | None; no impersonation in the first release |
