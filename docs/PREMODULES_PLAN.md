# Premodules plan: identity and auth, audit, settings, profile, tenant management

## Resume here (state at 2026-10-24)

Branch `company-provisioning-baseline`. This round's work is committed in the commit that introduced this section (see `git log`). Start the next session from that commit.

Done in this round (built, unit-tested, and the generated `webapp+api` app builds with 0 warnings and passes its tests, with and without `--multi-tenancy`):

- Profile (decision 8), company switcher (29), grouped settings (28), companies admin page with members and parent, business audit page, company audit summary (decision 7), branch picker (decision 14). The branch picker is a cookie, the gateway sends `X-Branch-Id`, and the server checks scope.
- Generator fixes: Settings page braces, unregistered Audit/Roles/BusinessAudit/CompanyAudit clients, a 403-specific message on CRUD writes, `--multi-tenancy` help text, and the round-trip test (chooses a company, opens every admin page).
- Decision register in the "Decision register" section of this file. Decisions 2, 4, 5, 8 are built; 7 is built with the test gaps listed in its row.

Status of the remaining list (2026-10-24, second pass):

1. **Decision 6, parent-to-child authority:** built (see the register row 6).
2. **Decision 13, Blazor teardown race:** not confirmed. Findings: the `modulus-ui` suite did not compile on `main` (`Xunit.TestContext.Current` in `SampleAdminDataTests`, an xUnit 3 API in an xUnit 2 project); fixed. With the async teardown base class, ten consecutive full runs passed (838 each). With synchronous teardown (the experiment, reverted), 3 of 6 full runs failed with the same "Collection was modified" stack. Removing the delayed focus in `ColumnHeader` and `ColumnValueList` did not stop it (2 of 6 failed), so that delay is not the cause. The second thread is still not identified, so the owner's regression-test rule stays open.
3. **Browser checks:** done for the generated multi-company web host, in Chromium: sign-in, then all ten admin pages return 200 with the expected heading and no console errors after the fix below. Not done: interactive flows (saving a setting, choosing a branch, creating a company) in the browser.
4. **Branch option on the Settings page:** built in the generated web host (multi-company apps only): a "Current branch" scope choice, and a note when no branch is chosen. The API already checks branch scope. Tests: `The_settings_page_offers_a_branch_scope...` (net10). The Blazor host equivalent is not built: the CLI generates only the Razor Pages and MVC hosts, and the Blazor host lives in `modulus-ui`.
5. **Owner and operator items:** no repository work until the owners provide their inputs (unchanged, see the register).

Defects found and fixed in this pass:

- **Generated multi-company API host crashed in Development** (`ISecretWriter` missing for `TenantDataStoreProvisioner`, caught by build-time validation). The provisioner is now built lazily; the failure moves to the first provisioning call. Tests: `TenantStoreValidationTests` (2).
- **Generated pages were unstyled.** `modulus-ui`'s asset project set `StaticWebAssetBasePath` to the pre-rename `_content/Modulus.Ui.Assets`, while the head helpers and Blazor head used the `Synvro.*` name. Fixed in `modulus-ui` (source and csproj). Verified with locally packed `1.6.1-local.fix2` packages. The packages are not published.

Still open (not decided in this repo):

- **Resolved (owner decision, 2026-10-24): first administrator.** The first administrator of a new deployment is made the `TenantOwner` role
  (`IdentitySeeding`, audited as `identity.tenant-owner.granted`). The owner role holds `identity:accounts:manage` and `identity:accounts:personal-data`:
  in each company through `CompanyProvisioning:OwnerPermissions` (Critical is allowed for this list only; the default company gets it on startup),
  and at host level through the first-boot grants. A normal administrator holds neither. Only an owner grants or revokes the role (`TENANT_OWNER_REQUIRED`),
  and the last active owner cannot be demoted (`LAST_TENANT_OWNER`). Recovery: `Identity:Seed:TenantOwnerEmail` names an existing account that becomes
  the owner when no active owner exists (audited). Verified on a regenerated multi-company app: the first administrator gets `200` on the Users API,
  where it had `403` before. Tests: `UserAdminHttpTests` (2 new), `IdentityBackendTests`, `IdentityAdminPermissionTests`, `CompanyProvisionerTests` (3 new).
  The last active owner is also refused a lock, a disable and an archive (`LAST_TENANT_OWNER`, `UserAdminHttpTests`). A multi-company
  person in exactly one company acts in it without choosing (`DefaultCompanyMiddleware`), so the first Users page opens without a
  company-header 403. Browser check on a regenerated app: the Users page has no permission error and no console errors.
  Not covered: an owner-transfer workflow beyond the role change, and a platform administrator's company creation (the owner is refused, by design).
- Resolved (2026-10-24): the CLI dropped the UI package references when `MODULUS_UI_VERSION` was set. Cause: the UI templates package was
  looked up only in `MODULUS_LOCAL_FEED`, not in `--package-source`. `modulus app --package-source` now serves that lookup too.
- Fixed (2026-10-24): the company short-name field's `pattern` attribute was an invalid regular expression in Chrome (`[a-z0-9-]` needs an escaped
  hyphen under the `v` flag), so the browser blocked the form. Test: `The_short_name_pattern_is_a_valid_regular_expression_in_browsers`.

Known gaps to keep in view:

- The identity directory test covers the lookup only; the identity side of the company audit summary is not exercised end to end in a browser.
- The branch picker is not covered by a generated-app round trip: no org unit is created by the generated tests.
- The `1.6.0-dev.local` and `1.6.1-dev.local` packages used for verification are local packs in the scratch folder, not published. Clear `~/.nuget/packages/synvro.modulus.*/1.6.0-dev.local` before a re-pack, or a stale copy is restored.

Status: **in progress** (plan 2026-10-23; state in "Resume here" above). Each premodule is a framework module that a generated application registers
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

The built-in administrator role's removal refusal is built (`Admin`, owner decision 4): delete and rename answer `409 BUILT_IN_ROLE`.

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

## Decisions recorded (2026-10-24)

The owner's final choices. Items marked *unverified* need a check before they are relied on.

- **D8 legal retention:** no statutory minimum is set. Do not enforce a minimum until counsel confirms one, and do not delete audit records
  freely. Keep a documented interim configuration; no irreversible deletion until a retention policy is approved.
  Built (2026-10-24): security audit retention is off by default (`Security:Retention:Enabled` = false). An operator turns it on with the
  period the legal advice sets. Test: `Retention_is_off_until_an_operator_turns_it_on`.
- **D7 pilot:** write measurable acceptance criteria for identity-account administration (role protection, authorization boundaries,
  account lifecycle, auditability, regression tests), then sign off after they pass. Proposed criteria are in "Pilot acceptance criteria
  (proposed)" below; they are not signed off.
- **Built-in administrator role:** `Admin` is protected from deletion and rename. Built: `BUILT_IN_ROLE` refusal in `RoleAdminEndpoints`.
- **D-01 `TenantAdministrator`:** map to `Admin`, migrate existing assignments without losing grants, mark `Admin` protected. Built
  (2026-10-24): the rehire approver default is `Admin` and `Admin` is protected. An installation that still has `TenantAdministrator` follows
  the upgrade steps in `docs/IDENTITY_PLATFORM_PLAN.md` (A1-1); the delete route refuses while holders or grants remain.
- **Profile fields:** display name, language, time zone, contact phone. Email changes need a confirmation link.
- **Audit permission:** `audit:security:view` goes to no role by default; an authorized operator grants it.
- **Audit export:** CSV, 31-day maximum window, the 1,000-row query cap kept; exports are authorized and audited.
- **Company-admin audit visibility:** decoded summary only. Raw host-level events stay with authorized host operators.
- **D-03 parent administration:** a parent administrator cannot change a child company without a grant in that child.
- **D-05 audit retention:** configured per category; a valid legal hold suspends deletion. Deletion is built only after the policy is approved.
  Built: per-category windows and per-chain legal holds already existed; the default is now off (see D8).
- **Premodule order:** identity, tenancy, profile, audit, settings.
- **Owner records D-02, D-06 to D-09, B-02 to B-05:** unverified until the original decisions and code are checked.
- **OpenBao:** Podman on Linux with AppRole; three independent Raft nodes for production (one node only for development or a temporary
  pilot); Shamir seal with 5 shares and a threshold of 3, each held by a different named custodian (no KMS, no auto-unseal, decided 2026-10-25); migrate all listed secrets (database and
  tenant credentials, payment and API keys, webhook secrets, LDAP and SAML bind credentials, AI platform keys, Data Protection keys,
  personal-data search keys, log-redaction keys); static KV secrets with scheduled rotation; cache reads for at most 5 minutes and fail
  closed for writes and new-tenant provisioning (verified 2026-10-24: `Secrets:Ttl` and `Secrets:MaxStaleOnFailure` both default to 5 minutes;
  the stale bound was 15 minutes and is now 5, test `The_default_maximum_age_of_a_cached_secret_is_five_minutes`; the runbook is
  `docs/runbooks/openbao-production.md`); shared database by default with tenant scope
  in the application and PostgreSQL row-level security where it applies; SSO/OIDC with MFA for administrators and a separate break-glass
  account; a separate protected audit destination; production separate from development and staging; no Python client until a Python service
  needs one; database passwords every 90 days, API and payment keys every 90 days or sooner, AppRole Secret IDs short-lived and per deployment.
- **Settings grouping:** approved. Add a group field to `SettingDefinition`.
- **Company switcher:** build now. Membership, permissions and data scope are checked on every request, never implied by selection.
- **Baseline re-application:** approved and built (2026-10-24). Endpoint, identity check, audit and the no-overwrite rule as described under
  "Admin grant for new companies (done, D-10)" in the remaining-work section.
- **Offboarding export (D-P5):** deferred from Release 1. The contract and manifest stay; the export and retention hold come later unless a
  confirmed requirement makes them a blocker.
- **`audit:view-host`:** a Critical permission, granted to no role by default, kept separate from company audit permissions.
- **`ISettingStore` breaking change:** accepted. Branch id stays in every method; in-repo implementations and tests updated (done); external
  implementations need a documented migration.

## Pilot acceptance criteria (proposed, not signed off)

Pilot: identity-account administration (decision D7). Sign-off needs every item below to be met, with the test named or a recorded result.

1. **Role protection.** `Admin` cannot be deleted or renamed, with or without holders (`The_built_in_admin_role_is_never_deleted_or_renamed...`).
   A role with holders or grants is refused for delete and rename (`A_role_is_not_deleted_...`, `A_role_is_not_renamed_...`).
2. **Authorization boundaries.** A caller without `identity:accounts:manage` cannot change roles (`A_signed_in_account_without_the_account_permission...`).
   A company administrator cannot change a global value (403). A parent administrator cannot change a child company without a grant there.
3. **Account lifecycle.** Invite, activate, disable, archive and rehire-review work end to end, and a rehire needs an approver with `Admin` or
   `HR` in the account's own company (`RehireReviewTests`).
4. **Auditability.** Every administrative write records an audit event, and the audit viewer needs `audit:security:view` or `audit:view`
   (`SecurityAuditViewerTests`, `BusinessAuditViewerTests`).
5. **Regression.** The identity, authorization and platform unit suites pass on net8.0 and net10.0, the build has 0 warnings, and a generated
   API app's test suite passes.
6. **Operator checks.** Audit retention is off unless an operator enables it, and the enabled period is recorded in the deployment's runbook.

## Status of the decided items (2026-10-24, after the Phase 2 and 3 pass)

- **Decision 10 (audit export): built.** The export needs both `since` and `until`, covers at most 31 days, and keeps the 1,000-row cap.
  Tests: `SecurityAuditViewerTests` (5).
- **Decision 11 (company-admin audit visibility): open.** The company viewer still returns each event's stored details. Decoding them into a
  summary needs the field list decided first.
- **Decision 12 (parent administration, D-03): satisfied by default, explicit path not built.** The company administration routes run in host
  context under `tenants:manage`, so no parent administrator can write to a child through them. An explicit "parent admin with a grant in the
  child" rule is not built; it needs the company switcher.
- **Decision 28 (settings groups): data built.** `SettingDefinition.Group` is carried to the admin view (`SettingView.Group`). The grouped
  screen is not built. Test: `A_definition_group_is_carried_to_the_administrator_view`.
- **Decision 32 (`audit:view-host`): built.** Critical, granted to no role. Host-context reads of the security audit need it; a company's own
  chain does not. Tests: `The_host_chain_needs_the_host_view_permission_and_a_company_chain_does_not`.
- **Decision 30 (baseline re-application): built.** See "Admin grant for new companies (done, D-10)".
- **Correction to the note in section 2 about grant visibility.** The "regression" described there (host-level grants invisible inside a
  company) was addressed by the per-company baseline grants of D-10. The generated multi-tenant app passed 11 of 11 on 2026-10-24.
- **Decision 8 (profile): API built, screens not built.** `GET` and `PUT account/profile` (display name, language as a BCP 47 tag, time zone
  as an IANA or Windows id, contact phone on the built-in `PhoneNumber`; a changed phone is unconfirmed). The address changes through
  `POST account/profile/email`, which mails a one-time link to the new address, and `POST account/profile/email/confirm`, which completes it
  with the token. A taken address gets the same answer and no mail. The change-email mail is sent inline, not through the queue (the queue
  mails only stored addresses), so the request is slower for a taken address; the answer is identical. Upgrade: the columns `DisplayName`,
  `Language` and `TimeZone` on the user table need a migration. Tests: `ProfileTests` (10 cases, including the three invalid-value rows), net8.0 and net10.0.
  Not built: the profile page and the change-email confirmation page in the generated web host.
- **Decision 29 (company switcher): built in the API and the generated web host.** `GET account/companies` (`MapModulusCompanySwitcher`) lists the
  caller's active memberships (closed companies left out, suspended ones listed read-only) and marks the company in the request. The list is a
  convenience: each request is still checked against membership by `TenantMiddleware`. Tests: `CompanySwitcherTests` (4), net8.0 and net10.0.
  The generated Razor host has a company page (`/company`, multi-tenancy only): it sets a `modulus.company` cookie that `BffGatewayHandler` turns into
  `X-Tenant-Id`, and accepts only a company the caller is listed for. The round-trip test chooses a company first. Not built: a Blazor host page, and
  data-scope checks beyond membership and the write guard.
- **Decision 28 (grouped settings screen): built in the generated web host.** Rows are grouped under their definition group (ungrouped under General, last).
  The Settings page never registered its typed client (`SettingsApiClient`), so it could not resolve it at runtime; the registration is fixed in
  `ApiClientExtensions` for every Razor host.
- **Decision 12 and 29 (company switcher UI): built in the generated web host (multi-company apps).** The company page lists the caller's companies,
  chooses one (a cookie that the BFF gateway turns into `X-Tenant-Id`) or clears the choice. Only a listed company can be chosen, and the API checks
  membership on every request. The per-request data-scope checks beyond membership are the existing tenant filter and write guard, not a new check.
- **Profile pages (decision 8): built in the generated web host (openiddict apps).** `Profile` (name, language, time zone, phone, and the change-email
  form) and `ConfirmEmailChange` (the link target; the change happens on POST, not on GET). The API routes are also served under `/api/account/profile`,
  because the BFF forwards only `/api`.
- **Not started:** the company-admin audit summary (decision 11, blocked on the field list).

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
- **Parent-administrator authority (premodule 2):** decided (decision 6) and built: an explicit authority per parent, child, account and operation, plus the parent permission.

### Decisions already taken, to confirm in review

- Parent grants apply below their holder; a deny anywhere on the path wins (the owner confirmed these defaults).
- Role administration gate (D-P17): role list, create, rename and delete need `identity:accounts:manage`; role permissions need `authorization:manage`.
- Personal-data permission and the audit permission are granted to no role by default.
- A new company gets the configured baseline (`CompanyProvisioning:Grants`, role to permissions), never a copy of another company's grants.
  The baseline may not name an unregistered, Critical or wildcard permission, or an `integration:` role. The default company gets the same
  baseline at startup. The baseline is empty unless configured; generated apps configure `Admin` with the example permission.

### Buildable gaps

- **Companies admin page in web apps (closed 2026-10-24):** `/Admin/Companies` (`tenants:manage`) lists, creates, changes the status and seat
  limit of companies through `/api/tenancy/tenants`, and shows and changes a company's members (add, revoke) and its parent company. Its
  navigation entry was already in the API and now leads to a real page. Multi-company web apps are allowed (decision 29).
- **Admin grant for new companies (done, D-10):** `POST /api/tenancy/tenants` applies the configured baseline to the new company and, when
  `AdministratorUserId` is given, makes that account a member. An invalid baseline answers 500 `BASELINE_INVALID` and keeps the company with no
  grants. Closed (2026-10-24, decision 30): `POST /api/tenancy/tenants/{id}/baseline` re-applies the baseline to an existing company and answers
  the roles it granted. A role that already holds grants there is left alone, so a deliberate change is never overwritten, and every run is
  audited (`company.provisioned`, with the count of roles granted). A closed company does not resolve, so it answers 404. The designated
  administrator must be an account in the identity store (`IUserRoleDirectory`); without a registered identity store the company is refused,
  since the account cannot be verified. Tests: `TenantCompanyProvisioningTests` (7 in total).
- **Business audit log viewer UI (premodule 4, closed 2026-10-24):** `/Admin/BusinessAudit` (`audit:view`) searches the company's entries by
  entity, actor, action and time, and pages them. Navigation entry added. The security viewer covers the security chain only.
- **Premodule 5:** grouping settings by definition group: closed 2026-10-25. `SettingDefinition.Group` files each setting, the administrator's view carries it, and the Settings page groups its rows (test `Administration_CarriesTheGroupEachSettingIsFiledUnder_AndNullWhenUngrouped`).
  The EF settings store is wired into generated multi-tenant apps (`AddEfCoreSettingStore`, values in the tenant store); single-tenant apps
  stay in memory. Closed: `SettingsStoreWiringTests` (3) and a generated multi-tenant app builds with 0 warnings and passes 11 of 11. Open:
  the `ModulusSettingValues` table needs a tenant-store migration in production (dev creates it when no migrations exist).
- **Branch scope (D-P4, built in the framework):** `SettingScope.Branch` and `SettingSource.Branch`. Precedence user, branch, company,
  installation, default. A branch value is keyed by company and branch (`ISettingStore` takes a branch id; the EF table needs no new column).
  It applies to the request's branch (`ISecurityContext.BranchId`, checked against the caller's org scope) and needs a company and a branch in
  scope to be written. Admin API: `PUT|DELETE /api/settings/branch/{name}`. Tests: `BranchSettingsTests` (6), store tests (+2). Open: the
  generated Settings page has no branch option, because the Web host does not send `X-Branch-Id` yet (a branch selector is needed first).
- **Browser checks:** the generated Users, Roles, Audit, Settings and Companies pages were verified by build and API tests only.

### Limitations in the built code

- **Upgrade scripts (closed):** `TenancyUpgradeScriptTests` checks that the three tenancy scripts add every company column the entity maps
  (`Status`, `TrialEndsAt`, `MaxUsers`, `ParentId`), and that the entity has the same property names.
- **Per-request grant memo (closed):** `CachedPermissionGrantStore` now keys its memo by the company lineage (`ICurrentTenant`), so a tenant
  change inside one request no longer reuses the earlier company's grants. Covered by `CachedPermissionGrantStoreTests`.
- **Admin listings are own-rows for writes:** the grant listings show the company's own rows for editing, and a separate read shows inherited
  rows. A listing does not merge them.
- **Refresher:** the delegation role refresher runs in the host context, so it sees every company. This is correct, not a gap.

### Blazor test stability

- **Teardown race:** mitigated, not explained. All component test classes derive from one base class that awaits asynchronous teardown; ten
  consecutive full runs passed after the change. Corrections (2026-10-24): the suite already disables test parallelization
  (`TestCollections.cs`), so parallel classes are not the cause. Forcing a GC and waiting for finalizers after synchronous dispose did not
  stop the failures (6 of 8 runs failed), so finalizer timing alone is not the explanation either.
- **Acceptance gap:** the owner requires a regression test for a confirmed cause. The cause is inferred from stack traces, not confirmed, so no
  such test exists yet.
- **Row-rule test:** it waits for each render before reading the grid. It passed in the last ten full runs.

### Repository state (2026-10-24)

- **Framework (`modulus`):** `main` is at `8e288c6` on origin. The work of this round is committed as `d4f3538` on the local branch
  `company-provisioning-baseline`, not pushed and without a PR.
- **UI (`modulus-ui`):** the Blazor test work is on `origin/blazor-test-stability` (`954ab84`). Local `main` is 5 commits ahead and 3 behind
  `origin/main`, because the remote kept a separate 0.9.1 line. The version line is an open decision.

### Verification (2026-10-24)

- Unit suites, on net8.0 and net10.0 (`dotnet test --filter "Category=Unit"`): Platform 574, MultiTenancy 140, Authorization EF 131,
  Authorization management 136, Identity 451, AspNetCore 156 (net10.0) and 149 (net8.0). The CLI suite is 639 on net10.0.
- The EF authorization suite passed on both targets in this round, and five further net8.0 runs passed. The one failure seen earlier on
  net8.0 has not been reproduced; its cause is unknown and the item stays open. On 2026-10-24 one more timing test failed once in a full run,
  `DelegationRoleStalenessTests.The_refresh_timer_bounds_an_unreported_change_to_its_interval` (net8.0); it passed 3 of 3 in isolation. Treat
  both as flaky under load until a cause is found.
- `dotnet build modulus.slnx`: 0 warnings, 0 errors. `dotnet format modulus.slnx --verify-no-changes`: clean.
- Generated multi-tenant API app, built on repacked framework packages that include the branch scope and the settings store: builds with
  0 warnings, 11 of 11 tests pass.
- Generated single-tenant app: 15 of 15 tests, measured on the framework at `1.6.0-local.11`. Not re-run in this round.

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
that applies in the children (the subtree grant) and the Companies page. The rule that lets a parent administrator manage a child's memberships and grants is built (decision 6). The generated multi-tenant app builds and passes its 11 tests on the framework at local.8.

Progress (2026-10-24): generated multi-tenant API apps now map the company routes (`AddModulusTenantAdmin`, `MapModulusTenantAdmin`, a
Companies menu entry for `tenants:manage`). The Companies page is built (2026-10-24, see the buildable gaps). Earlier, the EF grant store
showed a company only the grants with its own tenant id, so host-level grants were invisible inside a company and company requests were refused
(`403`), failing the generated multi-tenant app's round-trip and permission tests. On 2026-10-24 the generated multi-tenant app passes 17 of 17,
including the round trip through a chosen company, so this looks resolved. Confirm it against the grant store before relying on it.


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
browser. Built since: the branch scope (D-P4, an organization-unit scope between user and company; see the remaining-work section) and the EF
settings store in multi-tenant apps. Grouping by definition group: built 2026-10-25 (`SettingDefinition.Group`).

- **API:** exists (`/api/settings`). Each row already carries the type, allowed values and allowed scopes (`SettingView`), so a separate
  definitions endpoint is not needed. `AllowedScopes` is a flags enum and serializes as a number; the screen maps it to names.
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

## Decision register (owner decisions, 2026-10-24)

The owner decided the 14 open items. Status is what the repository shows; external dependencies are marked as such.

| # | Item | Decision | Status |
|---|---|---|---|
| 1 | Legal retention minimum (D8) | Unset until counsel confirms in writing | Open (counsel) |
| 2 | Security audit retention | Off by default; operator enables after approval; no automatic deletion before that | Built (`Security:Retention:Enabled` = false) |
| 3 | Pilot sign-off (D7) | Not signed off; define acceptance criteria, run tests, review evidence, then sign off | Open (owner) |
| 4 | Built-in administrator role | `Admin`, protected against deletion and rename | Built (`BUILT_IN_ROLE`, `RoleAdminEndpoints`) |
| 5 | `audit:security:view` default | No role by default; an operator grants it | Built (no default grant) |
| 6 | Parent-administrator authority | DECIDED 2026-10-24: a persisted `CompanyAuthorityGrant` per parent, child, principal and operation (`ManageMembers`, `ManageGrants`); revoked, not deleted; no caching; deny on any failed check. Acceptance: no grant, a grant for another child, `ManageMembers` without `ManageGrants` for grant operations, a revoked grant, or missing parent permission each deny; a valid relationship, parent permission and matching active grant allow | Built 2026-10-24: `ParentCompanyAuthority` checks, parent-to-child routes, host-only grant administration with no self-grant, audit of grant, revoke and refusal. Tests: `ParentCompanyAuthorityTests`, `ParentCompanyRouteTests`, `CompanyAuthorityStoreTests`. Upgrade: `ModulusCompanyAuthorityGrants` in the three authorization scripts |
| 7 | Company-admin audit summary | Six fields: time, category, action, outcome, actor display name, target; no raw details, hashes or host events | Built: `GET /api/audit/company-summary` (`audit:company-summary:view`, no default grant), `/Admin/CompanyAudit` in the web host. Actor names come from `IUserDisplayNameDirectory` (identity implementation, host context, display name only, one batch per page). An account that cannot be resolved, or has no display name, shows as "Unknown user"; the page still returns. The chain keeps user ids only; names are read at display time. Tests: `CompanyAuditSummaryTests` (5, Platform), `UserDisplayNameDirectoryTests` (3, Identity: batch, missing and nameless accounts, duplicates). Authorization on the endpoint is covered by the generated apps' security probe suite (`401`/`403` for every guarded endpoint). Company isolation: the endpoint takes no company id; it reads the caller's own chain only |
| 8 | Profile fields | Display name, language, time zone, phone; email change by confirmed link; no avatar or files in Release 1 | Built in the web host |
| 9 | OpenBao production evidence | Required before production sign-off; single node is development only | Open (operator) |
| 10 | SSO, KMS, custodians | OIDC SSO with MFA for OpenBao admins; Shamir seal, 5 shares, threshold 3, five separate custodians (no KMS, decided 2026-10-25) | Architecture chosen; providers and people not yet confirmed (operator) |
| 11 | AI platform conformance | Activation blocked until the platform's suite passes against the real connector | Open (platform owner) |
| 12 | `modulus-ui` publishing line | Target `1.6.1`, after reconciling the local branch and reviewing the `0.9.x` line | Open (release; manual publish) |
| 13 | Blazor teardown race | Keep the fix as a candidate; do not declare the cause confirmed; find the second thread or a reproducible explanation | CLOSED 2026-10-24 by waiver (option A, chosen by the owner): the async teardown base class is the fix. Cause unconfirmed; no regression test for it, by waiver. Evidence: synchronous teardown fails most full runs; async teardown 10 of 10 clean; parallelization already off (`TestCollections.cs`); delayed focus ruled out; forced finalization does not stop it. Upstream report drafted: `scratchpad/antdesign-issue.md` (AntDesign 1.6.2, static `DomEventSubscriptionStore`, `AntComponentBase` finalization) |
| 14 | Branch selector | Cookie, then gateway header `X-Branch-Id`, validated server-side against the caller's org scope; branches from the org-unit API | Built in multi-company web apps: `/Branch` picker, `GET /api/org/branches` (open units in scope), gateway sends `X-Branch-Id`; the server check is `BranchContextMiddleware`. Settings page branch option built in the generated web host (2026-10-24) |

Decisions recorded 2026-10-24 (owner, follow-up):

- **Item 5 (built-in Admin, audit default): confirmed, no change.** `Admin` stays protected (`BUILT_IN_ROLE`). `audit:security:view` has no default
  grant; security and audit staff get it explicitly.
- **Item 4 (profile field list): baseline approved.** Editable fields: display name, first name, last name, profile photo; work email, work phone;
  employee ID, job title, department, primary branch; preferred language, time zone. System-managed (not editable): user ID, account status,
  created and updated timestamps. Never in the profile: credentials, MFA secrets, recovery codes, authorization assignments (roles and
  permissions belong to the access-control module). Gap against the built profile (display name, language, time zone, phone; email by confirmed
  link; no photo): first name, last name, profile photo, work email (the built email is the account's own, changed by link), employee ID, job
  title, department, primary branch are not built. Building them is premodule 3 work and is not started.
- **Item 12 (modulus-ui line): reconciled on `release/1.6.1` (local, not pushed; owner approved keeping both commits for review).** Found already on `origin/main`: the 0.9.1 line merged into
  `main` at `5c581e6`, which deleted 28 docs (among them `docs/CLI.md`, which `AGENTS.md` names). Version metadata stayed `1.6.1` everywhere.
  Reconciliation commit `3db0dfa` restores those 28 docs from the 1.6.1 parent; `8ae6239` keeps the uncommitted asset-path fix and test
  fixes. Verified: solution builds with 0 warnings; tests pass (Hosting 15, Fluid 13, Mvc 245, Blazor 838). Not published: the owner publishes
  manually after reviewing the branch. Whether `origin/main` should be rewritten to drop the merge is the owner's call; this work does not rewrite it.
- **Item 2 (D7 pilot): SIGNED OFF 2026-10-24 by the owner** on the evidence reported (not independently re-executed by the signer). Evidence: Framework unit suite passes on net8 and net10 with 0 warnings
  (Identity 467, Authorization EF 143, Authorization Management 140, Platform 587, MultiTenancy 161, Audit EF 19). Criteria 1 to 4 are covered by
  the named test classes in those suites. Generated app (regenerated, `local.20`): 17 of 17 tests pass. A failure found on the way (the
  signed-in landing page returned 500 when the API was unreachable) was fixed: the default-company step now fails soft, with a template test.

Premodule 3 (profile), started 2026-10-24 on the approved field list:

- **Compatibility.** `PUT account/profile` (four fields, empty clears), `GET account/profile` and the email-change routes are unchanged. The
  new routes are additive: `GET` and `PATCH account/profile/details`. In `PATCH` a field left out keeps its value; an empty string clears it.
- **Fields.** Identity: display name (existing), first and last name (existing columns), photo (`AvatarUrl`, an https address; no upload yet).
  Contact: the existing `PhoneNumber` is the work phone (no second phone field); work email is new and separate from the sign-in address, and
  is not confirmed by a link. Organization: job title and department (new, editable by the person); employee code and primary branch (new
  `PrimaryBranchId`) are set only by an administrator through `PATCH identity/users/{id}/organization`. Preferences: language and time zone
  (existing). System-managed, read-only on the profile: user id, user name, sign-in address, active flag, created and modified timestamps.
  Credentials, MFA secrets, recovery codes and authorization assignments are not in the profile.
- **Required.** None. Every field is optional, because existing accounts have none of the new values.
- **Access control.** Employee code and work email are `Restricted` and job title and department are `Confidential` on the administrator's
  detail view, so they follow the field guard. The administrator's routes need `identity:accounts:manage` (the employee code additionally
  needs `identity:accounts:personal-data` to be read). An employee code is unique per company and upper-case.
- **Gaps, as built.** The primary branch is stored as an id and is not checked against the org units yet (an administrator enters it).
  The photo is an address only. Work email has no confirmation step.
- **Verification.** `ProfileTests` (2 new), `UserAdminHttpTests` (1 new), `TokenPrincipalFactoryTests` (a test subclass that declared its own
  `Department` now inherits it), `ProfileDetailsTemplateTests` (2). Identity suite 472 on net8 and net10; CLI 650. Generated app
  (`local.21`): builds, 17 of 17 tests pass. Browser, through the gateway: saved the details, reloaded, values persisted, no console errors.
- **Upgrade.** New columns on the identity user table (`WorkEmail`, `JobTitle`, `Department`, `PrimaryBranchId`): existing deployments add a
  migration (`modulus migrate add <Name> --module Identity`). Apps that declare their own `Department`, `JobTitle`, `WorkEmail` or
  `PrimaryBranchId` on a `ModulusUser` subclass must remove or rename them (CS0108).

Premodule 3 review (2026-10-24, after the owner's compatibility, migration and authorization decisions). Status by requirement:

- **Work phone:** reuses `PhoneNumber`. Done.
- **Work email:** separate from the sign-in address, which it never changes. Built 2026-10-24 (owner decision): an entered address is **pending**
  (`PendingWorkEmail`) and becomes `WorkEmail` only when its owner follows the link. The link is a Data Protection token with its own purpose
  (`Modulus.Identity.WorkEmailVerification.v1`, so a sign-in or e-mail-change token cannot confirm it), valid 30 minutes
  (`Identity:WorkEmail:Lifetime`), bound to the account and the pending address, and tied to a per-request stamp on the account: a new request or
  a new address replaces the stamp, and confirming clears it, so a link works once and only the newest works. Requests are rate-limited (one per
  60 seconds, `Identity:WorkEmail:Cooldown`) and answer the same whatever the address. The person confirms with `POST account/profile/work-email/confirm`
  while signed in as that account. An administrator's entry (`PATCH identity/users/{id}/work-email`, `identity:accounts:organization:manage`)
  also only starts a link. Each request and each verification is audited (`identity.user.work-email.requested` / `.verified`). The link mail is
  sent through `IIdentityEmailSender.SendWorkEmailVerificationAsync`; its default sends a confirmation mail, so an app must override it to point
  at the confirm route. Tests: `WorkEmailVerificationTests` (11), `ProfileTests`.
  **Breaking for a client that relied on the old behaviour:** `PATCH account/profile/details` no longer makes a work email take effect at once.
  The response carries `pendingWorkEmail` until the link is followed; `workEmail` stays empty until then.
- **Employee code and primary branch:** organization-managed. Writes need `identity:accounts:organization:manage` (Sensitive, new), in addition to
  the account administration permission. Employee code is upper-case and unique per company (checked in the application, and by a unique index on
  `(TenantId, EmployeeCode)` in the upgrade scripts for PostgreSQL, MySQL and SQL Server, filtered on SQL Server). The primary branch must be an
  org unit of the request's company (`IOrgHierarchy.Contains`); with no hierarchy registered it is refused. The branch name is not resolved yet.
- **Profile photo:** built as the API (2026-10-24). `PUT account/profile/photo` takes the image as the body and accepts JPEG, PNG and WebP only,
  decided by its bytes, not its name or declared type; SVG and animated images are refused. A file over 5 MB is refused before it is read in full,
  and a side over 4096 px is refused from the header before the pixels are decoded. The stored photo is a centred 512 px square, re-encoded as JPEG
  from the decoded pixels, so no metadata of the original survives; the original is never kept. The key is a random asset id under the account's
  tenant folder (`profile-photos/{tenant}/{asset}.jpg`), and the account keeps only that id (`PhotoAssetId`). `GET account/profile/photo` serves
  only the caller's own photo; `DELETE` clears the reference first, then removes the file. A replacement removes the old file only after the account
  is saved, and a failed account save removes the new file. A failed removal is logged, not returned as an error. `AvatarUrl` (https address) stays
  as it was, for accounts that already have one; the photo asset is the controlled path. Decoding uses SixLabors.ImageSharp 2.1.13 (Apache-2.0, pure managed; replaced SkiaSharp on 2026-10-24, see `THIRD-PARTY-NOTICES.md`). The
  storage is the framework's `IFileStorage` (local, S3 or Azure); without one the upload is refused with 503. **Not built:** the web page to upload a
  photo upload page is in the web template (see the release gate, item 3). EXIF orientation: applied before the crop since 2026-10-25
  (`AutoOrient`; test `A_camera_photo_with_a_rotation_tag_is_stored_upright`). Tests: `ProfilePhotoProcessorTests` (11), `ProfilePhotoEndpointTests` (7).
- **Optional fields:** every new field is optional; the original four keep their rules.
- **Confidential behaviour (tested, API level):** job title and department were masked for every caller, because a non-public classification with no
  clearance profile is closed. They now need `identity:accounts:organization` (Sensitive, new). Tests: an administrator without the clearance sees
  them as null; with it, they are visible; an ordinary account gets 403 on the admin routes (`Organization_details_need_the_organization_clearance_even_for_an_administrator`).
  The UI was not tested in a browser for these cases.
- **Admin-managed fields need explicit permission:** `Changing_the_employee_code_needs_the_organization_manage_permission...` (account administration alone gets 403).
- **Source compatibility:** the new `ModulusUser` properties could clash with an application's own property of the same name (CS0108). No generated
  template declares them (checked). A freshly generated app builds and passes its 17 tests. **Not done:** regenerating an older application and
  compiling it against this framework, as the owner asked.
- **Database migration (existing deployments):** `docs/upgrades/identity-platform-2026-10.profile.{postgresql,sqlserver,mysql}.sql`. Nullable columns
  (`WorkEmail`, `JobTitle`, `Department`, `PrimaryBranchId`, and since 2026-10-24 `PhotoAssetId`, `PendingWorkEmail`, `WorkEmailVerificationStamp`,
  `WorkEmailRequestedAt`), plus `ConfirmationFailures` (`NOT NULL DEFAULT 0`). Each script first checks for duplicate employee codes in one company and stops
  with a diagnostic, changing nothing, if it finds any. It then creates the unique employee-code index before dropping the earlier non-unique one, so
  upgraded and fresh schemas have the same index set. Verified 2026-10-24 on PostgreSQL 16, SQL Server 2022 and MySQL 8.4: clean data (two runs; data
  preserved; duplicate in one company refused; NULL and cross-company codes accepted) and duplicate data (non-zero exit, no column added, old index kept, no
  unique index). Test: `ProfileUpgradeScriptTests` (static checks on each script, including the order).
- **Fresh installations (done 2026-10-24).** The EF model maps `(TenantId, EmployeeCode)` as a unique index named `IX_AspNetUsers_TenantId_EmployeeCode_Unique`,
  filtered `IS NOT NULL` on SQL Server, PostgreSQL and SQLite; MySQL takes no filter. Codes are trimmed and upper-cased before they are written, so the
  database compares one spelling. Verified with `EmployeeCodeUniquenessTests` (SQLite) and `EmployeeCodeFreshInstallTests` (PostgreSQL 16, SQL Server 2022,
  MySQL 8.4; 12 tests per target), rerun after the later EF default for `ConfirmationFailures`: 12 of 12.
- **Release diff review:** `crud-edit.razor.sbn` reviewed. The 1.6.1 edit page had its own inline form; the create page on the same line already used the
  shared editor, so the merged edit page makes the pair consistent. Validation, Save/Create and Cancel are all kept in the editor. One unverified
  point: the namespace moved from `ApiClients` to `Services.ApiClients`; check it against the generator's output. The other ~320 deleted lines are not reviewed.
- **Release gate for premodule 3 (owner decisions, 2026-10-24).** Evidence: `docs/upgrades/RELEASE-NOTES-2026-10.md` ("Release gate for premodule 3")
  and `THIRD-PARTY-NOTICES.md`. Status per item:
  1. *Browser confidentiality (admin without clearance, admin with, ordinary user; displayed values and PATCH):* **Open.** API tests pass; no
     generated app was started in a browser this pass.
  2. *Release-diff review of every remaining deleted line:* **Open.** One file reviewed (`crud-edit.razor.sbn`); about 320 lines remain.
  3. *Photo and confirmation pages:* **Built** in the web template (`ProfilePhoto`, `ConfirmWorkEmail`), with template tests (6 in
     `ProfileDetailsTemplateTests`). **Not compiled** as a generated app this pass. The API behind them is tested: photo (`ProfilePhotoProcessorTests`,
     `ProfilePhotoEndpointTests`) and confirmation outcomes, including expired, used, superseded, too-many-attempts (`WorkEmailVerificationTests`).
  4. *Native image runtimes:* **Reframed.** The decoder is pure managed code, so there is no native runtime per platform. **Verified: Linux x64 only.**
     Windows, macOS, Linux ARM64, Linux ARM and musl are **not tested** and no claim is made for them. The refusal paths (malformed, truncated, TIFF,
     colour profile, animation, oversize) are tested on Linux x64 only.
  5. *Decoder licence and security (replaced SkiaSharp):* **Open, owner decision.** ImageSharp 2.1.13 is Apache-2.0 (verified at the upstream commit);
     its managed dependencies are MIT. Five advisories whose fixes exist only in the Split-Licensed 4.x line are suppressed in `Directory.Build.props`,
     each with its reason (`THIRD-PARTY-NOTICES.md`). The owner must accept that risk, or choose another decoder.
  6. *Legacy `AvatarUrl` policy:* **Built** (`AvatarUrlPolicy`, 21 tests): https only, no credentials, no whitespace, at most 2048 characters; the photo
     takes precedence; the server never fetches the address. External hosts are allowed by the rule; the page uses `referrerpolicy="no-referrer"`.
  7. *Work-email confirmation safeguards:* **Built and tested:** the link is bound to account, purpose and pending address; it expires; it is single-use;
     the newest link only works; a verified address is kept until a new one is confirmed; five failed attempts lock confirmation until a new request.
  8. *Employee-code migration:* **Built and verified** on PostgreSQL 16, SQL Server 2022 and MySQL 8.4: a duplicate in one company is refused before
     any change, with a diagnostic; the unique index is created before the old one is dropped.
  9. *Older generated app compiling:* **Open.** Not done in this pass.
  10. *Full regression:* Identity suite 539 of 539 on net8.0 and net10.0; CLI suite 654 of 654 (net10.0); the full solution builds with 0 errors and
      0 warnings; `dotnet format modulus.slnx --verify-no-changes` clean; provider fresh-install tests 12 of 12 on both targets.
  The release stays local and unpublished.
- **Blocked, unchanged:** D8 legal retention (counsel), OpenBao evidence (operator), item 10 SSO/KMS (operator), item 11 AI conformance (platform owner).
- **Publication:** `release/1.6.1` stays local. Nothing is pushed, merged to the remote, or published.

Rules that go with the decisions:

- Items 1, 9, 10 and 11 need external people or systems. The repository does not mark them complete.
- Item 6: the explicit child grant is checked server-side for every relevant operation.
- Item 7: actor names and target identifiers show only to users authorized for the company audit summary.
- Item 14: the cookie selects a branch and grants nothing. Only the server's scope check authorizes it.

## Open decisions for the owner

1. Pilot module: **decided 2026-10-23** as identity-account administration, the framework's own module (Inventory is a later candidate).
2. Profile fields for premodule 3 (name, language, time zone, contact phone: which ones, and which need confirmation).
3. Audit export format and maximum window (premodule 4).
4. Whether company administrators may see their own audit chain's raw events or only the decoded summary (premodule 4).
5. Confirm the order above, or choose another.

## Final disposition of blocked items (owner, 2026-10-24)

These four items stay open. Each closes only on the written or verified deliverable listed. Design intent does not close an item.

| Item | Status | Owner | Required before closure | Current safeguard |
| --- | --- | --- | --- | --- |
| D8 legal retention minimum | Blocked | Counsel | Written retention requirements: record categories and jurisdiction | `Security:Retention:Enabled` = false; no statutory minimum invented |
| OpenBao production evidence | Blocked | Operator | Evidence of cluster health, Raft membership and quorum, storage persistence, TLS, audit logging, and a tested recovery | Three-node Raft production design; single node for development or temporary pilot only |
| Item 10 SSO, KMS and custodians | Blocked | Operator | The Shamir 3-of-5 design with the five named custodians; documented recovery responsibilities; a tested restart-unseal, backup-recovery and custodian-unavailability procedure | OIDC SSO with MFA for OpenBao administrators |
| Item 11 AI platform conformance | Blocked | Platform owner | The platform's own conformance suite passing against the real connector; preserved output, failures, connector version and final result | Connector activation disabled |

No configuration change, deletion enablement, connector activation, or production sign-off is authorized by this disposition.

## Release-diff review of `modulus-ui` 1.6.1 (2026-10-24)

Scope: the deleted lines from the 1.6.1 baseline (`954ab84`) to the release branch (`HEAD`): 324 lines across 76 files. Each deleted line was
checked for whether its text still exists in the release tree. Of 299 deleted lines of 12 characters or more, 110 still exist (moved or
kept). The 189 that are gone were sorted by the change they belong to, then read in context.

| Category | Lines | Verdict |
| --- | --- | --- |
| Package rename `Modulus.Ui.Assets` → `Synvro.Modulus.Ui.Assets` (commit `7ade1c7`, asset base path fixed in `8ae6239`) | in the head, asset, theme, and Fluid files | Intentional. Asset paths match the renamed package. |
| Create/edit split: `crud-edit` renders the shared `_Form` (`crud-form` maps to `_Form.cshtml` in the CLI); `New` links go to `/new`, and the create routes exist | create/edit and index templates | Intentional. Checked: the hidden `Input.Id` and the validation summary are in `_Form`; `_List` (`crud-list`) keeps the empty state, table, and delete form. |
| Entity form (Blazor): route `/{entity}/edit/{Id}` → `/{entity}/form/{Id}`; the `TODO: save through the API` stub is replaced by real create/update calls | `entity-form.razor` | Intentional. Matches the `/form/` convention the samples use. The stub was a gap, now closed. |
| Namespace `.ApiClients` → `.Services.ApiClients` | Blazor CRUD templates | Verified consistent: the CLI writes clients to `Services/ApiClients`, and the framework's profile client uses that namespace. |
| Placeholder text on inputs (added `placeholder=`) | forms in several templates | Intentional UX text. No behaviour change. |
| `IUserDirectory`, `ITenantDirectory`, `IAuditTrail`, `INotificationCenter` injection renamed to `…Directory` / `AuditTrail` / `NotificationCenter` | Blazor admin pages | Intentional naming; every use in each file was renamed. |
| `CancellationToken` passed through test calls | `SampleAdminDataTests` | Intentional (analyzer warnings). **Not compiled in this pass.** |
| Sample money and date formatting: `en-US` → `InvariantCulture` | `Fmt.cs` | Behaviour-preserving: every format is a literal pattern with `$`, and invariant English month names equal en-US. |
| README and DEPENDENCIES links from `docs/UPGRADING.md` to `docs/DOCUMENTATION.md#…` | docs | Intentional. Each anchor matches a real heading (checked). |
| `[Authorize]` made conditional on `use_auth` (commit `7ade1c7`, "conditional [Authorize] when the app signs users in") | admin, user-profile, tenant, audit, notification, and role pages, MVC `AdminController` | **Intentional but security-relevant. Owner decision.** In an app with no sign-in, these pages carry no attribute. Access then depends on the framework fallback policy, which applies only when `AddModulusAuthorization` is registered. An unconditional `[Authorize]` would make such an app return 500 (no scheme). Options: keep the conditional, and require the app to register a fallback policy or `[AllowAnonymous]` explicitly; or make the attribute unconditional and document that no-sign-in apps must loosen it. |

**Accidental regressions found:** none confirmed. The one apparent loss (the inline table in `crud-index`) is the intentional split into `_List`.

**Not verified in this pass:** the `modulus-ui` build and test run (the Blazor test changes are unverified), and any generated output from these templates.
Items still open: the owner decision on conditional `[Authorize]`, and a build plus test run of `modulus-ui` on this branch.

## Decisions taken on the owner's behalf (2026-10-24)

The owner asked for the open items to be analysed and decided. These are the decisions, with the reason and what would change them. Nothing here
pushes, publishes, or enables a blocked item.

| # | Item | Decision | Reason | Revisit when |
|---|------|----------|--------|--------------|
| 1 | Five ImageSharp 2.1.13 advisory suppressions | **Accept**, as recorded in `Directory.Build.props` and `THIRD-PARTY-NOTICES.md` | Each affected path is unreachable: ICC (refused by the structure walk), BigTIFF (refused by the format gate), TIFF/histogram code is never called. Tests prove the gates. 2.x is Apache-2.0 and pure managed; 3.x is Split License and is rejected. SkiaSharp and MagicScaler were rejected earlier for native or licence reasons. | A fixed 2.x release appears, any gate test fails, or a permissive decoder with a clean advisory record is found. Review at every release. |
| 2 | Conditional `[Authorize]` when the app has no sign-in | **Keep conditional.** No-sign-in apps rely on the closed-by-default fallback policy | An unconditional attribute makes a no-sign-in app answer 500. The framework already closes such an app by default (`AddModulusAuthorization` fallback = signed-in user) and the startup guard refuses an unpoliced endpoint. Opening an endpoint stays a reviewed `Loosen` decision. | A generated no-sign-in app is found to serve an admin page anonymously. |
| 3 | `modulus-ui` build and tests | **Done**: `Modulus.Blazor.Tests` 838/838, including the `SampleAdminDataTests` changes | | |
| 4 | Fresh and older generated-app compile | **Defer** until 5 GB or more is free; do not delete anything to make room | Repack needs ~1 GB beyond what is free | Disk freed by the owner |
| 5 | Browser confidentiality tests | **Required before release**, no waiver | They are the only evidence for the displayed values and PATCH rejection | A generated app can run |
| 6 | Native runtime coverage | **Release supports linux-x64 only**; other platforms are listed as untested, not as passing | Pure managed code makes failure unlikely, but it is not evidence | A CI runner for each platform |
| 7 | Real SMTP delivery | **Required before enabling mail in any environment**; the feature ships off (`Identity:Email:SmtpHost` unset uses the no-op sender) | Only a recording transport was tested | A test mailbox is available |
| 8 | D8, OpenBao, item 10, item 11 | **Unchanged: blocked.** `Security:Retention:Enabled` stays `false` | Need counsel, operator and platform-owner evidence | Their evidence arrives |

The release stays on hold until items 4, 5 and 7 have evidence. Items 1 and 2 no longer wait for an owner.

### Verification pass (2026-10-24, second round)

- `modulus-ui` Blazor tests: 838/838. Mvc: 245/245. Fluid: 13/13. Hosting: 15/15 (all net10.0).
- Framework `dotnet format modulus.slnx --verify-no-changes`: exit 0 (clean).
- `Modulus.Identity.Tests` Category=Unit: 552/552 on net10.0 and net8.0.
- Not run: the generated-app compile, browser tests and SMTP test (disk and environment limits, see the decisions table above).

### Fresh-app compile and fixes (2026-10-24, third round)

The framework was packed into a scratchpad feed (root partition, not `/home`) with `NUGET_PACKAGES` redirected, and `modulus-ui` was packed
beside it (`Synvro.Modulus.Ui.*` and `AspNetCore.Mvc/Hosting/Fluid` 1.6.1). Four generated apps were then built with the new CLI.

Defects found by the fresh-app builds and fixed in the templates:

1. **`Profile.cshtml` did not compile** (RZ1010): a `@{}` block nested inside an `@if` in the page. Fixed: the avatar is chosen in a top-level
   block; the Identity types are referenced only when the app has sign-in (`use_auth`).
2. **Generated apps lacked the ImageSharp advisory suppressions**, so every auth app showed 40 NU1902/NU1903 warnings. Fixed:
   `Directory.Build.props.sbn` carries the five `NuGetAuditSuppress` items with their reason, for apps with sign-in.
3. **`--auth none` web apps did not compile** (`Settings` page used the auth-only `Web.Security` namespace). Introduced by this branch's
   uncommitted Settings change. Fixed: the branch-cookie code is generated only with `multi_tenancy`, the one case where the branch picker exists.
   The same `Web.Security` reference in `Branch.cshtml.cs` predates this branch, but that page is generated only with `multi_tenancy`.

Results:

| App | Build | Tests |
|---|---|---|
| webapp+api, openiddict, mvc | 0 errors, 0 warnings | 15/15 (generated suite) |
| webapp+api, none, mvc | 0 errors, 0 warnings | not run |
| webapp+api, openiddict, multi-tenancy, mvc | 0 errors, 0 warnings | not run |
| api, openiddict, multi-tenancy | 0 errors, 0 warnings | not run |

`modulus` CLI tests: 657/657 (three new in `OptionalAuthTemplateTests`, which fail on the defects above). `dotnet format --verify-no-changes`: clean.

Not done: browser tests, SMTP, native runtimes, and repacking with `/home` space freed (the disk is still at 1.1 GB free).

### Round 4 (2026-10-25): AI connector off by default, OpenBao unseal fixed at 3 of 5, no-sign-in smoke tests

Owner decisions applied (see "Final disposition of blocked items"): the unseal design is **Shamir, 3 of 5, five named custodians, no KMS**;
the AI connector is **off by default**. Items 10 and 11 stay blocked: the custodians, the SSO provider and the platform's conformance
result are still missing.

**AI connector (item 11 safeguard).**
- `ModulusAiConnectorOptions.Enabled` defaults to `false` (was `true`). `add-ai` already wrote `"Enabled": false`; the generated testing
  settings turn it on for the generated tests only.
- Decided at startup, like the background services. With the connector off, `MapModulusAiConnector` maps **no route**. The first draft
  kept the routes and returned 404 from an endpoint filter, but authentication runs before endpoint filters, so a request with an envelope
  could still reach the platform's signing-key fetch (`AiEnvelope`). Found by the new tests, fixed.
- The revocation dispatcher does not run and the revocation observer queues nothing while off. Before this round, a disabled connector with
  instances configured still sent `revocations/scope` requests.
- Turning it on requires a restart.
- Tests: `DisabledByDefaultTests` (4: default off; no route mapped and no platform call; no signal queued; a pre-journalled signal waits;
  control: the same signal is sent when on). Existing tests that exercise the connector set `Enabled = true` explicitly; two validator
  tests now say "when on", because the validator skips a disabled connector so a fresh app with no keys can start.
- Results: `Modulus.AI.Connector.Tests` 129/129 (net10.0 and net8.0); `Modulus.AI.Connector.EntityFrameworkCore.Tests` 30/30 (net10.0).
- Running a generated API (`add-ai`, Development): `/health/live` 200; every `/_ai/connector/*` route answers 401 to a wrong key, and the
  startup log has no connector line.

**Not changed, needs a decision.** The change journal `ai_changes` is still written on every save while the EF journal is registered, even
with the connector off. The journal rows are written by the application's own transactions and are read only by the connector, so
"no application data accessed" is not strictly met. Stopping the write means changes made while off are not journalled, and the index needs
a full re-extract after activation. Owner to choose.

**OpenBao unseal (item 10).** The runbook, the disposition, the plan, the framework-operations note, the operator checklists and the compose
template now all say: Shamir seal, 5 shares, threshold 3, five named custodians, no KMS, no auto-unseal. The runbook's restart, backup-recovery,
custodian-unavailability and lost-share procedures are listed for testing. Item 10 stays blocked until the custodians are named and the
procedures are tested.

**Found, then resolved (2026-10-25).** `docs/IDENTITY_PLATFORM_PLAN.md` said "Raft of five nodes preferred"; it now says three, as the runbook and decision 17 do.

**No-sign-in web apps.** Every page of a `--auth none` web app answers 500: the closed default requires a signed-in user, and no
authentication scheme exists, so the endpoint throws. The generated smoke test now asserts only that no page is served anonymously. A
clean 401 or a sign-in redirect would need the app to register a scheme, or an explicit open decision. Owner to decide.

**Generated apps (fresh, `/tmp` feed, rebuilt CLI).** Each builds with 0 errors and 0 warnings, and each suite passes:

| App | Tests |
|---|---|
| web, sign-in, MVC | 15/15 |
| web, no sign-in, MVC | 8/8 (before this round: 3 failing smoke tests, which assumed sign-in) |
| web, sign-in, multi-tenancy, MVC | 17/17 |
| API, sign-in, multi-tenancy | 11/11 |
| API, sign-in, `add-ai` | 14/14 |

`modulus` CLI tests: 658/658, including three new in `OptionalAuthTemplateTests` (no-sign-in smoke tests, identity references, advisory
suppressions). `dotnet format --verify-no-changes` run at the end of this round: clean (exit 0).

### Round 5 (2026-10-25): gaps closed in the code, and what stays open

Built and tested in this round:

- **EXIF orientation of profile photos.** `ProfilePhotoProcessor` applied no rotation, so a camera photo was stored sideways. The
  rotation is now applied before the square crop. Test: `A_camera_photo_with_a_rotation_tag_is_stored_upright` (fails without the fix).
  `Modulus.Identity.Tests` Category=Unit: 553/553 on net10.0.
- **Settings grouping (premodule 5).** Already in the code (`SettingDefinition.Group`, the Settings page); the plan said "not built", which
  was stale. Test added: `Administration_CarriesTheGroupEachSettingIsFiledUnder_AndNullWhenUngrouped` (`Modulus.Platform.Tests`, 25/25 in
  `SettingsPhase5Tests`).

Still open, and why (none can be closed in this repository alone):

- Owner or operator items (decision register, items 1, 3, 9, 10, 11) and the release gate items 1, 2, 5, 9 above: browser evidence,
  SMTP, the older generated-app compile, and the decoder licence decision. Disk is at 1.1 GB free; repacking needs about 1 GB more.
- Branch picker in a generated-app round trip: the generated tests create no org unit (a test fixture change, not started).
- Primary branch is checked against the org units of the company, but its name is not resolved (premodule 3, display only).

### Round 6 (2026-10-25): open items closed, UI polish, and what stays open

Closed and verified in this round:

- **No-sign-in web app answers 401, not 500** (`NoSignInAuthentication` scheme; pipeline runs authentication and authorization). Generated suite 8/8.
- **AI change journal writes nothing while the connector is off** (`AiChangeSaveContributor` checks `Enabled`; the journal starts empty, so a full re-extract follows activation). EF connector suite 31/31.
- **Flaky `DelegationRoleStalenessTests` (net8.0)**: cause was the test reading the shared SQLite connection while the refresh timer wrote through it. Fixed. Five clean runs after the fix.
- **Branch picker round trip** (multi-tenant generated suite, 19/19). **Web-to-API round trip** (A2.5.3, A5.5). **Kill-the-API** (A2.5.4, live).
- **Raft** aligned to three nodes. **A2.3.5 / A2.3.6** deferred by decision.
- **Older generated app** (generated by the commit `d4f3538` CLI) compiles against the current framework with 0 errors. Its 40 warnings are the ImageSharp advisories, which the current template suppresses; with the same `NuGetAuditSuppress` block copied in, it builds with 0 warnings and its 15 tests pass.
- **SMTP delivery** verified against a real SMTP server (Mailpit): a password-reset mail arrives, its link resets the password, the link is single-use, and sign-in works with the new password.
- **Password recovery pages (B1)**: forgot and reset pages, a forgot link on sign-in, and the reset page at `/ResetPassword` (the path the email link uses). A browser run of the whole flow passes: forgot, mail, reset, sign-in, and creating a product through its form.
- **Browser checks (interactive)**: that flow ran in headless Chromium with no console or page errors. Screenshots are in the scratchpad.
- **NuGet source bug**: with one folder passed as both the package source and the UI feed, NuGet's source mapping could not resolve the UI packages. The generator now emits one source in that case.
- **UI polish** (generated web app): a welcome page in place of the developer note, a styled error page (404, 403, 409, 429, 500) in place of raw problem documents, the SPA navigation loader on, a favicon in place of a 404, and every page rewritten with the framework's card, table and form pattern (profile, users, roles, settings, audit, companies, branches, passkeys, photo, confirmations, sign-out). Empty lists show a failed-load state with the reason when a request fails, not "no results".
- **Profile names the primary branch** (or "a branch you cannot see" when it is outside the caller's scope).

Still open, and why:

- **Publishing (needs your go-ahead):** the `modulus-ui` fixes (failed-load state, `crud-list`, `crud-index` for MVC and Blazor) are in source and in local packs (`1.6.1-local.fix4`), not published. The shipped `1.6.1` UI packages serve assets from a path that returns 404, so pages render unstyled; that is the root cause behind the earlier unstyled pages, and it needs a published UI release. The CLI and Platform packages are also unpublished.
- **Browser confidentiality (release gate):** the admin detail pages do not show the classified fields (employee code, job title, department), so there is nothing in the browser to check yet.
- **Branch name in the profile, set path:** the "not set" path is checked in the browser; the name path compiles, but no admin path to set a primary branch was verified (the owner permission list does not include the organization permission).
- **Tenancy (found, not changed):** in a multi-company app the seeded admin sees empty Users, Roles and Settings lists, because host-level accounts and built-in roles are hidden inside a company and the company baseline does not grant settings reads. This is the authorization design; a decision is needed on what the company administrator should see.
- **B2 to B7:** these target the UI code that now lives in `modulus-ui` (B2 extension fields in forms, B3 audit columns, B4 to B7 notes and page precedents).
- **Native runtimes:** only linux-x64 can be run here; there is no arm64 emulation in this environment, and no CI runners for the other platforms.
