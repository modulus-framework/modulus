# Premodules plan: identity and auth, audit, settings, profile, tenant management

Status: **plan, not started** (2026-10-23). Each premodule is a framework module that a generated application registers
instead of writing its own. "Premodule" here means: the engine (already in the framework), the HTTP API (the endpoints an
application or UI calls), the screens (in `modulus-ui`), the CLI wiring (`modulus app` / `modulus add-*`), and tests. A premodule is
done when all five parts exist and its acceptance tests pass in a generated application.

Rough sizes below are engineering estimates for planning only, not commitments.

## What exists today (checked in source, 2026-10-23)

| Premodule | Engine (exists) | HTTP API (exists) | Screens (modulus-ui) | Gaps |
|---|---|---|---|---|
| **Identity and auth** | `Modulus.Identity` (accounts, lifecycle, invitations, two-factor, sessions, login history, SCIM, LDAP, SAML, passkeys, device code, back-channel logout); OpenIddict token server; BFF web session | Passkeys (`/api/account/passkeys`), SAML sign-in endpoints (prefix set by the application), account lifecycle (`PUT /{id}/lifecycle`), rehire, SCIM `/scim/v2`, sessions (`account/sessions`), two-factor setup/enable/disable, login history (`account/login-history`) | Sign-in, sign-out, access denied, passkey enrolment, sessions in the generated Web host; no user administration screen | No admin screen for users, roles or invitations; no user list API for admins beyond SCIM |
| **Audit** | `SecurityAuditLog` (hash chain per company), `ModulusAuditDbContext`, retention, restore validator, anchors | Login history (self) only | None | No admin query API for the security audit or the business audit log; no viewer; no export |
| **Settings** | `ISettingManager`, typed definitions, pins, EF store, change counter and Redis backplane | `/api/settings` (`GET`, `PUT|DELETE tenant/{name}`, `user/{name}`, `global/{name}`) | None | No settings screen; no generated admin page |
| **Profile** | Identity user (`ModulusUser`), current-user abstraction (`ICurrentUser`) | Self-service sessions, passkeys, two-factor, login history | Passkey enrolment page only | No endpoint to read or change the signed-in person's own profile (name, language, time zone, contact) |
| **Tenant management** | `TenantManager` (create, membership, status, seats), `ITenantStore`, membership store, data stores (OpenBao pointers) | Tenant data store endpoints (prefix set by the application) only | None | No HTTP API for listing, creating, suspending or closing companies; no membership admin API; no screen |

Shared gaps that every premodule depends on: a UI shell for administration pages (the navigation menu now comes from the server,
so each premodule contributes its items through `AddNavigationContributor`), a permission per screen, and generated tests.

Permission names: `identity:accounts:manage`, `settings:manage` and `audit:view` exist in the code today. Names marked *new* are proposals and must be registered before use.

## Release 1 progress (2026-10-23)

Built and tested (framework, and verified on a generated application):

- **Permission catalog** (D-P2): `GET /api/identity/permissions` (`identity:accounts:manage`). Lists permission, description, sensitivity,
  lifecycle, replacement, group and implied permissions. Tests: `RoleAdminHttpTests`.
- **Roles** (D-P1): `GET|POST /api/identity/roles`. Create refuses a duplicate name (`409`), the reserved `integration:` prefix and a blank name
  (`400`), and is audited. Tests: `RoleAdminHttpTests`.
- **Role permissions** (D-P1): `GET|PUT /api/identity/roles/{role}/permissions`, in `Modulus.Authorization.Management` so the grant guards are
  reused. PUT sets the role's allow grants: an administrator grants only what they hold, a role they hold cannot be changed by them, a
  Critical permission needs `authorization:grant-any` to grant or revoke, unknown permissions are refused, a toxic combination is refused
  (`409`), every change is audited. Deny grants are shown and left alone. Tests: `RolePermissionApiTests` (5, both targets).
- **First administrator** (D-P1): `Identity:Seed:RolePermissions` in configuration, applied once per database by the existing grant seed.
  Verified: the seeded admin opened the Users page and was refused the Critical grant to another role (`403`, with the reason).
- **Generated wiring:** the catalog, roles and role permissions are mapped for OpenIddict apps; the management package is referenced and
  registered when the grant store is. Verified: a fresh generated app builds with 0 warnings and passes its 15 tests.

Still open in Release 1 for premodule 1 (decided, not built):

- **Role delete** (D-P1), built: `DELETE /api/identity/roles/{name}` (`MapModulusRoleDelete<TRole,TUser>`). Refused (`409`) when accounts
  still hold the role (`ROLE_IN_USE`), when it holds grants (`ROLE_HAS_GRANTS`, the check is `IRoleDeletionGuard`, implemented in the
  authorization management assembly over the grant store), when it is the default role (`DEFAULT_ROLE`), and refused without a guard
  (`GRANT_CHECK_UNAVAILABLE`, fail closed). Reserved names are `400`, unknown names `404`. Audited as `identity.role.deleted`.
  Tests: `RoleAdminHttpTests` (4, both targets) and `RoleDeletionGuardEfTests` (4, against the EF grant store: no grants, allow, deny only, other roles' grants).
- **Role rename** (D-P1), built as a choice: `PUT /api/identity/roles/{name}` with `{name}`. It runs the same checks as delete (no accounts, no grants, not the default role, not reserved), so no grant or membership is left under the old name. Taken names are `409`. Audited as `identity.role.renamed`. Tests: `RoleAdminHttpTests`. A rename of a role in use is refused until the role is emptied; moving memberships is a later option.
- **Field guard** (D-P3), built with the platform's `AddFieldSecurity` and `IFieldAuthorizer`. Name and email are `Restricted` on the `UserSummary` and `UserDetail` models and need `identity:accounts:personal-data` (Sensitive) to read. Masked in the list and detail; search by address matches only for holders of the permission. Not yet: export (none exists), and phone and avatar (not fields yet). Tests: `UserAdminHttpTests`. Decision (2026-10-23): the generated app does not grant it by default; an operator grants it to the administrators' role in configuration, the same way as the account administration permission (no role is granted by default).
- **Concurrency** (D-P12), built for the user routes: `GET /identity/users/{id}` returns the account's concurrency stamp as `ETag`; roles,
  lock and unlock accept `If-Match` and answer `412` (`STALE_WRITE`) when it names an older state; every successful write returns the new
  `ETag`. Role changes refresh the stamp, so a tag read before them is stale. Role routes: `GET /api/identity/roles/{name}` returns the role's
  concurrency stamp as `ETag`; rename and delete accept `If-Match` and answer `412` (`STALE_WRITE`) for an older state. Tests:
  `RoleAdminHttpTests` (read tag, stale refused, rename returns a new tag) and `UserAdminHttpTests`.
- **Idempotency** (D-P13): role create is covered by the host's idempotency middleware when the app enables it (`enable_idempotency`); the
  middleware answers a repeated `Idempotency-Key` with the first response. Nothing per-route is added. Invitations are not built in this
  premodule's routes yet (the framework's invitation service exists).
- **Effective access** (read-only), built: `GET /api/identity/users/{id}/effective-access` (under the account administration permission)
  returns every permission the account holds through its direct grants and roles, delegations and the count of toxic combinations. The
  Users page shows it in a "Permissions in effect" list. Test: `UserAdminHttpTests`. The `/authorization/effective-access/{userId}` route
  stays for operators.

Premodule 1 additions (2026-10-23), all built and tested:

- **Invitations:** `POST /api/identity/invitations` (`UserInvitationRequest`) wraps `IUserInvitationService`. Inviting into any role needs
  `authorization:grant-any` (the service's rule); a refusal is `400` with the reason. Audited as `identity.user.invited` with the actor.
  Repeating an invitation for an account that has no password yet resends the link. Test: `UserAdminHttpTests`.
- **Personal fields:** the detail carries phone number and employee code as `Restricted` (masked without `identity:accounts:personal-data`).
- **List filters:** `status` (`active`, `inactive`, `locked`, `any`) and `role`. Lock status is read in memory for the accounts that have a
  lock-out end, because SQLite cannot compare `DateTimeOffset` in SQL; the set is small. Test: `The_user_list_filters_by_status_and_by_role`.
- **Sessions and sign-in history:** `GET {id}/sessions`, `DELETE {id}/sessions/{sessionId}` (audited) and `GET {id}/login-history`.
- **Masked export:** `GET /api/identity/users/export` (CSV, same filters, the detail's masking, at most 5,000 rows, formula cells kept as
  text). Audited as `identity.user.exported` with the row count only. Test: `The_export_masks_personal_fields_...`.
- **Generated Users page:** invite form, status and role filters, export link, and sessions (end one) and sign-in history on the detail.
  Verified: a fresh generated `webapp+api` app builds with 0 warnings and passes its 15 tests. The page was not driven in a browser.

Roles (2026-10-23, the gate chosen by the team, to be confirmed by the owner):

- **Gate (D-P17, a technical choice):** the role list, create, rename and delete need `identity:accounts:manage`; the role's permissions
  (read and set) need `authorization:manage`. The role delete and rename routes had no permission check before this round (any signed-in
  account could call them); they now require `identity:accounts:manage`. Test: `RoleAdminHttpTests` (a signed-in account without it gets `403`).
- **Roles page** in the generated web app (`/Admin/Roles`, menu entry for `identity:accounts:manage`): lists the roles, creates, renames and
  deletes them, and shows and sets the permissions when the caller holds `authorization:manage`. Verified: a fresh generated `webapp+api` app
  builds and passes its 15 tests. The page was not driven in a browser.

Still open on this premodule: the built-in administrator role's removal refusal (it needs a rule for which role is built in, an owner
decision).

## Decisions after the PRD review (2026-10-23)

The product requirements document (PRD v1.0, planning baseline) was reviewed against all its requirement groups (FR-01 to FR-34) and
the non-functional requirements. The earlier review of this plan covered accounts, roles, permissions and personal-field protection
only; the PRD asks for more. Decisions below follow the PRD's three rules: keep the kernel small, keep optional capabilities optional,
and require evidence for each capability. Where a decision is an owner decision in the decision record, it is marked as such.
The owner may revise these.

### Requirement mapping

| PRD requirement | Premodule | Status | Decision |
|---|---|---|---|
| FR-05.1 to FR-05.4 tenancy models and context | 2 | Built (context, membership, isolation) | Keep |
| FR-05.5 access only to permitted tenants | 1, 2 | Built for accounts; roles and permissions to add | Test in each premodule |
| FR-05.10 onboarding, suspension, reactivation, export, offboarding | 2 | Onboarding, suspension, reactivation built; export and offboarding not | Decided below (D-P5) |
| FR-04.3 scopes incl. branch | 5 | Global, tenant and user only | Decided below (D-P4) |
| FR-04.5, FR-04.6 sensitive settings, auditable changes | 5 | Sensitive values and audit partly built | Verify in premodule 5 |
| FR-04.9 controlled rollout, emergency disable | 5 | Feature flags built; no admin screen | Settings screen shows flags read-only |
| FR-06.9 identity is not permission | 1 | Built (permission checks on every route) | Keep |
| FR-07.2 to FR-07.5 roles, scopes, field restrictions | 1 | Scoped grants and field security built; no role administration | Decided below (D-P1, D-P3) |
| FR-07.6 delegation, temporary privileges | 1 | Built (access requests, break-glass, delegations) | Show on user detail (read) |
| FR-07.7, FR-18 approval for high-risk actions | 1 | Approval workflow built | Not used for role changes in Release 1 (D-P9) |
| FR-07.9 failures do not reveal data | all | Built (`404` for other companies, no detail in `403`) | Keep |
| FR-07.11, FR-21.5 permission changes audited | 1 | Built for user writes; role and grant writes to add | Required in 1 |
| FR-07.12 privileged operations distinguishable | 1 | Critical permissions and step-up built | Keep |
| FR-08.6 time zone, locale through contracts | 3 | Not built | Profile fields (premodule 3) |
| FR-09.5 distinguishable error kinds | all | Built (one error catalog) | Use the catalog |
| FR-09.9, FR-12.4 duplicate requests | all | Admin writes are set operations | Decided below (D-P13) |
| FR-11.8 concurrent changes do not overwrite silently | 1, 3 | Not used on the admin path | Decided below (D-P12) |
| FR-16 files, avatar | 3 | Not built | Release 2 (D-P8) |
| FR-17 notifications | 1, 3 | Identity email sender abstraction only | Release 2 (D-P7) |
| FR-18 workflow | 1 | Built | Later use (D-P9) |
| FR-21.7 to FR-21.10 audit protection, search, export | 4 | Chain and retention built; no query or export | Premodule 4 (D-P6) |
| FR-22.5, FR-22.8 classification, personal-data rules | 1, 3 | Classification built; no field guard on admin routes | Decided below (D-P3) |
| FR-25.4, FR-25.5 locale and time zone precedence | 3 | Not built | Premodule 3 |
| FR-26.7, FR-26.8, FR-26.10 platform vs tenant admin; no business data | 1, 2 | Partly built (host context for tenant admin) | Keep admin routes free of business data |
| FR-28 subscriptions, entitlements | none | Not built | Out of scope for Release 1 (D-P10) |
| FR-31.7, FR-31.10 upgrade rules | all | Upgrade scripts exist for the tables | Each new table needs a script (D-P16) |
| AI Platform, DevOps/Kubernetes | none | Out of scope by the PRD | Not in this framework (D-P11) |

### Decisions

- **D-P1 Role and grant administration.** Role management and role permissions are administered through `/api/identity/roles` (list,
  create, rename, delete, a role's permissions). Role-permission changes call the existing grant store, so they keep the grant cap and
  the separation-of-duties checks. Only the holder of `authorization:grant-any` can grant a Critical permission or a permission they do not
  hold. The whole authorization API stays unmapped in generated apps. The first administrator is granted through configuration
  (`Identity:Seed:RolePermissions`, applied once per database by the existing grant seed, marker recorded), because no one can grant on the first run. This answers the open
  grant question with option (b), plus role routes gated by `authorization:grant-any`.
- **D-P2 Permission catalog.** `GET /api/identity/permissions` lists every registered permission with its module, description and
  sensitivity. It needs `identity:accounts:manage`, so no new permission is created for reading.
- **D-P3 Personal data and the field guard.** Email, first and last name, phone, employee code and avatar are classified as personal
  data. The user list and detail pass through the field guard (`FieldOutputGuard` with a `FieldSecurityProfile`). A caller without the new
  permission `identity:accounts:personal-data` (Sensitive) sees these fields masked. Personal values are never written to logs or audit
  details (the audit records ids and actions only).
- **D-P4 Branch scope for settings.** Premodule 5 adds an organization-unit scope (`IOrgHierarchy` already exists), after the company scope.
  Precedence: user, then branch, then company, then installation, then the default. This is needed by PRD FR-04.3 and is Release 1.
- **D-P5 Company export and offboarding.** The contract is defined in premodule 2: a manifest of the company's data by module, an export
  job that uses the module's own export operations, and a retention hold before deletion. Only the contract and the manifest are built in
  Release 1; deletion stays a manual, audited operation.
- **D-P6 Audit search and export.** Premodule 4 adds search (filters on category, actor, action, outcome, time, company) and export as CSV.
  Each exported row carries the chain position and the head hash, so a receiver can verify the export. The viewer sees records; it does
  not edit them (FR-21.7).
- **D-P7 Notifications.** Release 1 keeps the identity email sender abstraction. A general notification service is Release 2.
- **D-P8 Files and avatars.** The avatar is a file in Release 2, so premodule 3 has no avatar upload. The `AvatarUrl` field is not shown
  until then.
- **D-P9 Workflow for role and lock changes.** Release 1 applies role and lock changes directly, audited. An approval chain for Critical
  role changes is a Release 2 option that uses the approval workflow already built.
- **D-P10 Entitlements and quotas.** Out of Release 1. Not part of any premodule.
- **D-P11 AI Platform and DevOps.** Out of scope, as the PRD states.
- **D-P12 Concurrency.** Admin writes use the user's concurrency stamp: the detail response carries an `ETag`, and a write with `If-Match`
  that does not match returns `412`. The framework already supports ETag and `If-Match` for REST responses. Role and profile writes follow the same rule.
- **D-P13 Idempotency.** Role-permission, lock and unlock writes are set operations, so repeating one gives the same result. They do not
  need an idempotency key. Creating a role and inviting a user do need one, and use the framework's idempotency middleware.
- **D-P14 Errors.** Every route answers with the framework's error catalog, so a refusal has the same code everywhere.
- **D-P15 Admin routes carry no business data.** Identity and tenant administration routes return account and company data only. Business
  data stays behind its own module's permissions (FR-26.8).
- **D-P16 Upgrade scripts.** Each new table (roles metadata, grants seed record, profile fields if stored separately) gets an upgrade
  script for PostgreSQL, SQL Server and MySQL, and the upgrade is run twice on each engine, as before.

### Release mapping

- **Release 1 (foundation):** premodule 1 (accounts, roles, permissions, field guard, audited writes, sessions), premodule 2 (company
  status, seats, membership, offboarding contract), premodule 5 (settings with branch scope and read-only pins), premodule 4 (audit search
  and export), premodule 3 (profile fields: name, language, time zone).
- **Release 2 (reusable capabilities):** notifications, avatars and files, approval chain for Critical role changes, administration screens
  for flags and entitlements.
- **Later (P2):** entitlements and quotas, module distribution.

### Acceptance (PRD §8) for the premodules

- **Authorization:** each admin route refuses a caller without the permission (`401` anonymous, `403` without the permission), also when
  called directly. Tests exist for premodule 1 and must exist for each new route.
- **Tenant isolation:** each premodule has a company-isolation test over HTTP (as in premodule 1) and a membership test.
- **Data integrity:** stale writes return `412`; repeated set operations give the same result.
- **Audit:** every write is in the security audit with actor, target and outcome. The chain verifies after the test run.
- **Security:** personal fields are masked for callers without the clearance, in list and detail responses, and in the export.
- **Upgrades:** each new table has scripts for all three engines, verified twice per engine.
- **Developer experience:** a generated application shows the Users page after the first administrator is granted through
  `Identity:Seed:Grants`, and the generated tests cover the new routes.

Validation: the PRD's two acceptance applications are the test beds. The identity-account administration module is the pilot for the
garments ERP case (approvals and complex roles come later); the food delivery case uses the public catalog and idempotency parts of
the framework and is not part of these premodules.

## Remaining work and limitations (2026-10-24)

This section is the single list of what is still open after the premodule 1 to 5 work. Each item says what blocks it.

### Owner or operator (cannot be closed in the repository)

- **Legal retention minimum (D8):** a legal decision. No value is set.
- **D7 pilot sign-off:** the owner's sign-off.
- **OpenBao production evidence:** an operator runs the production checks and provides the evidence.
- **AI platform conformance:** the real platform's suite must run against the connector before activation. The in-repo fake platform tests only our side.
- **Publishing packages:** the owner publishes manually. Packages are built on request, with the new version.
- **Built-in administrator role (premodule 1):** which role counts as built in, so its removal can be refused.
- **Profile field list (premodule 3):** the owner confirms the fields before premodule 3 starts.
- **Audit permission default (premodule 4):** whether `audit:security:view` is granted to any role by default. Currently none.
- **Parent-administrator authority (premodule 2):** who in a parent company may add members or grants in a child, and under which rule.
  Not built; the access rule for reads is built.

### Decisions already taken, to confirm in review

- Parent grants apply below their holder; a deny anywhere on the path wins (the owner confirmed these defaults).
- Role administration gate (D-P17): role list, create, rename and delete need `identity:accounts:manage`; role permissions need `authorization:manage`.
- Personal-data permission and the audit permission are granted to no role by default.
- The company `Admin` grant is written into the default company at startup and into companies the tests create, not into companies created later
  through the API (see the gaps below).

### Buildable gaps

- **Companies page in web apps:** needs a company switcher, because `--multi-tenancy` is refused for web apps.
- **Admin grant for new companies:** companies created through the company API start with no grants. Only the default company gets `Admin` at
  startup. The fix is to seed the same grant when a company is created.
- **Business audit log search (premodule 4):** the viewer covers the security chain only.
- **Premodule 5:** the branch (organization-unit) scope from D-P4, grouping settings by definition group (definitions carry no group field), and
  an EF settings store for generated apps (values are in memory until one is added).
- **Browser checks:** the generated Users, Roles, Audit, Settings and Companies pages were verified by build and API tests only.

### Limitations in the built code

- **Upgrade scripts:** the `ParentId` column is in the three tenancy scripts, but no automated test checks them against the EF model.
- **Per-request grant memo:** if a tenant changes inside one scope, the per-request memo can reuse the earlier company's grants. The shared
  grant cache keys by company, so it is safe.
- **Admin listings are own-rows for writes:** the grant listings show the company's own rows for editing, and a separate read shows inherited
  rows. A listing does not merge them.
- **Refresher:** the delegation role refresher runs in the host context, so it sees every company. This is correct, not a gap.

### Blazor test stability

- **Teardown race:** fixed in practice. All component test classes derive from one base class that awaits asynchronous teardown. Ten consecutive
  full runs passed after the change.
- **Acceptance gap:** the owner requires a regression test for a confirmed cause. The cause is inferred from stack traces, not confirmed, so no
  such test exists yet.
- **Row-rule test:** it waits for each render before reading the grid. It passed in the last ten full runs.

### Repository state (2026-10-24)

- **Framework (`modulus`):** `main` is at `8e288c6` on origin. The work of this round is uncommitted until it is committed.
- **UI (`modulus-ui`):** the Blazor test work is on `origin/blazor-test-stability` (`954ab84`). Local `main` is 5 commits ahead and 3 behind
  `origin/main`, because the remote kept a separate 0.9.1 line. The version line is an open decision.

### Verification of this round

- Affected unit suites pass on net8 and net10: authorization (122 and 7 new grant tests), management (136), platform (558), multi-tenancy (128),
  identity (450) and CLI (636).
- Generated single-tenant app: builds, 15 of 15 tests pass, on the framework at `1.6.0-local.11`.
- Generated multi-tenant app: builds, 11 of 11 tests pass, on the same framework.

## Ordering

The order follows dependencies: tenant management and identity are needed by everything; audit is needed to show evidence for the others.

1. **Identity and auth (admin)**: user list, detail, roles, invitations and lock-out, on top of the existing account lifecycle.
2. **Tenant management**: company list and detail, status changes, membership and seats, on top of `TenantManager`.
3. **Profile (self-service)**: read and update the signed-in person's own profile fields.
4. **Audit viewer**: query and export the security audit and the business audit log, with the chain verification result.
5. **Settings administration**: screens for the registered settings, scope by scope, with the pins shown as read-only.

## Per premodule

### 1. Identity and auth

**Status (2026-10-23): API built and tested; screens and CLI not built.** `UserAdminExtensions.MapModulusUserAdmin<TUser>()` in
`Modulus.Identity` serves `GET /identity/users` (paged, searchable), `GET /{id}`, `PUT /{id}/roles` (refuses `integration:` roles),
`POST /{id}/lock` (reason and future end required) and `POST /{id}/unlock`. Every route needs `identity:accounts:manage`. The actor is
the authenticated principal's subject, not `ICurrentUser`, because the test host's current user carries no id. Tests:
`UserAdminHttpTests` (3, both targets): anonymous `401`, password-only step-up `401`, and the authorized path with a second factor
(list, read, roles, lock, unlock, `404`, the audit events). The identity suite passes (430 on each target).
Company isolation (pilot scenario 1) is tested on the data layer: `UserAdminCompanyIsolationTests` (2, both targets) shows a
company's user manager sees only its own users and not another company's user, and the host sees all. The identity suite passes
(432 on each target).
HTTP isolation (pilot scenario 1, over the routes): `UserAdminHttpTests` now includes a test where a request names its company
(`X-Test-Company`, a test stand-in for company resolution): the list, the detail and the role change show or change only that company's
users (another company's user answers `404`), and the host sees every company. Membership gating is not part of this test; it is covered
by the multi-tenancy tests. The identity suite passes (433 on each target).
Membership (pilot scenario 1, enforced): `UserAdminMembershipTests` runs the routes behind the real tenant middleware
(`UseMultiTenancy`, header resolver, `RequireMembership`). A member of company A lists A's users; the same caller asking for company B
gets `403`. The company store is a test store with two companies. The identity suite passes (434 on each target).
Two test-host notes: the base host registers a null company before the test's configuration runs, so the test replaces the tenant
store and the admin's two-factor setup runs in the host context. Neither changes production registration.
Screens (built 2026-10-23, in the generated web host rather than in `modulus-ui`): a Users page at `/Admin/Users` (search, open a
user, save roles, lock with a reason and optional hours, unlock). The page calls the API through the BFF (`UserAdminApiClient`); the API
routes sit under `/api/identity/users` so the BFF forwards them. A Users entry in the server menu is shown only to people with
`identity:accounts:manage`. Verified on a generated app: the admin's page lists users and a saved role change answers "Roles saved.";
without a grant the same admin gets the permission message and no menu entry. The page lives in the generated application because
that is where the other Web pages are delivered; the `modulus-ui` package is not changed for it.
Not yet, and an owner decision: the permission is granted to no role by default (owner record, 2026-10-23), and generated apps do not
map the authorization management API (`MapModulusAuthorizationManagement`), so no one can grant it in a generated app yet. Choose one:
(a) map the management API in generated apps (exposes grant administration to holders of `authorization:manage`), or (b) grant through
seed data or a deployment step. `modulus add-identity-admin` is not needed: the pages are part of the identity backend.

- **API (framework):** `GET /api/identity/users` (paged, filter by status, role, company), `GET /api/identity/users/{id}`,
  `PUT /api/identity/users/{id}/roles`, `POST /api/identity/users/{id}/lock` and `/unlock`, `POST /api/identity/invitations` (the
  existing `IUserInvitationService`). Permission `identity:accounts:manage` (Critical; already defined). Every write is audited.
- **Screens (modulus-ui):** user list with search, user detail (status, roles, sessions, passkeys, two-factor state, login history),
  invite form, lock and unlock actions with a reason.
- **CLI:** `modulus app` already wires the sign-in pages; add `modulus add-identity-admin` for generated admin pages.
- **Tests:** the pilot suite in `docs/IDENTITY_PLATFORM_PLAN.md` §14.2 (scenarios 1 to 3) runs against these endpoints. Anonymous
  `401`, signed in without the permission `403`, cross-company `403`, step-up required for Critical operations.
- **Size (estimate):** 3 to 4 weeks including screens and tests.

### 1a. Review (2026-10-23): permission, role and field management for premodule 1

The first version of premodule 1 covers accounts only. Review against what the framework already has, and what premodule 1 must add:

| Capability | Already in the framework | Gap for premodule 1 | Proposal |
|---|---|---|---|
| **Permission catalog** | `IPermissionRegistry` holds every registered permission with its sensitivity; the modules register theirs | No read endpoint: an administrator cannot see what can be granted | `GET /api/identity/permissions` (or under the authorization admin prefix): name, description, module, sensitivity. Read-only. Needs `authorization:manage` or a narrower read permission (decide) |
| **Role list and management** | `RoleManager<ModulusRole>` (create, rename, delete); reserved `integration:` roles refused on assignment; role inclusions (`/authorization/roles/inclusions`) | No HTTP routes to list, create, rename or delete roles; no role description | `GET|POST /api/identity/roles`, `PUT|DELETE /api/identity/roles/{name}`. Refuse the reserved prefix and the built-in administrator role's removal. Audit every change |
| **Role permissions** | Grants by role: `GET|POST|DELETE /authorization/grants` (holder type `Role`), Allow and Deny, SoD check, audit; scoped grants | Not shown per role in one place; the Users page has no role-permission view | `GET /api/identity/roles/{name}/permissions` returns the grants for the role (through the existing grant store). Changes stay on the grants API, so one code path makes them |
| **User effective access** | `GET /authorization/effective-access` (permission level, per user) | Not on the Users page | Show the user's effective permissions on the detail page, read-only |
| **Field guard (field-level security)** | `FieldSecurityProfile`, `FieldOutputGuard`, `IFieldAuthorizer`, `IFieldSecurityRegistry` (platform): clearance by classification and by field | Not applied to the user list or detail, so personal data (email, names, phone) is shown to anyone with `identity:accounts:manage` | Apply the field guard to the user responses: personal fields need a field clearance, otherwise masked. Clearances are data, managed through the field security registry. Decide which fields are personal (proposal: email, first and last name, phone, employee code) |
| **Role-permission escalation** | Grants are capped by the grantor's own authority (`authorization:grant-any` lifts it); Critical permissions are not delegable | Role creation must not let an administrator build a role stronger than their own | Role-permission changes go through the grant cap. An administrator cannot grant a permission they do not hold, unless they hold `authorization:grant-any` |

Consequences for the open decision on granting `identity:accounts:manage` (owner record, 2026-10-23): the grant has to be made by
someone, and the role management above is the place to make it. Two options, both needing the owner:
(a) map the authorization management API in generated apps for holders of `authorization:manage`, so the Admin role can grant the
    permission to a role; or (b) keep the API unmapped and grant through seed data or a deployment step.
Proposal: (a), limited to the role-permission routes under `/api/identity/roles`, which call the grant store with the cap above, rather
than mapping the whole authorization API.

Acceptance for 1a: a role can be created and given `identity:accounts:manage` through the API by a holder of `authorization:grant-any`
only; an administrator without it cannot grant what they do not hold (`403`); personal fields are masked for a caller without the
field clearance and shown with it; every change is in the security audit; the permission catalog lists every registered permission.

### 2. Tenant management
Parent grants apply below their holder (built 2026-10-24, the defaults confirmed by the owner). `TenantInfo.ParentId` and `TenantInfo.Lineage`
carry the company's line of ancestors, set by the tenant store when a company is looked up (bounded, failing closed at a missing, inactive
or cyclic company). `ICurrentTenant.Lineage` exposes it to the authorization store, which reads grants, scoped grants and delegations in the
company and above it, on the synchronous and the asynchronous read alike. Precedence is the checker's existing rule: a deny anywhere in the chain
wins, so a parent deny always applies below it and a child allow cannot override it. Tests: `ParentGrantTests` (6, including sync and async
agreement and an inherited delegation), `TenantLineageStoreTests` (2). The authorization, management, platform, multi-tenancy, identity and CLI
suites pass on both targets. The generated single-tenant app (15 tests) and multi-tenant app (11 tests) build and pass on the framework at
local.10. The shared grant cache keys by company, so it is safe. Delegations are read in force through the company's lineage; the admin listing
and the role refresher still read the company's own rows.

Parent and child companies (2026-10-24, option 2 of the access model): a company has an optional parent (`TenantEntity.ParentId`).
`TenantManager.SetParentAsync` refuses a self-link, a missing parent and a link that would close a cycle (walk depth capped at 64), and audits
`tenant.parent-changed`. `PUT /api/tenancy/tenants/{id}/parent` exposes it (host context). The three tenancy upgrade scripts add the column.
Tests: `TenantAdminEndpointTests` (5, both targets). Access does not follow the link for permissions: a parent's administrators reach a child only through the
child's own grants. Membership follows the link (built 2026-10-24): a membership of a company counts for every company below it, so a member of a parent may
enter a child; a member of a child does not reach its parent; every company on the path up must be active. The selectable list includes the
descendants. Tests: `ParentMembershipTests` (3), tenancy suite 126 of 126 on both targets. What a parent member may do in a child is still decided
by the child's own grants, so a parent member without a child grant is refused there (`403`). Not built yet: granting a parent-level permission
that applies in the children (the subtree grant), the rule that lets a parent administrator manage a child's memberships and grants, and the
Companies page. The generated multi-tenant app builds and passes its 11 tests on the framework at local.8.

Progress (2026-10-24): generated multi-tenant API apps now map the company routes (`AddModulusTenantAdmin`, `MapModulusTenantAdmin`, a
Companies menu entry for `tenants:manage`). Not built: the Companies page. `--multi-tenancy` is refused for web apps (no company switcher), so a
page would have no host to live in; the switcher is the prerequisite. A regression found while testing: since commit b92876b (2026-09-22) the
EF grant store shows a company only the grants with its own tenant id, so grants seeded at host level (the `Admin` role's permissions, the first
administrator's grant) are invisible inside a company and every company request is refused (`403`). The generated multi-tenant app's round trip
and permission tests fail for this reason. Decision needed: seed the role grants per company (the B4 model), or let host grants apply to every
company (a change to a security filter). Until then the multi-tenant generated app fails two of its tests.


Progress (2026-10-23): the company administration API is built in `Modulus.MultiTenancy.EntityFrameworkCore` (`MapModulusTenantAdmin<TTenant,TMembership,TDataStore>()`,
`AddModulusTenantAdmin()` for `tenants:manage`, Critical). It lists (paged, searchable), creates (duplicate slug `409`, invalid slug `400`), reads,
changes the status (`Trial` needs an end date), the seat limit, and manages members (seat limit `409` `SEAT_LIMIT`; revoked members stay listed as
inactive). Every route runs in host context. Tests: `TenantAdminEndpointTests` (4, both targets). Not built yet: the company screens on the Users
side of the generated web app, and the navigation item.

- **API:** `GET /api/tenants` (paged), `POST /api/tenants`, `GET /api/tenants/{id}`, `PUT /api/tenants/{id}/status` (Trial, Active,
  Suspended, Closed), `PUT /api/tenants/{id}/seats`, `GET|POST|DELETE /api/tenants/{id}/members`. Permission `tenants:manage` (new, Critical;
  not defined yet), with host context only. `TenantManager` already enforces the rules (suspended is read-only, closed does not resolve,
  seats are checked).
- **Screens:** company list, company detail (status with reason, trial end, seats used of the limit, members), status change with
  confirmation.
- **Not in scope:** storage quotas, offboarding export, retention per company (the plan lists them as not built).
- **Tests:** a suspended company answers `423` to writes; a closed company does not resolve; seat limit refuses the 9th active member
  by `409`; a company administrator cannot see another company.
- **Size (estimate):** 2 to 3 weeks.

### 3. Profile (self-service)

- **API:** `GET /api/account/profile`, `PUT /api/account/profile` for fields the owner chooses (display name, language, time zone,
  contact phone). Fields are stored on the identity user; email changes need confirmation by a one-time link (`IIdentityEmailSender`).
  No permission beyond being signed in; a person can change only their own profile.
- **Screens:** profile page (name, language, time zone, contact), change-email confirmation page, linked to the existing sessions and
  passkeys pages.
- **Decision needed:** the exact field list (see open decisions).
- **Tests:** a person reads and updates only their own profile; email change needs the link; a changed time zone shows in the next page.
- **Size (estimate):** 1 to 2 weeks.

### 4. Audit viewer

Progress (2026-10-24): built in `Modulus.Platform` (`MapModulusSecurityAuditViewer`, `AddModulusSecurityAuditViewer`, permission
`audit:security:view`, Sensitive). Search over the caller's company's chain (the host chain from the host context), chain verification,
and CSV export. Each exported row carries the chain position and the verification with the head sequence and hash (D-P6). The export
itself is audited with its row count. The export is capped at the store's 1,000 rows per query. Tests: `SecurityAuditViewerTests` (3, both
targets). In generated apps with the security guard: the API routes, a menu entry for holders of `audit:security:view`, and an Audit page.
Verified: a fresh generated `webapp+api` app builds and passes its 15 tests. The permission is granted to no role by default; an operator
grants it (the same rule as the account permissions). Not built: business audit log search (the page reads the security chain only), and a
company-level export that spans several chains.

### 4. Audit viewer

- **API:** `GET /api/audit/security` (filter by category, actor, action prefix, outcome, time window, company; paged, newest first),
  `GET /api/audit/business` (filter by entity, actor, time), `GET /api/audit/chain/{companyId}/verify` (runs
  `VerifyChainAsync` and reports the head and the anchor result), `GET /api/audit/export` (CSV of a filtered window, one row per event,
  with the chain position). Permission `audit:view` (exists, used by the AI audit capabilities) for the own company; `audit:view-host` (new, Critical, not defined yet) for the host chain.
- **Screens:** audit list with filters, event detail (no secrets or user names beyond what the record holds), chain status card.
- **Constraints from the framework:** the security audit never stores passwords, tokens or SQL text (already enforced); the viewer
  shows what is stored and does not add fields.
- **Tests:** a tampered row fails verification; a viewer without `audit:view` gets `403`; export is limited to the caller's company;
  paging is stable under concurrent writes.
- **Size (estimate):** 2 to 3 weeks.

### 5. Settings administration

Progress (2026-10-24): the API was already built and tested (`SettingsAdministrationEndpoints`: list with source and pins, writes per scope,
global writes behind `settings:global:manage`, refusals for unknown names and bad values). Generated apps now register it (`AddModulusSettings`,
`AddModulusSettingAdministration`, `MapModulusSettingAdministration`) and ship a Settings page: effective value and source per setting, pinned
values read-only, an edit control by value type (checkbox-style choice for booleans, a choice list for allowed values, text otherwise), and the
API's refusal message on the field. Verified: a fresh generated `webapp+api` app builds and passes its 15 tests; the page was not driven in a
browser. Not built: the branch scope (D-P4, an organization-unit scope between user and company), grouping by definition group (the
definitions carry no group field yet), and an EF settings store for generated apps (values are in memory until the tenant store is added).

- **API:** exists (`/api/settings`). Add `GET /api/settings/definitions` shape review so the screen can show the type, allowed values and
  allowed scopes; no new write paths.
- **Screens:** settings list grouped by definition group, with the effective value, its source (pinned, user, company, global, default)
  and the pinned ones read-only. Edit controls follow the value type (text, boolean, number, choice, duration, GUID).
- **Tests:** a pinned setting refuses writes (`409`) and shows read-only; a company administrator cannot change a global value (`403`);
  a value that does not parse is refused (`400`) with the message shown on the field.
- **Size (estimate):** 1 to 2 weeks.

## Cross-cutting requirements (every premodule)

- Permissions are registered in the framework's registry; a route without a mapping fails closed.
- Every write is recorded in the security audit with actor, company and outcome.
- Each screen declares its navigation item through `AddNavigationContributor` so the server menu shows it only to people with the permission.
- Generated application tests cover: anonymous `401`, unauthorized `403`, cross-company `403`, and the happy path.
- Documentation: a section in `docs/DOCUMENTATION.md` and a page in `docs/runbooks/` for each premodule's operator tasks.
- No application-specific code in the premodules; the sample under `samples/` is not used.

## Dependencies on decisions and other work

- **Pilot module** (decided 2026-10-23): identity-account administration, so premodule 1 is the pilot scope.
  Premodule 1 is therefore the first to build and the first to test against the pilot suite.
- **Profile fields** (open): the owner must confirm the field list before premodule 3 starts.
- **Blazor test stability** (open): screens are tested with bUnit; the suite must be stable before the premodules add tests.
  See the investigation notes below.
- **Publishing:** the premodules ship in the UI packages (`modulus-ui`) and the framework packages (`modulus`); both are published manually.

## Blazor test stability: investigation notes (2026-10-23)

- Reproduced: `Modulus.Blazor.Tests` fails in about half of full runs (1 to 4 tests per run). The failing tests all fail during
  `BunitContext` teardown, in `DomEventListener.DisposeShared` → `DomEventSubscriptionStore.FindDomEventSubscription`, with
  "Collection was modified".
- Source checked (decompiled `AntDesign` 1.6.2, `DomEventService` and `DomEventListener`): the store is **per scope**, not static (the
  earlier comment in `TestCollections.cs` says otherwise and is wrong). Each store holds plain `List<DomEventSubscription>` values,
  read and changed without a lock. An error needs another thread to change a list during the read.
- `EditableGridTests` alone fails 4 of 8 runs; `ModulusDataGridTests` alone passes 8 of 8. The trigger is inside the class.
- Ruled out: `ColumnHeader` and `ColumnValueList` delayed focus calls (adding cancellation on dispose did not change the failure rate);
  `AppLookup` already cancels on dispose.
- Not yet found: the second thread. Next step: capture the thread id in the failing test's teardown and the thread that last changed
  the list (a test-only subscriber, or a logging decorator on `IDomEventListener`).
- Acceptance (owner record): ten consecutive clean full runs, plus a regression test for the confirmed cause.

## Open decisions for the owner

1. Pilot module: **decided 2026-10-23** as identity-account administration, the framework's own module (Inventory is a later candidate).
2. Profile fields for premodule 3 (name, language, time zone, contact phone: which ones, and which need confirmation).
3. Audit export format and maximum window (premodule 4).
4. Whether company administrators may see their own audit chain's raw events or only the decoded summary (premodule 4).
5. Confirm the order above, or choose another.
