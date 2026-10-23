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
| **A6 Job principal re-check** (P1b) | Initiator re-check at execution and sensitive stages, `SystemJob` identity | **Built.** Initiator re-check (2026-10-19), per-stage re-check through `IJobStageGuard` (2026-10-22) and named job actors (A6-2) are built. |
| **A9 Agent and delegated principal** (P1b) | Effective access bounded by agent and represented principal | **Unverified.** Delegation exists (`DelegationRoleRefresher`, delegation store); agent identity not checked. |

Identity items for later (not started in this round):

| Item | Current state |
|---|---|
| **A1 / A2 / ADR-01 / ADR-02** | **Gap.** `ModulusUser.IsActive` is a bool. Lockout is separate. No lifecycle matrix and no transition history. Identity is tenant-local (`TenantId` on the user row); no canonical identity. Needs a schema inventory and a migration design first. |
| **A12 P1c hardening** | Confirmed (2026-10-24). Lockout, idle and absolute session lifetimes (per-role overrides), session listing and termination exist and are tested: a busy session still ends at its maximum lifetime and its refresh stops too (`SessionHttpTests`). A breached-password check runs on creation, change, reset and invitation, against an offline SHA-1 list (`BreachedPasswordTests`, 10 with the session tests). Caveat: the check is off by default (`Identity:BreachedPasswords:Enabled`) and needs a list file; the generated apps do not yet ship one. |

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
- **Role assignment (2026-10-22, built).** Handing out a role hands out its permissions, so an invitation or shop-floor
  account that names roles needs `authorization:grant-any`, the rule the grant API already applies to role inclusions
  (`RoleAssignmentRules.MayAssign`). Invitations refuse the row; single shop-floor accounts throw `ForbiddenException`;
  an import refuses the whole batch before the first account is created. Decided by the owner's rule above, not yet
  confirmed by the owner: revisit if it blocks a trusted background seeding path. Note: with no HTTP principal the
  default `ICurrentUser` holds no permissions, so background role assignment is refused too (fail closed). Tests:
  `A_caller_without_grant_any_cannot_assign_roles_and_nothing_is_created`; the other Identity tests register
  `GrantAnyCurrentUser`. Identity 397/397 on net10.0 and net8.0.
- **Still open.** The import permission beyond `grant-any` (who may run an import at all) is not decided.
- **Wired later (2026-10-22).** `ShopFloorAccountService.CreateAsync` (single account, admin path) now checks the same six
  fields with `EnsureWriteAllowedAsync` and refuses with `FIELD_PROTECTED` before anything is created. The body moved to a
  private `CreateCoreAsync`, which the import calls after its own batch check, so an import is still checked once.
  Identity unit tests: 396/396 on net10.0 and net8.0 (new: `A_single_account_with_a_refused_field_is_not_created`).
  Format clean on Identity and its tests.
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
  - Import, bulk, export and reports do not call the field authorizer in the framework's own code. Verified over HTTP (2026-10-24, `ClassifiedFieldsHttpTests`, 8 rows): an update that sets a restricted field is refused as a whole (403, nothing written, the public field included), a caller with the write permission changes it, and an unrestricted-only change goes through. A batch import checks every row before writing any, so one restricted row refuses the whole import. Export is covered by `FieldOutputLeakageTests`. Generated CRUD (2026-10-24): the list and by-id reads (and the cursor read when cursor paging is on) call `Redact` on the DTO through an optional `IFieldAuthorizer`, as the writes already did; `ModuleLayoutTests` pins the calls. A DTO field is masked only if it carries the classification attribute, which the generator does not copy from the entity. Still open: the generator emits no import, bulk or report endpoints, so those paths exist only in app code; generated apps are not compiled in tests (VER-3).
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

### A6-1 job initiator re-check (2026-10-19, P1b, built; per-stage re-check built 2026-10-22)

- **Verified first.** The job envelope (`ChannelJobQueue`'s `JobEnvelope` and Quartz's `JobDataMap`) carried the tenant and correlation id but no person, so a queued job ran with no identity at all.
- **Decision used (owner delegated, recommended option).** The initiator is the signed-in person who queued the job. A check that they may still start access runs when the job comes up, and a refused job is skipped and logged, not retried. Recurring jobs carry no initiator and run as the system (this is the A6-2 identity, not yet named).
- **Contract.** `IJobInitiatorGuard.MayRunAsync(initiatorId, ct)` (Platform). The default allows every job, so an application without Identity behaves as before. `AddModulusIdentity` replaces it with `UserJobInitiatorGuard`, which allows only an account that `MayStartNewAccess()` (the A1-2 rule).
- **Both schedulers.** The in-memory queue (`ChannelJobQueue`) and Quartz (`QuartzJobAdapter`) capture and check the initiator.
- **Refusals are audited (2026-10-19, follow-up).** A refused job writes a security audit event: category `authorization`, action `job.refused`, outcome `denied`, actor the queuing person (`person:{id}`), target the job type, company the job's tenant. Both schedulers do this (`ChannelJobQueue` and `QuartzJobAdapter`). There is no jobs category yet, so the authorization category is used.
- **Quartz refusal path is tested (2026-10-19, follow-up).** `QuartzRefusalTests` starts the scheduler and runs a real queued job: a refused initiator's job does not run and is audited; an allowed one runs (control).
- **Per-stage re-check (2026-10-22, built).** A running job calls `IJobStageGuard.EnsureAllowedAsync(permission)` before a sensitive step. It checks that the person who queued the job still holds the permission, through `IJobInitiatorGuard.MayPerformAsync`. Identity answers from the grant store (the initiator must still be able to start access and hold the permission). With no permission resolver registered, the step is refused (fail closed). A job with no person behind it is not checked, as before. A refused step throws `ForbiddenException`. Tests: `JobStageTests` (3: allowed while the permission is held, refused after it is lost, no check without a person). The Identity answer is tested by `JobStageIdentityTests` (4: allowed with the permission, refused without it, refused with no resolver, refused once disabled). **Read-only view of the initiator (2026-10-22, built).** `JobContext.InitiatorId` gives the queuing person's id (null for a system job), and `IJobStageGuard.HoldsAsync(permission)` answers whether that person still holds the permission, without throwing (a job with no person answers true, as the stage check does). Tests: `JobInitiatorViewTests` (3). Decided: `ICurrentUser` stays anonymous in a job. Replacing it would change every component that reads the current user, so handlers that need the initiator's rights use this view, not `ICurrentUser`. The recurring in-memory path is tested (`RecurringJobTests`): the scheduler tick and the first run time are now optional parameters (`ChannelJobQueue`, `AddRecurring`), defaulting to 30 seconds and the next cron time, so a test can make a run due at once. A due recurring job runs once, as `system:{jobId}`, and waits for its next cron time. The Quartz recurring path is tested separately.
- **Verification.** `JobInitiatorTests` (4: refused job skipped, allowed job runs, no person means no check, refusal audited with the person), `QuartzInitiatorTests` (2: initiator carried on enqueue, none on recurring), `QuartzRefusalTests` (2: refused job not run and audited; allowed job runs), `LifecycleEnforcementTests.A_queued_job_stops_running_once_its_initiator_is_disabled`. Platform 421/421, Quartz 7/7, Identity 395/395 on net8.0 and net10.0. Format clean on the changed projects.

### A9-1 agent principal: the access cap (2026-10-19, P1b, partly built)

- **Verified first.** `ISecurityContext.Agent` (documented as "the intersection of the user's and its own" rights) was always null, and nothing issued or read an `act` claim. There was no agent identity and no token exchange.
- **Decision used (owner chose).** An agent is an integration client acting for a signed-in person, as in RFC 8693: the token's `sub` is the person and its `act` claim names the agent's role (`integration:{clientId}` in Identity). Effective access is the person's access intersected with what the agent role holds.
- **Built.** The cap is enforced where every decision path resolves: `PrincipalGrantQuery.AgentRole` (new optional parameter). `PermissionResolver` intersects with the agent role's grants (deny-wins), and `DelegationAwarePermissionResolver` re-caps after it unions in delegated permissions. The HTTP policy (`PermissionRequirementHandler`) and `ICurrentUser` (`GrantStorePermissionChecker`) both read `act`. A permission claim is never trusted for an agent token. Without the grant store, `ClaimsPrincipalCurrentUser` grants nothing to an agent token. A blank `act` grants nothing.
- **Not built.** The token exchange endpoint: nothing issues an `act` token yet, so the cap has no live caller until it does. `ISecurityContext.Agent` is now read from the request's `act` claim (2026-10-22, `SecurityContextAgentTests`, 3 tests): the agent's role, with no agent for a request without the claim or for a job. An agent's access decisions are on the audit trail (2026-10-22): `AccessDecisionAuditEvent.AgentId` carries the agent role, and the security audit record shows it as the `agent` detail. Tests: `AgentDecisionAuditTests` (2). Every other security audit event raised during a request (tenancy, identity, data) also names the agent: `SecurityAuditLog` reads the request's `act` claim when it records, so an event raised outside a request (a job) names none (`SecurityAuditAgentTests`, 3). Not yet: the audit events of a token exchange, which does not exist yet. Background jobs and the AI connector do not apply the cap, because they do not resolve a principal from a token.
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
| A6-1 | Job principal re-check | Done (2026-10-19) for the initiator at execution, and refusals are audited (`job.refused`); the per-stage re-check inside sensitive jobs is built (2026-10-22, `IJobStageGuard`) |
| A6-2 | `SystemJob` identity | Done (2026-10-19) for attribution: every job carries a named actor (`person:{id}` or `system:{name}`); per-job permissions are still open (see A6-1) |
| A8-1 | Record sharing | Slice 1 built (2026-10-23): `RecordShare`, `IRecordShareStore` (in-memory default), `RecordShares` (create with a 30-day default cap, live check, revoke by the sharer only, all in the caller's company), `AddRecordSharing()`. Tests: `RecordSharesTests` (6). Slice 2 built (2026-10-23): `ResourceAuthorizer` gives the grantee the share's one read action (`RecordShareOptions.SharedAction`, default `view`) where no rule granted it, decision code `SHARED`. A share never overrides an explicit deny (`ResourcePolicy.Evaluate` reports it, the policy's own code is unchanged), a tenant mismatch, a missing attribute, or an approval or step-up refusal; a revoked or expired share grants nothing (`RecordShareDecisionTests`, 7). Slice 3 built (2026-10-23): durable EF store `EfRecordShareStore` (table `ModulusRecordShares`, company-filtered in the store and by the ambient tenant filter; host shares stored as `Guid.Empty`; registered by `AddEfCoreAuthorizationStores`). **Upgrade note:** existing deployments need an app migration that adds this table (the framework ships no migrations for this context). Tests: `EfRecordShareStoreTests` (4): round trip, company isolation including the host, host shares, and a revocation that survives a restart. Slice 4 built (2026-10-23): HTTP surface under `/authorization/resources/{type}/{id}/shares` (POST create, GET list) and `/authorization/resources/shares/{shareId}` (DELETE revoke), mapped by `MapModulusResourceAuthorization` only when `AddRecordSharing()` is registered. Creating or listing needs the caller's own read of the record: a grantee reached through a share gets 403, so a share cannot be passed on. Only the sharer can revoke. Tests: `RecordShareEndpointTests` (5). Slice 5 built (2026-10-23): `ResourceAuthorizer.ExplainAsync` reports a live share as the reason (`SHARED`) while still listing the matched rules; the explain endpoint uses it. It does not report a share over an explicit deny. Tests: 3 in `RecordShareDecisionTests`. Slice 6 built (2026-10-23): masked fields for shared records. A share grants a read of the record and no field permission, so a grantee sees the record's unclassified fields and its classified fields stay masked unless they hold the field's own permission (`RecordShareFieldMaskTests`, 2). Reading the masked-reference item as classified-field masking is an assumption; the plan defined no reference model. **Still open:** reference masking (a shared record hiding the targets of links the grantee cannot read) needs a reference model first; the admin UI; the account lifecycle of the grantee is enforced at sign-in and by token checks, not by the share itself. Decision: first release |
| A9-1 | Agent principal | Partly done (2026-10-23): the access cap is enforced on the decision paths, on background jobs and, now, on the AI connector: a signed envelope may carry `act` (`AiEnvelopeClaims.Agent`) naming an integration role; any other value is refused, and the call's principal carries the claim so the grant store caps the user to the agent. Tests: `AgentEnvelopeTests` (3). End to end with real grants (2026-10-23): the connector test host now resolves the user's permissions through the real `PermissionResolver` over an `InMemoryPermissionGrantStore`, so an agent granted `catalog:read` reads, an agent granted only `audit:view` is refused, and an agent never widens a user who lacks the permission (`AgentEnvelopeTests`, 6 in total). The identity module's own `GrantStorePermissionChecker` is internal and is not referenced by this test project; the resolver is the same production code it calls. Earlier notes: Token exchange is built (spec 12A), and the agent is on `ISecurityContext` and in the audit trail. A job queued through an agent carries the agent role (`JobContext.AgentId`), and each permission step is capped by it (`IJobInitiatorGuard.MayPerformAsAgentAsync`). Status after the token exchange (§12A): the agent is capped on the decision paths, on jobs, on the AI connector and on token exchange; the agent's act-for-anyone default is the open point in §12A. See the A9-1 section |
| A9-2 | Delegation bound | Done (2026-10-19) for delegator liveness: a delegation ends when its delegator may no longer start access. Role snapshots still refresh on a timer and on access change, so role staleness is bounded by that interval, not zero |
| A7-4 | Nested and alternate output tests | Done (2026-10-22): `FieldDeepRedactionTests` (nested, list, cycle) and `FieldOutputLeakageTests` (serialized response, export rows with nested values, null and empty parts) cover the leakage cases. Platform 5/5 new on net10.0 and net8.0. |
| P1b-GATE | Revocation latency | Partly done (2026-10-22): `RevocationLatencyTests` (3) show that a revoked user or role grant, or a grant added later, changes the decision on the very next check with the same resolver (no decision cache). Still open: the numeric target (owner decision, section 13) and the measured time for role snapshots and sessions, which the timer and the session store bound. |
| P1b-GATE | Job and agent tests | Done (2026-10-23) for the conformance truth table: `JobAgentMatrixTests` (10) runs the real permission resolver, grant store and stage guard over person allowed, denied and absent, with and without an agent: a step through an agent holds only when the person and the agent both hold it, an agent never widens a person, and a denial stands. Still open: HTTP probes for jobs. No framework endpoint enqueues a job, so there is nothing for an HTTP probe to reach until an app exposes a job-queuing endpoint; the agent HTTP surface (the AI connector) is probed (`AgentEnvelopeTests`) |

### 12.3 P1c: hardening and pilot

| ID | Item | Remaining work |
|---|---|---|
| A11-1 | Audit retention | Slice 1 built (2026-10-23), in memory: `SecurityAuditRetention` purges only an archived, expired, contiguous prefix of a chain and leaves a checkpoint (the purged head); `SecurityAuditChain.VerifyFrom` verifies the rest from it; a chain on legal hold (`SecurityAuditHolds`) is never touched; with no archive (`ISecurityAuditArchive`, default refuses) nothing is purged; a chain that fails verification is not purged. Default period 730 days is a **placeholder**: section 13 says the regulator's minimum, which needs the deployment's figure. Tests: `SecurityAuditRetentionTests` (8). Slice 2 built (2026-10-24): `EfSecurityAuditStore` now stores the purge checkpoint (table `modulus_security_audit_checkpoints`, one row per chain: **new table, existing deployments need a migration**) and purges: it deletes the chain's entries through the checkpoint in one transaction, refuses a checkpoint that is not an entry of the chain, and refuses to move the purge point backwards (`EfSecurityAuditStoreTests`, 3 new rows). **Decided (2026-10-24, chosen by the maintainer's delegation): a dedicated purge role.** On PostgreSQL, `SecurityAuditDatabaseScripts.PostgreSql(app, schema, purgeRole)` grants only the purge role DELETE on `modulus_security_audit`; the trigger refuses every update and truncate for everyone, and permits a delete only for the purge role on an entry at or below a checkpoint recorded for its chain. `AddModulusAuditPurgeRole(...)` registers the purge context on the purge role's connection; `PurgeAsync` writes the checkpoint before the delete, in one transaction, and refuses without a purge context. Verified on a PostgreSQL container (`AuditPurgeRolePostgresTests`, 5 rows): the application role cannot delete or update, the purge role deletes through the checkpoint and the store keeps the rest valid, and the purge role cannot delete beyond the checkpoint or update. **SQL Server (2026-10-24): built and verified on a real SQL Server 2022 container.** `SecurityAuditDatabaseScripts.SqlServer(app, schema, purgeUser)` gives the purge user `SELECT, DELETE` on the table and checkpoint rights, refuses every update with an `INSTEAD OF UPDATE` trigger, and lets an `AFTER DELETE` trigger roll back any delete that is not by the purge user at or below its chain's checkpoint. Unit-tested (`SqlServer_purge_role_script_lets_only_the_purge_user_delete_through_the_checkpoint`). `AuditPurgeRoleSqlServerTests` (5 rows, the PostgreSQL rows' twin) passes on net8.0 and net10.0 against `mcr.microsoft.com/mssql/server:2022-latest`: the application user cannot delete or update; the purge user deletes through the checkpoint and the store keeps the rest valid; it cannot delete beyond the checkpoint or update. The first pull failed on the network and succeeded on retry. **MySQL (2026-10-24): built and verified on a real MySQL 8.4 container.** `SecurityAuditDatabaseScripts.MySql(app, database, host, purgeUser)` gives the purge user `SELECT, DELETE` on the table and checkpoint rights; a `BEFORE DELETE` trigger refuses every delete unless the connected user (`USER()`, not the trigger's definer) is the purge user and the entry is at or below its chain's checkpoint; updates are refused for everyone. `AuditPurgeRoleMySqlTests` (5 rows) passes on net8.0 and net10.0. The beyond-checkpoint refusal is asserted on the trigger's message, so a connection failure cannot pass it. Without a purge user the MySQL table stays append-only for every user, as before.  the purge role's grants must be run as the migration role by an administrator. **Durable hold register (2026-10-24, built):** `EfSecurityAuditHolds` keeps holds in table `modulus_security_audit_holds` (**new table: existing deployments need a migration**). Placing a hold is the application's (INSERT); releasing needs the database owner (no DELETE is granted). A held chain is refused by every purge, checked inside the purge's own transaction, so a direct `PurgeAsync` is refused too, not only retention. Each database script grants the holds rights. Tests: `EfSecurityAuditStoreTests` (3 hold rows), `SecurityAuditRetentionTests`, and one hold row in each of the PostgreSQL, SQL Server and MySQL purge suites (18 integration rows on net8.0 and net10.0). **Scheduled host (2026-10-24, built, opt-in):** `AddModulusSecurityAuditRetention(configuration)` binds `Security:Retention` (`Interval` default 1 day, `LockLease` 30 min, `Retention` default 730 days) and runs `SecurityAuditRetention` on a `PeriodicTimer` (`SecurityAuditRetentionService`). When an `IDistributedLock` is registered, one replica runs a pass at a time (`modulus:audit:retention:leader`); without one, every replica runs it, so a multi-replica deployment must register a distributed lock or risk archiving a chain twice. Tests: `SecurityAuditRetentionServiceTests` (4 rows: archive and purge under the lock, no archive keeps everything, a replica without the lock runs nothing, the schedule runs and stops, plus the registration row); Platform 501/501 on net10.0 and net8.0. **Archive (A11-2, 2026-10-24, built):** `EfSecurityAuditArchive` (registered by `AddModulusAuditStore`, replacing the no-archive default; a custom archive registered earlier is kept) keeps each purged entry as a row in `modulus_security_audit_archive` (**new table: existing deployments need a migration**), with its JSON payload and both hashes. A write is idempotent, a different entry at an archived sequence is refused, and a segment that does not verify is refused before anything is written; the first entry must follow the genesis hash. `ReadAsync` and `VerifyAsync` read the archive back and verify it on its own. The database scripts grant the application user SELECT and INSERT only, so the archive cannot be changed through the application. **Defect found and fixed:** on MySQL the chain could not verify after a read-back: `datetime` kept whole seconds, so stored timestamps no longer matched their hashes. `OccurredAt` now uses `datetime(3)` on SQL Server and PostgreSQL and Unix milliseconds in a `bigint` on MySQL (**column type change: existing MySQL deployments need a migration**). The purge suites now verify the surviving chain after the read-back. Tests: `EfSecurityAuditArchiveTests` (4 rows: retention archives before it purges and the archive verifies, idempotent re-archive, a disagreeing entry refused, a broken or non-genesis segment refused); 21 integration rows across PostgreSQL, SQL Server and MySQL; audit EF 19/19 and Platform 501/501 on net10.0 and net8.0. **Restore validation (built 2026-10-23, read-only):** `SecurityAuditRestoreValidator.ValidateAsync(chain)` proves that the archive and the live store form one chain from genesis: the archive ends exactly at the store's checkpoint, the archive verifies from genesis, and the live entries verify from the checkpoint. It writes nothing, and it reports a chain it cannot read back (an archive without read support) as not provable. Tests: `SecurityAuditRestoreValidatorTests` (6 rows, Platform 507/507 on net10.0 and net8.0; audit EF 19/19). **Still open:** re-inserting archived entries into a live chain (not built; the plan requires the technical spike to approve it first, because it would rewrite committed history); the archive lives in the audit database, so a deployment that needs cold storage must register its own `ISecurityAuditArchive`; the archive's own grants under the purge role are not set, because the purge role never reads or writes it |
| A11-2 | Audit archive and restore | Integrity verified across an archive and a restore. **Decided (2026-10-23, §14):** re-insertion into a live chain is not built; restore is validated read-only |
| A14, A15 | Defined in the BRS, not in this file | Scope to be confirmed before work starts (*verify*) |
| ADR-03 | Deny precedence | Done (2026-10-23): `DenyPrecedenceTests` (12) checks a person or role deny against every allow source (direct, role, delegation, wildcard); sharing is covered by `RecordShareDecisionTests` (an explicit policy deny stands over a share) |
| ADR-05 | Audit model | Confirm the audit event model against the evidence requirements |
| B1 to B7 | Defined in the BRS, not in this file | Scope to be confirmed before work starts (*verify*) |
| PILOT-1 | Pilot module contract | A pilot module that exercises tenant isolation and deny-by-default end to end |
| PILOT-2 | Migration rehearsal | Migration run on a populated prior-version schema, with a tested rollback |
| PILOT-3 | Performance | Measured on representative data for each query change |
| PILOT-4 | Runbooks | Operator runbooks for lifecycle changes, revocation, audit restore and key rotation |
| TEST-1 | Conformance suite | Built (2026-10-23) as `Modulus.Testing.Authorization`: `AuthorizationConformance.RunAsync(subject, otherCompany)` runs 14 named rows of the grant truth table (direct, role and wildcard allows, person and role denies, expiry and start date, restriction, delegation, agent cap, company isolation) against the app's subject, and reports each failure by name. The durable EF store passes every row (`EfAuthorizationConformanceTests`); a broken subject, a throwing subject and a shared store each fail the rows they break (`AuthorizationConformanceTests`). Record level added (2026-10-23): `ResourceConformance.RunAsync(subject)` runs 11 rows against the production `ResourceAuthorizer` on a sample record: ownership, the org-unit scope, an archived deny, a missing owner refused as `MISSING_ATTRIBUTE`, and record shares (a live share reads, a share never edits, a revoked share ends, an archived record stays closed to a share, only the sharer revokes). The durable share store passes every row (`EfAuthorizationConformanceTests`); a store that forgets a revocation fails the revoke row by name (`ResourceConformanceTests`). Approval rules added (2026-10-23): `ApprovalConformance.RunAsync(subject)` runs 9 "up to this amount" rows through the production `ApprovalAuthorityEvaluator`: within and over the limit, no limit, the largest applicable limit, currency match, permission, document type, company unit and validity. The durable approval store passes every row; a store that ignores validity fails the expiry row by name. Delegated limits added (2026-10-23): `ApprovalDelegationConformance.RunAsync(subject)` runs 7 rows: a delegate uses the delegator's limit for the delegated permission and never more, no delegation or another permission or an expired or revoked window lends nothing, and the delegate's own larger limit is the one compared. The durable limit and delegation stores pass every row; a delegation store that ignores windows and revocations fails by name. Approval step rules added (2026-10-23): `ApprovalWorkflowConformance.RunAsync(subject)` runs 8 rows against the production `IApprovalWorkflow`: the requester cannot approve their own request, a holder of the step permission can (control), a user without it cannot, one person cannot take two steps, a two-approval step needs two people, a decided request is closed, a rejection ends it for everyone, and only the requester withdraws. The in-memory and durable (EF) chain and request stores pass every row; a workflow that lets one person take two steps fails that row by name. Org placement added (2026-10-23): `OrgPlacementConformance.RunAsync(subject)` runs 8 rows through the production `OrgScopeResolver` on a three-level tree with a sibling and a second root: a unit-only placement reaches just its unit, a downward placement reaches every level beneath it, a downward placement does not reach up or across, an upward placement reaches the units above but not siblings, a placement never reaches another tree, a placement at an unknown unit still covers it, several placements union, and a person with no placement has no reach. The in-memory stores and the durable EF hierarchy and placement stores pass every row; a hierarchy that forgets descendants fails the downward row by name. Not in the kit: moving a unit and seeing its subtree follow. The EF hierarchy caches its snapshot for 30 seconds (`CacheDuration`), so a move reaches other nodes after that delay; that staleness is measured under TEST-3. TEST-1 has no open rows. |
| TEST-2 | Probe coverage | `SecurityProbeSuite` extended to read, list, search, count, aggregate, export, create, update, classified fields, bulk, imports, related lookups, jobs and agents  Slice 1 built (2026-10-24), behavioural: `ScopeQueryAgreementTests` runs the scope enforcer's database query over a SQLite table and checks it returns exactly the rows the per-record check accepts, for own, org-unit subtree, assigned customer and company-wide scopes; an export over the table reaches no further than reading. Status probes are unchanged. Slice 2 built (2026-10-24), HTTP: `ScopeBulkAndExportHttpTests` (Management tests) calls real endpoints that use `EnsureAllInScope` and `ForExport`. A bulk approval of in-scope orders goes through; one foreign order refuses the whole batch with 403 and approves nothing. An export returns exactly the caller's readable set and is a subset of their list; an export without the read permission is 403. Slice 3 built (2026-10-24), HTTP classified fields: `ClassifiedFieldsHttpTests` (Management tests): a restricted field is masked in the response for a caller without its read permission, shown to a caller who holds it, and an anonymous caller gets 401. The test user resolves permissions from the grant store, as production does. Slice 4 built (2026-10-24): `ScopeBulkAndExportHttpTests` adds a related lookup (`/customers/{id}/orders`) that applies the same read scope as the list, so the caller sees only their own orders for that customer. Slice 5 built (2026-10-24): a bulk import (`/orders/import`, rows upserted by id) refuses the whole batch when any row would overwrite or create an order outside the caller's scope. Own-row imports go through; a foreign-owned row, or an overwrite of a foreign order, is 403 with nothing written (`ScopeBulkAndExportHttpTests`). Agents over HTTP are covered by the connector's envelope tests (`AgentEnvelopeTests`, the `act` claim and the agent cap). Slice 6 built (2026-10-24): `JobsOverHttpTests` and `JobsWithoutInitiatorIdentityTests` (Management tests, Development host, so scope validation is on). An endpoint checks the caller's scope and queues an approval job; the job checks the queuer's permission at its step (`IJobStageGuard`) and the batch's scope. An in-scope approval queued over HTTP is carried out for its queuer; an out-of-scope one gets 403 at enqueue and is never queued; a queuer who lost the permission before the run is refused at the step (`refused-permission`). **Bug found and fixed:** `ChannelJobQueue` read the enqueuing user from its root provider, so in Development (scope validation on) every request that queued a job threw, and in Production a job could be queued without its initiator and skip the initiator checks. It now reads the request's own caller and falls back to the provider outside a request (existing job tests kept: `Modulus.Platform.Tests` 496/496, `Modulus.Identity.Tests` job and lifecycle 61/61). **Gap found, open (framework decision):** a job has no request, so the scope resolver's grant query (`HttpPrincipalGrantQuerySource`) is anonymous and every scoped step refuses, even with the initiator bound. The safe result is fail closed; the test host supplies a job-aware `IPrincipalGrantQuerySource` (`JobPrincipalSource`) to show the shape. **Decided and built (2026-10-24): job roles are read at run time.** The worker reads the queuer's roles from `IUserRoleDirectory` when the job starts, after the initiator guard passes, into `JobContext.Roles`; the queue stores no roles. `HttpPrincipalGrantQuerySource` answers a job with the queuer and those roles, so role grants reach jobs and `ApprovalWorkflow` and `ApprovalAuthority` get the same. Reason: the identity store is the source of truth, a role removed after queueing must not be used, and the identity job guard already reads roles at run time. An unknown person or no directory gives no roles, so role grants are refused. Tests: `JobsOverHttpTests` (a role grant reaches the job; a role the store no longer gives does not, even though the request carried it), Management 111/111, Platform 496/496, Authorization EF 103/103 on net10.0 and net8.0. **Built (2026-10-24): `ICurrentUser` inside a job is the queuer.** `ClaimsPrincipalCurrentUser` (Identity, the shipped implementation) answers `UserId`, `IsAuthenticated` and `IsInRole` from the job when there is no request: the queuer's id and the roles loaded at job start (`JobContext`). A request still answers from its own principal. Tests: `JobCurrentUserTests` (through the real queue: the probe job sees Bob and his role; a job with no person has no caller), Identity 425/425, Management 111/111, Platform 496/496 on net10.0 and net8.0. Format clean. **Still open:** an app that writes its own `ICurrentUser` must answer the same way from `JobContext` (`InitiatorId`, `Roles`). The test host's `JobCaller` shows the pattern. Nothing in the framework can do this for a custom implementation. |
| TEST-3 | Concurrency tests | Role and grant changes, revocation, session termination and cache invalidation, with measured staleness. Slice 1 built (2026-10-23): `OrgHierarchyStalenessTests` runs two nodes (separate containers over one SQLite file, one clock). A move on one node is visible there at once. On the other node the old tree is served until its snapshot expires: still stale at 29 seconds, fresh at 31 seconds (`CacheDuration` 30 s). A node that loads after the write sees the move at once. The measured window is a finding: `Invalidate` clears only the writing node, so a replica can serve a moved unit's old scope for up to 30 seconds. Slice 2 built (2026-10-23): `GrantChangesAcrossNodesTests` measures grants across two nodes. A grant, a revocation and a user deny over a role grant each show on the other node at the next decision (measured staleness: zero, because the EF grant store is read on every decision). A finding from the measurement: `IPermissionGrantStore` is a scoped `CachedPermissionGrantStore`, so it memoizes grants for the life of its scope. A scope that outlives a change keeps its first answer (pinned by a test). Each request has its own scope, so a request sees changes at once; a long-lived scope must not make authorization decisions. Slice 3 built (2026-10-23): `DelegationRoleStalenessTests` measures delegation role snapshots against the identity store. A role change the store reports reaches the snapshot at once. One it does not report stays in the snapshot until the next refresh (measured: the snapshot still holds the old roles before the refresh). The refresh timer, run at a 50 ms interval in the test, picks the change up within a second; in production the interval is one minute (`DelegationRoleRefreshOptions.Interval`), so an unreported change can cap a delegation by a stale role for up to a minute. Slice 4 built (2026-10-23): `SessionsAcrossNodesTests` (identity) and `MembershipAcrossNodesTests` (multi-tenancy) measure sessions and memberships across two nodes. A session started, ended, or ended in bulk on one node reads the same on the other at its next check, and a membership added or removed does too; the registry and the membership store query the database on every check with no cache (measured staleness: zero). A concurrency finding, now fixed: `TenantManager.AddMemberAsync` checked the seat count and then inserted, with no lock between the two. On a PostgreSQL container (`TenantSeatRacePostgresTests`, Testcontainers via Podman), eight adds from two nodes to a one-seat company all took a seat without the lock, and exactly one with it. The fix runs the seat check and the insert in one transaction that first locks the company row (a no-op update), so a concurrent add waits until the first commits; the write goes through the execution strategy, so retrying providers still work. The SQLite unit test (`MembershipAcrossNodesTests`) passes both ways, since SQLite serializes writes and cannot show the race. Slice 5 built (2026-10-23): `TokenRevocationAcrossNodesTests` (identity) measures revocation across two nodes on one database. Revoking every session on one node empties the list on the other at its next read, and revoking one session removes it there at once. The session list and the access-token check both read the stored OpenIddict entries on every request, so the measured staleness is zero. SCIM seat use goes through the same `TenantManager.AddMemberAsync` (its other counts only page lists), and reactivation of a revoked membership is in the same method, so the seat lock covers both; the PostgreSQL test measures that path. TEST-3 has no open rows. | ID | Item |
|---|---|
| VER-1 | Done for the unit suites (2026-10-23): every test project runs on net8.0 and net10.0, and the full solution unit run passes on both. Integration tests were not run. Original scope: run net8.0 for: the dimension registry and EF store, the A3 guard, A7 field authorization, lifecycle and transition changes, and `IAccountLifecycleService` |
| VER-2 | Migrations for existing deployments: `ModulusDimensionValues` and `ModulusPermissionCatalog` (authorization context); lifecycle columns and `ModulusAccountTransitions` (identity context). Document the commands in the release notes | Done (2026-10-23), see `docs/upgrades/RELEASE-NOTES-2026-10.md`. Scripts per database and engine (PostgreSQL, SQL Server, MySQL) for the authorization, audit and identity databases; each is idempotent, was run twice against old-shape tables, and matches EF's generated DDL for the same model. The audit `OccurredAt` narrowing (PostgreSQL and SQL Server) is lossless because the hash covers whole milliseconds; the MySQL conversion is recorded as a break in the release notes (see the MySQL audit row below). |
| VER-2b | MySQL audit history before the 2026-10 upgrade | Stored as `datetime` (whole seconds): entries with a non-zero millisecond part cannot be verified again. **Decided (2026-10-23, §14):** accept the break; record the pre-upgrade chain heads as proof (the upgrade notes require it); do not re-anchor |
| VER-3 | Done for the `api` kind (2026-10-24). Packed the framework locally (`dotnet pack modulus.slnx -c Release -p:VersionSuffix=local.1 -o <dir>`, 47 packages), generated `VerApp` with `MODULUS_FRAMEWORK_VERSION=1.5.0-local.1 dotnet run --project cli -- app VerApp --kind api --auth openiddict`, restored against that directory and nuget.org. It builds with 0 errors and 0 warnings across all 12 projects (including the generated read handlers that call `Redact`) and its tests pass 9/9. The other kinds were generated and checked the same way: `webapp+api` (Razor Pages): 14 projects, 0 warnings, tests 14/14; `api --multi-tenancy`: 0 warnings, tests 11/11; then `generate-crud Order --module Catalog` on the multi-tenancy app: builds with 0 warnings. That run exposed a defect, now fixed: the integration-event contract snapshot depended on which assemblies the test process had loaded. A test run alone did not load the framework's Platform assembly, so its recorded names (`authorization.*`, `tenancy.*`) read as removed. `ModuleBoundaryRules` now loads the Modulus assemblies that the loaded ones reference before it scans, so the set is the same in every run. After it, the generated multi-tenancy app passes 12/12 with the contract test run alone and in the suite; framework `Modulus.Testing.Tests` 21/21 on net10.0 and net8.0. `generate-crud Order --module Catalog` on the plain `api` app also builds with 0 warnings and passes 9/9 (2026-10-24). Still open: the generated apps reach the framework only through a local pack (`MODULUS_FRAMEWORK_VERSION` plus a local feed), because the packages are not published. |
| VER-4 | Done (2026-10-23), see the upgrade note below |

**Upgrade note (identity lifecycle, VER-4).** `ModulusUser` changed in four ways a deployment must handle:
- `Lifecycle` (`AccountLifecycle`) is the account state. `Active` is now `0`, so a stored `0` means active; `Invited` `1`, `PendingActivation` `2`, `Disabled` `3`, `Archived` `4`. Any row written with the old numbering must be remapped before the change goes live.
- `IsActive` is still on the entity and still read by nothing new; it is removed in the next release (A1-2). Keep it in step with `Lifecycle` until then.
- `PrincipalKind` (`PrincipalKinds`, default `person`) distinguishes people from other principals.
- The suspension columns are `SuspensionReason`, `SuspensionNote` and `LifecycleChangedAt`; `ModifiedAt` and `CreatedAt` are also new on the entity.

**Upgrade note (migrations, VER-2).** The framework ships no migrations for these contexts; the host application generates them. The new or changed tables are:
- authorization store: `ModulusDimensionValues`, `ModulusPermissionCatalog`, `ModulusRecordShares` (A8-1), and the earlier tables listed in the sections above;
- identity: the lifecycle columns on the user table and `ModulusAccountTransitions`.
Generate the migration in the host with `dotnet ef migrations add <Name>` against each context (or `modulus migrate add <Name> --module <Module>` for module contexts), review it, then `dotnet ef database update` or `modulus migrate update`. The exact context names depend on the host's `AddEfCoreAuthorizationStores` and identity registration. Not verified here: a generated app built against these migrations (VER-3).

### 12.5 Other tracks (outside this plan)

These are tracked in `docs/DOCUMENTATION.md`, in the section "Tenant secrets, settings and navigation: plan and status". They are not repeated here.

- **OpenBao Phase 0:** done (2026-10-23). The Contents bullet exists, no stale `/_ui/menu` reference remains in the Improvement Plan, and the fan-out tests fail on the pre-fix code and pass on the current code.
- **OpenBao Phase 3:** the existing-deployment `ModulusTenantDataStores` migration (deliberately not shipped).
- **OpenBao Phase 4:** connectivity probe and tenant migration in provisioning; cross-node invalidation through the outbox; admin API; metrics and audit; `modulus add-openbao` and the doctor check.
- **Phase 5:** setting engine, built 2026-10-23 (`docs/DOCUMENTATION.md`, Tenant secrets, settings and navigation). **Phase 6:** navigation engine, built 2026-10-23 (same section). **Phase 7:** `modulus-ui` adapter (separate repository).

## 12A. Spec: agent token exchange (A9-1, built 2026-10-23)

Status: **built (2026-10-23)**, on the approved default for the open point below. As built:
- `grant_type=urn:ietf:params:oauth:grant-type:token-exchange` at `/connect/token`, enabled by `Identity:TokenExchange:Enabled` (`AddModulusTokenExchange`). The request also needs `subject_token_type` and `actor_token_type` (OpenIddict requires them).
- **Deviation:** `act` carries the agent's role (`integration:{clientId}`), not an object with `sub`. The existing readers (`ISecurityContext.Agent`, the access cap, the audit `agent` detail) already read that value, so the role is kept; the agent is still identified by its client id inside the role.
- Refusals are `invalid_grant` (400) with the reason in `error_description`; a missing token is `invalid_request`. Every exchange and refusal writes a security audit event (`token.exchange`). The audit records are asserted (`TokenExchangeHttpTests`: the success names the person and the agent; a refusal is denied, names the agent, and has no person).
- The per-agent flag is `IIntegrationClientDirectory.SetMayActForPeopleAsync` (default off).
- Shared access-token checks moved to `AccessTokenChecks`, used by introspection and the exchange.
- Tests: `TokenExchangeHttpTests` (6): the happy path (sub is the person, act names the agent, lifetime at most 15 minutes), flag off, another company, a token issued through an agent, a disabled person, and a missing actor token. Identity 417/417 on net10.0 and net8.0.

**Goal.** An integration client (an agent) acts for a signed-in person, and every decision made on the agent's behalf is capped by the agent's own rights (the access cap that A9-1 already enforces on the decision paths).

**Standard.** RFC 8693 (OAuth 2.0 Token Exchange), at the existing token endpoint (`/connect/token`).

**Request.**
- `grant_type=urn:ietf:params:oauth:grant-type:token-exchange`
- `subject_token`: the person's access token (must verify, must not be expired or revoked, and must not itself be an agent token).
- `subject_token_type=urn:ietf:params:oauth:token-type:access_token`
- `actor_token`: the agent's own client-credentials token (the `integration:` role, bound to a company).
- `actor_token_type=urn:ietf:params:oauth:token-type:access_token`
- `scope`: optional; the result is never wider than either token.

**Issued token.**
- `sub` = the person (the subject token's `sub`), so the person stays the authority.
- `act` = `{ "sub": "<agent client id>" }`, with the role `integration:{clientId}` carried as today; `ISecurityContext.Agent` and the audit `agent` detail already read this claim.
- `tid` = the person's company, which must equal the agent's bound company.
- Lifetime: at most the subject token's remaining life and never more than 15 minutes.
- No refresh token (the exchange is repeated when needed).

**Refusals (all `invalid_grant` or `access_denied`, with no detail that reveals another company).**
1. The subject token does not verify, is expired, or was revoked (check the token entry, as introspection does).
2. The subject token already carries an `act` claim (no agent of an agent).
3. The actor token is not a client-credentials token for an `integration:` role, or the agent is disabled or unbound.
4. The person's company is not the agent's bound company.
5. The person may no longer start access (the lifecycle rule).
6. The agent is not allowed to act for people at all (an explicit flag, default off).

**Audit.** Each exchange (issued or refused) writes a security audit event: category `identity`, action `token.exchange`, actor the person, with the agent in the `agent` detail.

**Verification (to be written first).**
- Exchange succeeds for a person in the agent's company, and the issued token's `act` names the agent.
- Each refusal above, individually.
- The issued token's effective access is the intersection of the person's and the agent role's, through the existing cap (reuse `AgentBoundPermissionTests`' cases over HTTP).
- A revoked person token cannot be exchanged.

**Open point for review (recommended default).** Whether an agent may act for any person in its company, or only for people it is explicitly linked to. Default proposed: any person in the bound company, with the agent flag (item 6) off until an administrator turns it on per agent.

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

**Decided (2026-10-22, owner asked for the choices to be made).** Every row above takes its recommended default, recorded here as decided. The numeric NFR targets stay provisional (startup warning outside Development) until measured. In addition:
- **Import permission.** Creating shop-floor accounts, singly or by import, needs `authorization:manage` (the Critical permission that already exists). A background process with no current user is not checked, as for the field checks. Tests: `Only_an_account_administrator_may_create_or_import_shop_floor_accounts`.
- **Enterprise extension.** Deferred: no new columns until a release. Revisit with the attribute list.
- **Publishing the framework.** Deferred: no publish until a release is chosen. The generated-app blocker (published 1.5.0 lacks the newer types) stands until then.

## 14. Engineering decisions (2026-10-23, delegated by the maintainer)

The maintainer asked engineering to decide the open items. Each choice below is recorded here with its reason. A choice that needs a named business or compliance approver says so; engineering does not record that approval.

| Item | Decision | Reason | What is still needed |
|---|---|---|---|
| D7 pilot module | Inventory first, Procurement next (the plan's position) | Inventory has the fewest cross-module dependencies, so it exercises the tenant and deny-by-default paths with the least noise | A named business approver for the pilot |
| D8 retention and privacy | Engineering default 730 days (`Security:Retention:Retention`), holds always win, personal fields purged and tombstoned, hash-chain integrity kept. The scheduled purge stays off until a deployment enables `AddModulusSecurityAuditRetention` | A purge that runs by default could delete evidence before anyone has set the legal minimum | Compliance owner's minimum period and DSAR obligations, before any deployment enables the purge |
| D9 NFR targets | Revocation visible within 60 seconds; permission check within 10 ms at p95. Delegation role refresh interval lowered from 1 minute to 30 seconds so the slowest staleness path sits inside the target | TEST-3 measured the paths: token and session revocation and grants are read per request (zero staleness); the org hierarchy snapshot is 30 seconds; delegation roles were up to 1 minute | The 10 ms p95 has not been measured yet; it needs the P1b benchmarks (PILOT-3) |
| D10 MFA assurance | Shop-floor sign-in stays PIN-only; Critical permissions need a fresh `mfa` sign-in (`StepUp`). Both are built | Matches the plan's position and the code as built | None for engineering |
| VER-2b MySQL audit history | Accept the break. Record the pre-upgrade chain heads as proof (the upgrade notes already require it). Do not re-anchor | Re-anchoring would hide the break; recording the heads keeps the evidence that existed before the upgrade | Operators capture heads before running the MySQL upgrade script |
| A11-2 re-insertion of archived entries | Not built. Restore is validated read-only by `SecurityAuditRestoreValidator` | Re-insertion rewrites committed history, and the validator already proves the archive and the live store form one chain | None, unless a regulator requires re-insertion |
| ADR-05 audit model | Accepted as implemented: append-only chain, anchors, retention, holds, archive and purge are built and tested | The evidence requirements in the plan are met by the built and tested integrity core | Compliance review of the event model, recorded with the D8 sign-off |
| PILOT-1 to PILOT-4 | Still open. They are engineering and business work, not decisions | PILOT-1 needs the pilot's acceptance scenarios from the business; PILOT-2 to PILOT-4 need a populated schema, benchmarks and runbooks | Pilot scenarios (PILOT-1); engineering work for PILOT-2 to PILOT-4 |
| OpenBao Phases 5 and 6 | Deferred. Not started in this release | Each is several days of work and no release needs them | A decision to schedule them |
| OpenBao Phase 7 (`modulus-ui`) | Belongs to the `modulus-ui` repository, not this one | Cross-repository work | The `modulus-ui` owners |
| Operator-owned OpenBao items (Raft, TLS, auto-unseal, audit devices, snapshots, Kubernetes roles) | Operators own them. This repository documents them and does not build them | They run on the operator's infrastructure | The operator's runbook |

### 14.1 Owner decision memo (2026-10-23): proposed, pending sign-off

An owner decision memo was pasted into the session. It is recorded here as **proposed**, not approved. The memo names no approver and no sign-off date, so none is recorded. The section 14 choices above are superseded where the memo differs.

| Item | Memo position | How the code and plan reflect it | Gap still open |
|---|---|---|---|
| D7 pilot | Approve a controlled pilot on one low-risk module, on isolated infrastructure with synthetic data; zero unresolved critical or high findings without an approved exception; record module, scope, criteria, exclusions and approver | Not yet recorded as a D7 document. The approver and module are not named | D7 document, with the named module and approver (needs the owner) |
| D8 retention | Configurable retention; seven years as the proposed baseline, not a legal determination; append-only; logged disposal with legal-hold checks; evidence in protected storage | Default changed from 730 to 2555 days (`SecurityAuditRetentionOptions.Retention`). Purge stays opt-in. Holds, the append-only trigger, archive and checks are built | Legal and contractual retention confirmed before production. Disposal evidence in protected storage is operator work |
| D9 NFRs | Availability ≥ 99.5% monthly; p95 ≤ 500 ms and p99 ≤ 2 s for standard APIs; RPO ≤ 15 min; RTO ≤ 4 h; provisional, not contractual | Recorded as pilot baselines. The engineering targets in section 14 (revocation within 60 s, 10 ms p95 permission check) still apply | Workload profile and measurement method documented; RPO and RTO verified by a real backup and restore exercise; nothing is measured yet |
| D10 MFA | Phishing-resistant MFA (FIDO2 or passkeys) for privileged access to production secrets and security-critical infrastructure; authenticator app as a documented fallback; no SMS or email as the sole factor; restricted break-glass; logged privileged events; exceptions with owner and expiry | Passkeys (`AddModulusPasskeys`), step-up for Critical permissions and the `amr` claim are built. Shop-floor sign-in stays PIN-only | **Built, opt-in (2026-10-23):** `StepUpOptions.PhishingResistantPermissions` requires a recent passkey sign-in (`hwk` or `swk` in `amr`) for the named permissions; a password with an authenticator app is refused for them. Default empty, so nothing changes until the list is set. Tests: `StepUpTests` (3 new). **Still open:** the owner names the privileged permissions and the exceptions; the break-glass procedure for operators is not built |
| VER-2b | Accept the limitation; preserve the pre-upgrade records and backups as read-only evidence; record counts, timestamps and checksums; set a new anchor at the post-upgrade boundary; label earlier history "not fully verifiable"; do not rewrite | Matches the decision in section 14. The upgrade notes require pre-upgrade heads | Operators capture the evidence and the new anchor during the MySQL upgrade |
| Archive re-insertion | Not authorized. Mandatory technical spike first (order, signatures, checkpoints, duplicates, provenance, a separate verifiable archive segment with a signed manifest) | Matches section 14. Not built | The spike, with a reviewed design and owner approval |
| Pilot work PILOT-1 to PILOT-4, ADR-05, TEST-3 | Authorized once the D7 criteria are signed; same scope and gates; do not bypass a failed gate | TEST-3 has no open rows in the plan. The rest are open | Start after the D7 sign-off |
| OpenBao Phases 5–7 | Approved as separate work packages, each with scope, dependencies, estimate, acceptance criteria and rollback, and each with its own completion review | Section 14 deferred Phases 5 and 6 for scheduling. The memo approves them as packages; it does not require starting them now | A scope, estimate and rollback plan per phase, when they are scheduled |
| Operator OpenBao items | Confirm as operator responsibilities: Raft sizing, TLS, auto-unseal, audit devices, snapshots and restore, Kubernetes roles, break-glass handling. No production readiness sign-off without the operator's evidence | Matches section 14. Nothing here is built by engineering | Operator evidence for each item |

### 14.2 D7 pilot record (drafted 2026-10-23, pending owner confirmation)

| Field | Value |
|---|---|
| Pilot module | Garments ERP **Inventory** (ADR-07; the plan's first pilot). Chosen by engineering on the maintainer's instruction to decide |
| Why this module | Fewest cross-module dependencies, so the tenant and deny-by-default paths are exercised with the least noise |
| Test scope | Tenant isolation, deny-by-default, permission and grant changes taking effect, audit-event completeness, backup restore, rollback |
| Infrastructure | Isolated pilot environment; synthetic or non-sensitive data only |
| Exclusions | Long-running reports, asynchronous processing and external-provider delays (measured separately, as in the memo's D9 wording); no production data |
| Accountable approver | **Not named.** The owner must name one. Engineering does not record an approval |
| Success criteria | The three scenarios below pass; zero unresolved critical or high findings without an approved exception; the backup and rollback drill passes |

**Acceptance scenarios (PILOT-1, drafted by engineering; the owner confirms or replaces them):**

1. **Tenant isolation.** A user of company A cannot read or change inventory of company B through the API, an export, a background job or a report. Each attempt is refused (403 or 404) and recorded in the security audit.
2. **Deny by default and grant changes.** A user with no inventory grant is refused a stock adjustment (403). After a grant, the same call succeeds. After the grant is revoked, the next request is refused; no re-login is needed.
3. **Privileged operations and audit evidence.** A privileged (Critical) operation is refused without a recent multi-factor sign-in, and succeeds with one. Each attempt, allowed or refused, is recorded in the audit chain with its actor and company. The chain verifies afterwards. A stock adjustment is the pilot's routine operation; the privileged case is a Critical permission from the framework (for example `identity:accounts:manage`).

**Limits of this draft.** Inventory's endpoints are not in this repository, so the scenarios are written at the behaviour level. Mapping them to the real endpoints needs the Inventory module's code. The PILOT-2 to PILOT-4 work (migration rehearsal, performance, runbooks) is not started and depends on this record being signed.

**Sample check (2026-10-23).** The repo's stand-in, `samples/TradeFlow/src/Modules/Inventory`, has the stock adjustment at `POST /stock-items/{siteId}/{itemId}/adjust` (`CreateManualAdjustmentEndpoint`). It declares **no** `Permissions(...)`, so under the closed-by-default fallback any signed-in user can adjust stock: scenario 2 would fail. Its command handlers take `currentTenant.TenantId ?? Guid.Empty`, so with no company selected they read and write the empty tenant: scenario 1 would fail too. The sample is not a pilot-ready module. The garments ERP Inventory module is not in this repository, so this is a gap for that module's team to close before PILOT-1 can pass. Engineering has not changed the sample.

**Mapping to the owner's acceptance outcomes (decision record, 2026-10-23).** Outcome 1 (authorized access succeeds) is scenario 2's grant half. Outcome 2 (unauthorized and cross-tenant access denied) is scenarios 1 and 2. Outcome 3 (privileged controls and verifiable audit evidence) is scenario 3. The scenarios are still drafts until the owner confirms them, and the formal sign-off is recorded in the repository, not here.

### 14.3 Owner decision record (2026-10-23): status by item

Received as a pasted decision record. Proposed signatory: Azim Mahmud. **Formal sign-off is not recorded here.** Each row states what is built and what is not.

| Item | Owner decision | Status in the code and plan |
|---|---|---|
| D7 pilot | Approved in principle; confirm the three scenarios against the outcomes; test evidence and a go/no-go before wider rollout | Scenarios mapped to the outcomes (§14.2). Not confirmed. No test evidence yet; the pilot module has no code in this repository |
| D8 retention | No production purge until the legal and contractual minimum, per data category, is approved | Purge is opt-in (`AddModulusSecurityAuditRetention`) and off by default. **Gap:** the retention is one period for every chain; per-category retention is not built. The 2555-day default is a proposal, not a legal minimum |
| D10 privileged access | Approve the classification; enforce by default in production; no permanent exemptions; break-glass only for a documented incident; service identities least-privilege; exceptions recorded with owner and expiry | **Built:** generated production apps require the MFA step-up for Critical permissions (`AuthenticationSetup.sbn`, `IsProduction()`; the step-up needs an enrolled second factor, so enrol before deploying). **Not built:** (a) the passkey requirement is not on by default (`PhishingResistantPermissions` is empty), so it is not enforced in production as the record asks; (b) privileged permissions that do not exist yet as Critical in the framework: audit configuration, retention and export, database administration and deployment, disabling monitoring, and framework settings that affect tenant isolation; (c) the break-glass procedure; (d) the exception register. **Passkey enablement blocked (2026-10-23):** the owner asked for the passkey rule in production. It is not enabled, because generated apps do not wire passkey enrolment (`AddModulusPasskeys` / `MapModulusPasskeys` appear in no template, and no UI calls the enrolment endpoints). With the rule on, every production admin would be refused every Critical operation with no way to register a passkey. **Chosen (2026-10-23): option 1,** build passkey enrolment first, then enable the rule. Slices: (a) **done (2026-10-23):** generated API hosts with the local token server register `AddModulusPasskeys<AppIdentityDbContext, ModulusUser>` and map `MapModulusPasskeys` only when `Identity:Passkeys:Enabled` is set; development enables it for localhost (`http://localhost:5180`), base settings leave it off. Verified: a generated app builds with 0 warnings, starts in Development, answers anonymous login options with a challenge (`rpId` localhost, user verification required), refuses anonymous enrolment and listing with 401, and the seeded admin still obtains a token. Tests: `PasskeyWiringTests` (4). A build against an older local package was stale (`MissingMethodException`); the verification used a fresh NuGet cache; (b) **done (2026-10-23):** a Razor page `/Passkeys` in the generated Web host (`webapp+api`, OpenIddict) adds, lists and removes the signed-in person's passkeys through the BFF gateway (`PasskeyApiClient` → `/api/account/passkeys`). Verified in headless Chromium with a virtual authenticator: sign in, add (`Begin` 200, `Complete` 200), list, remove (204), then the list is empty; anonymous visitors are redirected to sign-in. Found and fixed on the way: the API must allow the Web origin (`http://localhost:5181`, WebAuthn checks the page's origin); the typed client needs its base address; a 204 from the API must not carry a body. Not verified: production mode (the security guard with anonymous passkey endpoints), passkey sign-in from the Web host, and a real hardware key; (c) enable `PhishingResistantPermissions` for production only after (a) and (b) pass. Until (c) the rule stays off.

**(d) done (2026-10-23):** passkey sign-in on the Web login. The BFF has `POST bff/login/passkey` (the passkey grant through `IBffSessionService.SignInWithPasskeyAsync`, sharing the password path's session setup); the Web sign-in page has a "Sign in with a passkey" button (`passkey-login.js`), and its options come from an anonymous client to the API. The seeded first-party clients may use the passkey grant. Verified in headless Chromium with a virtual authenticator: sign in with a password, add a passkey, clear the cookies, sign in with the passkey alone, list, remove.

**Found and fixed while verifying (2026-10-23, serious):** the Web host's HTTP clients kept the BFF's `Set-Cookie` in their own cookie container and replayed it on every later request, so after one person signed in, another visitor's request could be treated as that person's session (observed: an anonymous POST authenticated as the admin). The BFF-facing clients now use `UseCookies = false` (`AddBffGatewayClient`, the BFF session client, the passkey clients), with a regression test. Existing generated apps need regenerating or the same one-line change.

**(c) done, with two caveats (2026-10-23):** production now refuses to start without `Identity:Passkeys:Enabled` (an admin could not enrol otherwise), and in production the framework's privileged permissions (`identity:accounts:manage`, `scim:manage`, `tenancy:data-stores:manage`, `authorization:manage`, `authorization:grant-any`, `authorization:entitlements:manage`, `authorization:break-glass`) need a passkey sign-in. Caveats: (1) no generated app maps those admin surfaces yet, so in a default generated app the rule protects nothing; it takes effect in an app that wires them; (2) production needs the relying-party ID and origins configured for the deployment (owner-specific values, not defaulted). Not verified: a production-mode run of the generated API.

**(c) was blocked (2026-10-23, now resolved by (d)):** the Web host signs people in only with a password (`bff/login` → the password grant), and that issues `pwd`, `otp`, `mfa` only (`TokenPrincipalFactory`). The phishing-resistant rule needs `hwk` or `swk`, so with (c) on, an admin with a passkey still could not perform a privileged operation from the Web host. Needed first, as a new slice (d): a passkey sign-in path in the Web login and BFF (the passkey grant behind `bff/login`), verified in the browser with a virtual authenticator. Then (c). Also: production admins need a TOTP second factor enrolled before the MFA step-up takes effect, or the same lockout applies |
| D9 NFR | Keep the 10 ms p95 permission-check objective, marked unverified until P1b passes | **Measured, not met for every scenario (2026-10-23, §14.5.1).** On the EF store with PostgreSQL 17 and one host: within 10 ms at p95 up to about 100 applicable grants at concurrency 32; over it at 1,000 grants at concurrency 32 (47 ms) and at 10,000 grants sequentially (17 ms). One run per row, one host, one principal shape. **Owner decision needed** (§14.5.1): the option to keep the 10 ms target, the cache trade-off, or a grant-count rule. P1b's remaining parts (cache states, a representative mix, a longer run) are not run. |
| Archive reinsertion | Deferred; no build; any future work starts with a spike and approval | Not built |
| Framework release | Approved after release checks: CI, compatibility, generated-app build and smoke tests, package contents, release notes, rollback plan | **Not published.** Publishing needs an explicit instruction. Checks not yet run in full |
| Enterprise extension attributes | Deferred until after the newer-types release | Not built |

**Release gate before publishing:** the CI run, the compatibility check against 1.5.0 consumers, a generated-app build and smoke test, the package-contents check for the new types, the release notes, and the rollback or supersession plan.

### 14.4 Owner decision (2026-10-23): privileged surfaces in generated apps

Approved: every generated application that exposes identity-account administration maps every such route, action and endpoint to `identity:accounts:manage`, server-side, fail-closed, with no implicit grant to any role, least privilege, generated tests through the UI and the API, and the permission discoverable in the authorization configuration. A default generated app must not pass security acceptance while an exposed identity-administration surface lacks the mapping.

**Status:** a default generated app exposes no identity-account administration (the framework's lifecycle and SCIM routes are mapped only when an app calls them, and no template does). The gate therefore holds, and the work done is: the permission is registered as Critical in every OpenIddict generated app and granted to no role (`IdentityAdminPermissionTests`). Not built: the generated tests for a surface, which apply once a generated app exposes one. Such a surface has to be generated with its mapping and tests in the same change.

### 14.5 Permission-check benchmark (D9, 2026-10-23)

Benchmark: `tests/benchmarks/Modulus.Benchmarks/PermissionCheckBenchmarks.cs` (BenchmarkDotNet, Release, short job, net10.0). It measures one resolver decision with an in-memory grant store, so database I/O is **not** in these numbers.

| Grants | Roles | Warm decision (mean) | Cold decision (mean, new resolver) |
|---|---|---|---|
| 100 | 1 | 4.8 µs | 10.3 µs |
| 100 | 5 | 4.9 µs | 10.5 µs |
| 10,000 | 1 | 995.6 µs | 2.28 ms |
| 10,000 | 5 | 957.7 µs | 2.16 ms |

Reading: the decision scans the grants, so its cost grows linearly (about 100 ns per grant). At 10,000 grants the warm in-memory mean is about 1 ms, which is inside 10 ms before any database round trip.

**D9 status:** the in-memory numbers above are means and exclude the database, so they do not settle the objective. The database measurement below does, for the scenarios it covers.

#### 14.5.1 Database measurement (EF store, PostgreSQL 17, 2026-10-23)

Tool: `tests/benchmarks/Modulus.Benchmarks.EfLatency` (a console probe, not in the solution and not run in CI). It seeds grants through the store's own API into an empty database (it refuses a database that already has a schema), then times one decision the way a request runs it: a fresh scope, the principal's grants read from the database, then the resolver. Five roles, the principal holds all five, one target grant. 5,000 checks per run after 200 warm-up checks. Single run per row. Client, PostgreSQL 17 (container) and the probe ran on one machine that was also running other work, with no network hop.

| Grants the principal's roles hold | Concurrency | p50 ms | p95 ms | p99 ms | Checks/s |
|---:|---:|---:|---:|---:|---:|
| 100 | 1 | 1.68 | 2.53 | 2.90 | 582 |
| 100 | 8 | 2.08 | 3.72 | 6.37 | 3,393 |
| 100 | 32 | 7.76 | 10.58 | 14.84 | 4,090 |
| 1,000 | 1 | 2.02 | 4.92 | 5.94 | 405 |
| 1,000 | 8 | 4.97 | 6.84 | 8.55 | 1,586 |
| 1,000 | 32 | 30.96 | 46.94 | 56.55 | 990 |
| 10,000 | 1 | 13.67 | 17.08 | 20.31 | 72 |
| 10,000 | 8 | 61.51 | 81.33 | 94.26 | 128 |
| 10,000 | 32 | 239.19 | 297.06 | 329.38 | 133 |

Reading. The decision reads every grant the principal's roles hold, on every check: nothing is cached across requests (`PermissionRequirementHandler` memoizes within one request only), so the cost grows with the grant count and with concurrency. In this setup:

- **Within 10 ms at p95:** up to about 100 applicable grants at up to 32 concurrent checks, and 1,000 grants when sequential.
- **Over 10 ms at p95:** 1,000 grants at concurrency 32 (47 ms), and 10,000 grants even sequentially (17 ms).

**Not claimed:** that the target is met for a deployment. The probe uses one principal shape (five roles, all grants on those roles), one database on the same host, and one run per row; a representative tenant mix, network latency and a longer run were not measured. The result is a finding for the owner, not a pass.

**Decision needed (owner, §13):** how the 10 ms objective applies when a principal holds many grants. The options, which the owner chooses between: (a) keep 10 ms and cache a principal's grants for a short time, accepting that a revocation takes effect after that time (conflicts with the revocation-latency gate unless the cache is invalidated on every grant change); (b) keep the grants per principal low by design (fewer, broader role grants, or role inclusion); (c) keep the grant model and set the objective by grant count and concurrency.

### 14.6 Production check (2026-10-23)

Run against a generated `webapp+api` app (OpenIddict, passkeys on) started in Production (`ASPNETCORE_ENVIRONMENT=Production`).

| Check | Result |
|---|---|
| No `Identity:Passkeys:Enabled` | Refuses to start, names the missing setting (exit 134) |
| Passkey settings present, no migrations | Stops at the authorization store: migrations required (by design) |
| Migrations authored (authorization store, Catalog, Identity) | Applied at startup on empty databases |
| Anonymous request to a privileged route | 401 |
| Password-only admin token to the privileged route | 401 `STEP_UP_REQUIRED` (`WWW-Authenticate` acr `mfa`) |
| Passkey enrolled with the admin's password session | 200 |
| Passkey sign-in (passkey grant, `amr` hwk) | token 200 |
| Privileged route with the passkey token | **200** |
| Privileged route with the password-only token | **401** |

Scratch-only changes that the check needed (none of them in the templates): migrations authored with `dotnet ef` for the authorization store (a design-time factory naming the migrations assembly), throwaway certificates registered with the token server, `AllowPasswordFlow` and an admin seeded from settings, a privileged probe route guarded by `identity:accounts:manage`, and the grant of that permission to the Admin role.

Gaps the check exposed, now in the release notes: generated apps register no production certificates (now read from `Identity:Certificates:Signing` and `:Encryption`, 2026-10-23); production needs the authorization store's migrations authored by the application; and no generated app maps a privileged route, so the rule protects only applications that add one.

**Production check: passes, with the scratch-only setup above.** It is not a full deployment check: TLS, real certificates and the operator's database are not exercised.


### 14.7 Owner decision memo (2026-10-23, second): recorded

The owner's decision memo pasted into the session (2026-10-23) is recorded here. It approves design and development work within the stated boundaries. It does not record any executed test, completed operator task or published release; each row says what evidence is still missing. Where it differs from §14.1 and §14.3, this section governs.

| Item | Recorded decision | Status and what is still needed |
|---|---|---|
| D7 pilot approver | Framework owner (Azim Mahmud) is the accountable go/no-go approver. An independent reviewer validates the security test results, not the owner alone. | Recorded. The three scenarios of §14.2 still need confirmation against the outcomes, and no pilot code exists in this repository yet (PILOT-1). |
| D8 retention | Seven years (2555 days) as a provisional product default, subject to longer applicable requirements. Production purge stays disabled until legal and contractual requirements are confirmed. Legal holds override expiry. | Recorded. The default matches the built value (`SecurityAuditRetentionOptions.Retention`). Purge stays opt-in. Per-category retention is still not built (§14.3). |
| D10 privileged permissions | A mandatory privileged-permission list, with tightly controlled exceptions. Enforced on applicable production and generated-app surfaces. Default deny when a privileged surface has no mapping. Exceptions justified, least-privileged, logged, time-limited where possible, reviewed. Service identities and emergency access get their own narrow controls. | Recorded. Generated production apps already require step-up for Critical permissions, and the template lists the phishing-resistant permissions (§14.3, §14.4). Progress (2026-10-23): `messaging:manage` (outbox inspection and replay) is now Critical; the procedure, the classification table and the gaps are in `docs/runbooks/privileged-access.md`. Still open: the privileged surfaces that do not exist yet in the framework (audit retention and export, key access), the exception register kept by the operator, and a break-glass review deadline set by the operator. |
| D9 permission-check latency | Retain the 10 ms p95 objective. Make no claim that it is met. Report warm and cold cache separately, with p99, throughput and errors, and define the timing boundary before testing. | Recorded as unverified. §14.5 measured it against the EF store: not met for every scenario. P1b must produce the evidence. |
| ADR-05 evidence | Reproducible tests another engineer can review, not screenshots or an assertion that a test passed. Covers deny-by-default, privilege mappings, tenant isolation, benchmark method, compatibility, migration and rollback, a named acceptance owner, known limitations and an approval record. | Recorded as the acceptance standard. Work is accepted only when it meets this, not because it exists. |
| A2.3.6 `m-fields` / `m-datatable` overload | Approved as an additive, backward-compatible API change. Existing overloads and call sites keep their behaviour and there is no ambiguous resolution. Must add tests for new and existing call sites, and must pass the build and regression tests. A breaking or ambiguous design is revised, not forced through. | Approval recorded. The overload is UI work in the `modulus-ui` repository (§Phase B note), so engineering evidence is needed there. |
| AI platform 6-series | No production activation until the real platform's conformance suite passes. Development and isolated testing continue. The suite covers authentication, authorization, tenant isolation, provider and capability handling, streaming, timeouts and retries, token accounting, secret protection, and behaviour when the platform or a provider is down. Activation is gated, and the feature stays off by default. | Recorded. The framework's own conformance kit (`Modulus.AI.Connector.Testing`) does not replace the real suite. Production activation is blocked until it passes (open item "Against the real platform" in docs/DOCUMENTATION.md, AI connector section). |
| OpenBao (operator) | Mandatory end-to-end TLS, including cluster traffic. Raft of five nodes preferred, three as the constrained-HA minimum (one node may fail; a one- or two-node cluster is not HA). Auto-unseal through an independent KMS or HSM preferred; a documented manual-unseal alternative if none exists, never a stored static key. Two independent audit destinations, tested for failure. Kubernetes roles bound to explicit service-account names and namespaces, with access and denial both tested. | Requirements recorded. Operator-owned: none of this is deployed or verified here. Production readiness needs the evidence the memo lists: TLS validation, quorum and recovery, seal and restart recovery, audit-device failure, snapshot restore, Kubernetes access tests, with the configuration version and an accountable sign-off. |

**Not claimed by this record:** that any test, benchmark, operator task or release has been executed for these items, or that production is ready. The owner's memo says the same.

### 14.8 Grant copy for the permission check (built 2026-10-23, off by default)

`TtlPermissionGrantStore` (`Modulus.Platform`) keeps each principal's grants on this node for `Authorization:GrantCache:Duration`
(`PermissionGrantCacheOptions.Duration`). It keeps grants only, never a decision. The default is zero, which means off, and then a
revoked grant applies on the next check (`RevocationLatencyTests`). A change announced through `IAccessChangeObserver` empties the
copy at once on this node; other nodes wait for the duration. Concurrent misses for one principal share one database read. Keys
include the company. Tests: `TtlPermissionGrantStoreTests` (7, both targets). **The owner must choose the duration** (D9): it is the
trade-off between the 10 ms objective and revocation time on other nodes.

Measured with `tests/benchmarks/Modulus.Benchmarks.EfLatency` on PostgreSQL 17 (one host, 5 roles, 5,000 checks, one run per row, a
warm copy except where stated). p95 in ms:

| Grants | Copy | c1 | c8 | c32 | Notes |
|---:|---|---:|---:|---:|---|
| 1,000 | off | 4.69 | 7.13 | 45.63 | the database cost of every check |
| 1,000 | 5 s | 0.31 | 0.17 | 0.17 | warm; a miss pays the database cost |
| 10,000 | 5 s (before the resolver change) | 3.14 | 8.26 | 38.94 | warm; resolution over 10,000 grants was the cost |
| 10,000 | 5 s (after the resolver change) | 1.52 | 7.64 | 11.72 | 5 s copy expires during the run; a miss costs a database read (max 2.1 s at c32) |
| 10,000 | 300 s, fully warm | 2.45 | 2.42 | 2.40 | the target is met warm (max 8.9 ms) |

The resolver now starts its implication closure only from the allowed permissions that name prerequisites (`PermissionResolver`), and a
randomized test (`ResolverEquivalenceTests`, 400 rounds with wildcards, denials, restrictions, lapsed and future grants, retired
permissions and prerequisite cycles) checks that it gives the same permissions as a full closure. A deliberately broken closure fails the
test. With that change 10,000 grants meet the 10 ms target warm (2.4 ms p95). A miss still reads the database, and at 10,000 grants under
load a miss took up to 2.1 s in one run. The warm rows do not show contention: a cache hit completes without yielding, so the probe runs
its checks one after another. They measure per-check cost. Warm and cold are reported separately, as the memo asks.

### 14.9 Status after the completion round (2026-10-23)

| Item | Status | Evidence or what is needed |
|---|---|---|
| Permission-check latency (10 ms p95) | Met warm: 1,000 grants 0.17 ms p95 and 10,000 grants 2.4 ms p95 (c32), with the grant copy on. Cold (a miss) reads the database; not met for a miss under load at 10,000 grants | §14.8. Resolver change verified by a randomized equivalence test. The owner still chooses the copy duration (D9). |
| Per-category audit retention | Not built | The purge removes a prefix of one hash chain (archive first). A window per category needs one chain per category, which changes the audit format of existing deployments. Owner decision. |
| Exception register | Built (2026-10-23) | `EfPermissionExceptionStore`, `MapModulusPermissionExceptions`, table `ModulusPermissionExceptions` (upgrade step 4, verified twice on PostgreSQL 17, SQL Server 2022, MySQL 8.4), `PermissionExceptionApiTests` (8). It reports; it does not enforce. |
| Cross-node settings without Redis | Built (2026-10-23) | `ModulusSettingVersions` change counter, `SettingVersionPoller` every `Settings:VersionCheckInterval` (5 s). Tenancy upgrade step 4, verified twice on each engine. `SettingVersionPollerTests` (2). |
| MVC web host routing | Fixed (2026-10-23) | Attribute-routed controllers mapped; the default route requires sign-in. Verified on a generated MVC app: `/widgets` redirects to sign-in, health 200. |
| `ui create-form-from-entity` partial | Clarified | The command says it wrote a partial and points to `ui create-crud`. The partial is still not a page, by design. |
| `generate-crud --ui-engine` | Open | The flag is accepted and ignored; no files are misplaced. Rejecting it is a small change, not yet made. |
| `/account/*` on the Web host | Not reproduced | The Blazor host maps its own; the Razor and MVC hosts sign in through the BFF. |
| Round trip in the templates | Not built | Needs the example Web pages decision (`webapp+api` apps have no example pages). |
| Authorization upgrade scripts | Built (2026-10-23) | Step 5 creates every table of the model, generated from the EF DDL and idempotent; verified twice on each engine (31 tables each). Shape changes to existing tables are not in the scripts. |
