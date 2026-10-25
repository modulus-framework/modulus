# Release notes: identity platform, 2026-10

This release changes how permissions, the permission catalog, accounts and the security audit are stored, and adds a provisioning host
for tenant databases. Existing deployments must run the upgrade scripts in this folder before the new release starts.

## What changes for applications

- **Production refuses to start without migrations for the authorization store and the tenant catalog.** `MigrateAuthorizationStoreAsync`
  and `MigrateTenantStoreAsync` throw outside Development when the context has no migrations. Author them in the application (a context
  derived from `AuthorizationStoreDbContext` or `TenantStoreDbContext`, with `dotnet ef migrations add`) before the first production start.
- **Grants live in the Authorization database.** A generated API with an `Admin` role keeps its grants there
  (`AddEfCoreAuthorizationStores`). `AddPermissionGrants` is a first-boot seed: it is written once into an empty database and never again,
  so a revoked grant stays revoked.
- **The permission catalog has one set of rows per host.** `PermissionCatalogOptions.Owner` defaults to the host's assembly name. A host
  reads, writes and removes only its own rows. Changing the owner orphans the rows recorded under the old one.
- **Accounts use the lifecycle, not `IsActive`.** Accounts disabled through `IsActive = false` become `Disabled` during the upgrade and stay
  disabled. Lifecycle changes are recorded in `ModulusAccountTransitions`.
- **Security audit:** the purge checkpoint, legal hold and archive tables are new. Retention is opt-in (`AddModulusSecurityAuditRetention`).
  `ISecurityAuditArchive` gained a read member (`ReadAsync`); an archive that does not implement it keeps the default, which refuses, and
  restore validation then reports the chain as not provable.
- **Restore validation (read-only):** `SecurityAuditRestoreValidator` checks that the archive and the live chain still form one chain from
  the genesis hash. It writes nothing. Re-inserting archived entries into a live chain is not supported.
- **Provisioning host (new generator output):** `modulus add-openbao --provisioner` writes a separate host, the only one with an OpenBao write
  identity. It reads grants from the API's Authorization database, so the two connection strings must name the same database. The API must
  issue unencrypted access tokens (`Identity:EncryptAccessTokens=false`) so the host can validate them. Set `Identity:Audiences` on the API to
  the audience the provisioning host expects, and `Provisioning:Audience` to the same value; the token server then puts it in the `aud` claim.
- **Setting values** (`ModulusSettingValues`) are part of the tenant store database and need the tenancy upgrade below.
- **Dimension values** (`ModulusDimensionValues`) are part of the authorization database and need the upgrade below.
- **MySQL:** the authorization store and the identity store can now be created on MySQL. The scoped-grant key is limited to 200 characters
  of `Scope` on MySQL (the other providers keep 300), and OpenIddict's token columns have explicit lengths there. A MySQL database that
  was created before this release never had these tables, so the upgrade needs no data change for them.
- **Provisioning hosts** are generated for SQLite and MySQL as well as PostgreSQL and SQL Server. The SQLite host pins the patched native
  SQLite library.
- **Access token audience:** `Identity:Audiences` puts the configured audiences in the `aud` claim of every access token.
- **Settings (typed, pinned, administered).** Setting definitions take a value type, allowed values and allowed scopes, and writes are
  checked against them (400 when refused). With several nodes and Redis, `AddRedisSettingCacheBackplane()` (after `AddEfCoreSettingStore()`)
  makes a write on one node visible on the others at once. Without Redis, the store's change counter (`ModulusSettingVersions`, upgrade
  step 4 of the tenancy scripts) is checked every `Settings:VersionCheckInterval` (5 seconds), so other nodes see the change within that time. `Settings:Pinned` fixes a value that no scope can change (409 on a write). `AddEfCoreSettingStore()`
  keeps values in the tenant store database (`ModulusSettingValues`, below) and caches them per node for `Settings:CacheDuration`. A
  company-scoped write with no company in scope is refused: before this release it changed the installation-wide value.
  `MapModulusSettingAdministration()` serves the administrator's view and writes (`settings:manage`; installation-wide writes need `settings:global:manage`).
- **Navigation.** `AddModulusNavigation()` builds the application menu from `INavigationContributor`s and checks it when the host starts. A person
  sees an item when they hold its permission. A company can hide, retitle, reorder or move items (stored as settings, `navigation.override.{key}`);
  an override never reveals an item. `MapModulusNavigation()` serves `/api/navigation/menu` and the administration routes (`navigation:manage`).
  Generated OpenIddict APIs register and map it (`AddModulusNavigation`, `MapModulusNavigation`). They need the 1.6.0 packages: the
  published 1.5.0 has no navigation.

- **Outbox management is privileged.** `messaging:manage` (list, inspect and replay dead-lettered messages) is now `Critical`: in production it
  needs a passkey or multi-factor sign-in, and no wildcard grant confers it. Grant it deliberately. The procedure is in
  `docs/runbooks/privileged-access.md`.

- **Permission grant copy (optional, off).** `Authorization:GrantCache:Duration` lets a node reuse a principal's grants for that long
  (grants only, never decisions). Off by default, so revocations apply on the next check. Enabling it trades revocation time on other
  nodes for speed; see `docs/IDENTITY_PLATFORM_PLAN.md` §14.8 for the measurements.

- **MVC web hosts (fix).** A generated `--ui-engine mvc` host now maps its attribute-routed CRUD controllers (`MapControllers`), and its
  default controller route requires sign-in. Before this fix the host refused to start under the security guard (the default route had
  no policy), and the attribute routes were unreachable. Regenerate an MVC web host, or add the same two lines to its endpoints file.

- **Permission-exception register.** `MapModulusPermissionExceptions()` records exceptions to privileged permissions (justification,
  business owner, compensating control, end date at most 90 days away, review by someone else) and lists the privileged grants no
  active exception explains. Table `ModulusPermissionExceptions`: upgrade script step 4 in the authorization scripts (verified twice on
  PostgreSQL 17, SQL Server 2022 and MySQL 8.4). It reports; it does not grant or revoke.

- **Authorization upgrade scripts create every table.** Step 5 of each authorization script creates the remaining tables of the store
  (access requests, approval workflow, positions, party links, scoped grants, record shares and others), generated from the EF model. Every
  statement is idempotent. Verified twice on PostgreSQL 17, SQL Server 2022 and MySQL 8.4: each database ends with the model's 31 tables.
  Existing tables are left as they are; a change to the shape of a table that already exists is not in the scripts.

- **UI packages renamed to `Synvro.Modulus.*` and versioned 1.6.0.** The `modulus-ui` packages (`Ui.Abstractions`, `Ui.Assets`, `AspNetCore.Mvc`,
  `AspNetCore.Hosting`, `AspNetCore.Fluid`, `Blazor`, `Documents`, `Ui.Templates`) are now `Synvro.Modulus.*`, matching the framework and the CLI.
  Applications that reference `Modulus.*` UI packages must change the package ids.

- **Per-category audit retention.** `Security:Retention:CategoryRetention:{category}` gives a category its own age limit (for example
  `authorization` 3650 days). An entry is purged only when it is older than its own category's limit, and a purge still removes only a
  contiguous prefix of a chain. So a long limit on one category also holds back the entries of other categories that come after it. Exact
  separation (one chain per category) is not built: it changes where events are written.

## Upgrade order

1. Back up every database listed below. Stop writes to the authorization and identity databases, or deploy in a maintenance window.
2. Run the scripts for each database, as its schema owner, never as an application user. Each script is idempotent: a rerun changes nothing.
3. Deploy the new release. Start the API first (it owns the permission catalog's first write), then the provisioning host.
4. Verify (see below).

Scripts, by database and engine (`docs/upgrades/`):

| Database | PostgreSQL | SQL Server | MySQL |
|---|---|---|---|
| Authorization | `identity-platform-2026-10.authorization.postgresql.sql` | `...authorization.sqlserver.sql` | `...authorization.mysql.sql` |
| Audit | `...audit.postgresql.sql` | `...audit.sqlserver.sql` | `...audit.mysql.sql` |
| Identity | `...identity.postgresql.sql` | `...identity.sqlserver.sql` | `...identity.mysql.sql` |
| Tenancy | `...tenancy.postgresql.sql` | `...tenancy.sqlserver.sql` | `...tenancy.mysql.sql` |

Commands:

```text
PostgreSQL  psql "$AUTHORIZATION_CONNECTION" -v ON_ERROR_STOP=1 -v api_owner=MyApp.Api -f identity-platform-2026-10.authorization.postgresql.sql
            psql "$AUDIT_CONNECTION" -v ON_ERROR_STOP=1 -f identity-platform-2026-10.audit.postgresql.sql
            psql "$IDENTITY_CONNECTION" -v ON_ERROR_STOP=1 -v user_table=AspNetUsers -f identity-platform-2026-10.identity.postgresql.sql
            psql "$TENANCY_CONNECTION" -v ON_ERROR_STOP=1 -f identity-platform-2026-10.tenancy.postgresql.sql

SQL Server  sqlcmd -S "$SERVER" -d "$AUTHORIZATION_DB" -U "$OWNER" -P "$OWNER_PASSWORD" -b -v api_owner=MyApp.Api -i identity-platform-2026-10.authorization.sqlserver.sql
            (the audit and identity scripts take the same options, with -v user_table=AspNetUsers for identity; the tenancy script takes none)

MySQL       mysql --init-command="SET @api_owner='MyApp.Api'" -h "$HOST" -u "$OWNER" -p "$AUTHORIZATION_DB" < identity-platform-2026-10.authorization.mysql.sql
            mysql -h "$HOST" -u "$OWNER" -p "$AUDIT_DB" < identity-platform-2026-10.audit.mysql.sql
            mysql --init-command="SET @user_table='AspNetUsers'" -h "$HOST" -u "$OWNER" -p "$IDENTITY_DB" < identity-platform-2026-10.identity.mysql.sql
            mysql -h "$HOST" -u "$OWNER" -p "$TENANCY_DB" < identity-platform-2026-10.tenancy.mysql.sql
```

`api_owner` is the API host's assembly name (`PermissionCatalogOptions.Owner`). The authorization, audit and identity scripts were run twice against old-shape tables on
PostgreSQL 17, SQL Server 2022 and MySQL 8.4, and their tables and columns match EF's generated DDL for the same model. The tenancy
scripts (company lifecycle columns and `ModulusTenantDataStores`) were run twice on each engine against an old-shape `ModulusTenants`
table with an existing company; the existing company kept its values. Their types match EF's generated DDL for the same model on all three engines (EF's MySQL type for `TrialEndsAt` is plain `datetime`, which the MySQL script now uses).
`ModulusSettingValues` was run twice on each engine (PostgreSQL 17, SQL Server 2022, MySQL 8.4) against the same old-shape table, and its
columns and primary key were read back from each catalog. Its column types were checked by hand against EF's mapping; they were not diffed against EF's generated DDL.

## Verify after the upgrade

- Each permission catalog row has the API's owner, and the provisioning host's rows appear after its first start.
- `ModulusAuthorizationSeeds` holds `permission-grants`. A database that already had grants was marked as seeded and not reseeded.
- Accounts disabled before the upgrade read as `Disabled`.
- For each audit chain, `VerifyChainAsync` passes for the entries written after the upgrade.

## Generated applications

- **Cookie replay (fix).** Generated web hosts keep the BFF's cookies out of their HTTP clients (`UseCookies = false` on the BFF
  gateway, the BFF session client and the passkey clients). Before this fix one person's BFF session could be replayed on another
  person's request through the same client. Regenerate a web host, or make the same one-line change by hand.
- **Passkeys.** Generated OpenIddict apps register passkey sign-in (`Identity:Passkeys`); development turns it on for localhost.
  The web sign-in page offers a passkey, and `/Passkeys` manages them. Production refuses to start without `Identity:Passkeys:Enabled`,
  and needs the relying-party ID and origins for the deployment.
- **Privileged permissions in production.** Production requires a passkey sign-in (phishing-resistant) for the framework's privileged
  permissions. No generated app maps those admin surfaces yet, so the rule takes effect only where an app wires them.
- **Identity administration.** `identity:accounts:manage` is registered as a Critical permission in every generated OpenIddict app. It is held by the
  `TenantOwner` role only (owner decision, 2026-10-24): the first administrator of a new deployment is made an owner, and a normal
  administrator holds neither account permission. Only an owner grants or revokes the role, and the last active owner cannot be demoted.
  Existing deployments: add `CompanyProvisioning:OwnerPermissions` (`["identity:accounts:manage", "identity:accounts:personal-data"]`)
  to `appsettings.json`, and name an existing administrator with `Identity:Seed:TenantOwnerEmail` (or grant the role on the Users page as an
  owner) once, since seeding only creates the first administrator in an empty database.
  The last active owner also cannot be locked, disabled or archived. A person who belongs to exactly one company acts in it without
  choosing (a multi-company web app); a person in several still picks on the Company page.
- **Production needs certificates.** A production start needs the operator's signing and encryption certificates for the token server.
  Generated apps read them from `Identity:Certificates:Signing` and `Identity:Certificates:Encryption` (`Path` to a PFX file, `Password`
  from User Secrets, the environment or a vault, never committed). A production start stops with the missing setting's name if a path is
  not set. Development certificates are refused in Production.
- **`modulus migrate add` names the module's context.** It passes `--context` to `dotnet ef`, so an application whose startup project
  holds more than one DbContext (for example an authorization store context) can still add module migrations.
- **Production needs migrations.** A production start stops until the authorization and tenancy stores have migrations authored in the
  application (the runbook's step 1). This is deliberate: those stores ship no migrations.

## Company hierarchy (2026-10-24)

- **Parent companies.** A company can have a parent (`ModulusTenants.ParentId`, the tenancy upgrade scripts add the column). A parent link
  does three things: a membership of a company counts for every company below it; a grant or a delegation held by a company applies below
  it, and a deny at the top always applies below; the company's line of ancestors is read when the company is looked up. A parent does not
  change a child's own grants, and a child's grants never reach its parent. Every company on the path up must be active.
- **Read-only inheritance.** A child's administrators see the grants they inherit (`GET .../permissions` returns `own` and `inherited`), but
  change only their own grants.
- **Parent administration (decision 6).** A parent's administrator manages a child's members and grants only with an explicit authority:
  an active row in `ModulusCompanyAuthorityGrants` for that parent, child, account and operation (`ManageMembers` or `ManageGrants`; a
  grant of one never covers the other). The caller must also hold `tenants:children:manage` in the parent (Critical, granted to no role by
  default). Routes: `POST|DELETE .../tenants/{parentId}/children/{childId}/members[/{userId}]` and `POST .../baseline` (needs `ManageGrants`).
  The host administrator (`tenants:manage`) grants and revokes authorities with `POST|GET .../tenants/{id}/children/{childId}/authority` and
  `DELETE .../authority/{grantId}`. Nobody grants authority to themselves. A refusal is `403 CHILD_AUTHORITY_DENIED` with no detail and is
  audited (`company-authority.denied`, with the rule that refused). Grants and revocations are audited too. The check reads the authority
  row on every request. The parent permission goes through the normal grant path, which is cached only when an operator enables `Authorization:GrantCache`.
- **Upgrade (decision 6).** Each of the three authorization scripts creates `ModulusCompanyAuthorityGrants`. Run the authorization script for
  your engine, then grant `tenants:children:manage` only to the roles that should hold it.
- **Upgrade.** Run the tenancy script for your engine. Existing companies get no parent, so nothing changes until a parent is set.

## Known limits and decisions still open

- **MySQL audit history:** the MySQL audit timestamps were stored as `datetime` (whole seconds, UTC) before this release. Their milliseconds are
  already lost, so entries with a non-zero millisecond part cannot be verified again after the upgrade. The MySQL conversion script says so
  before it runs. Record the pre-upgrade chain heads as the proof of those entries before the break.
- **Stored UTC:** the MySQL conversion assumes the stored `datetime` values are UTC. Check that against a known event time before running it.
- **Precision narrowing is lossless** for PostgreSQL and SQL Server, because the security audit hashes whole milliseconds. It was tested
  on a table of the same shape, not on a live application database.
- **Owner-based catalog:** an API restart no longer removes other hosts' permissions. The provisioning permission is not in the API's
  own catalog rows, which is expected.
- **Not in this release:** re-inserting archived audit entries into a live chain (it needs the technical spike the identity plan requires),
  and the owner decisions D7–D10 in the identity plan (pilot module, audit retention durations, NFR targets, MFA assurance matrix).

## Profile details (premodule 3)

- **New self-service routes:** `GET account/profile/details` and `PATCH account/profile/details` (first and last name, photo address, work
  email, job title, department). A field left out keeps its value; an empty string clears it. `PUT account/profile` is unchanged.
- **Administrator route:** `PATCH identity/users/{id}/organization` sets the employee code (upper-case, unique per company) and the primary branch.
- **Upgrade (existing deployments):** the identity user table gains `WorkEmail`, `JobTitle`, `Department` and `PrimaryBranchId`. Add a migration:
  `modulus migrate add <Name> --module Identity`, then `modulus migrate update`.
- **Name clash:** an application whose `ModulusUser` subclass declares `Department`, `JobTitle`, `WorkEmail` or `PrimaryBranchId` fails to
  compile (CS0108). Remove the property from the subclass; the framework property has the same name and type.
- **Work email is confirmed by a link (2026-10-24, breaking for one behaviour).** A work email set through `PATCH account/profile/details`
  (or `PATCH identity/users/{id}/work-email` by an administrator) is *pending* until its owner follows the link sent to it. The response carries
  `pendingWorkEmail`; `workEmail` stays empty until confirmation. Confirm with `POST account/profile/work-email/confirm` (signed in as the account,
  token from the link). **Upgrade path:** an application that displayed `workEmail` straight after a save must show `pendingWorkEmail` as well, and
  must route the link to its own confirm page. **Mail is configured, not coded:** set `Identity:Email:BaseUrl` (the public https address of the web
  application), `Identity:Email:FromAddress` and `Identity:Email:SmtpHost` (with the SMTP password in user secrets or the environment). The mail then
  links to `/ConfirmWorkEmail?token=`. Without `SmtpHost` no mail is sent. Lifetime 30 minutes, one request per minute (`Identity:WorkEmail`).
- **Profile photo (new, 2026-10-24).** `PUT account/profile/photo` (raw image body; JPEG, PNG or WebP by content, at most 5 MB, sides at most 4096 px,
  no animation), `GET account/profile/photo` (the caller's own, as a JPEG), `DELETE account/profile/photo`. The stored photo is a 512 px square
  with no metadata. It needs a file storage (`IFileStorage`): without one the upload answers 503. Deco

### Release gate for premodule 3 (conditions of 2026-10-24)

- **Duplicate employee codes block the upgrade, by design.** Before any change, each profile upgrade script checks for two accounts in one
  company with the same employee code. If it finds one, it stops with an error and changes nothing. Find the duplicates, resolve them, then rerun:
  - PostgreSQL and SQL Server: `SELECT TenantId, EmployeeCode, COUNT(*) FROM AspNetUsers WHERE EmployeeCode IS NOT NULL GROUP BY TenantId, EmployeeCode HAVING COUNT(*) > 1;`
  - MySQL: the same query, with the table name as your schema uses it.
  Resolving means changing one of the codes in the application (an administrator sets it through `PATCH identity/users/{id}/organization`),
  not editing the database by hand. The scripts never delete or rewrite employee data.
- **Index order.** Each script creates the unique index before it drops the earlier non-unique one, so a failure leaves the old index in place.
  Verified on PostgreSQL 16, SQL Server 2022 and MySQL 8.4, on clean data (two runs; data preserved; duplicate in one company refused; NULL and the
  same code in another company accepted) and on duplicate data (the script exits non-zero, adds no column, keeps the old index, creates no unique index).
- **Work-email confirmation limit.** After five failed confirmation attempts on one account, the confirm endpoint answers `429`
  (`WORK_EMAIL_TOO_MANY_ATTEMPTS`), even for the right link, until a new request is made. The counter is `ConfirmationFailures` (a non-null
  integer, default 0). Attempts are counted in the database, so the limit holds across replicas.
- **Confirmation answers (2026-10-24).** `POST account/profile/work-email/confirm` answers with a stable code:
  `200` confirmed; `410 WORK_EMAIL_LINK_EXPIRED` (time passed); `410 WORK_EMAIL_LINK_USED` (already confirmed); `400 WORK_EMAIL_LINK_SUPERSEDED`
  (a newer link or another address, or a cleared address, replaced it); `400 WORK_EMAIL_LINK_INVALID` (not issued for this account and purpose, or damaged);
  `429 WORK_EMAIL_TOO_MANY_ATTEMPTS`. Only guessed or forged links count toward the limit; an expired or used link does not.
  The expiry is inside the signed payload and is checked against the server clock. A signed-in session alone confirms nothing.
- **Web pages (2026-10-24).** The web template has a photo page (`/ProfilePhoto`: upload, replace, remove, preview, progress; type and size checked
  first for a clear message, then by the API) and a confirmation page (`/ConfirmWorkEmail?token=`). The confirmation page consumes the link on POST
  only, so a mail scanner that opens the link does not use it. The configured mail (`Identity:Email`) links to `/ConfirmWorkEmail`.
- **Avatar policy (2026-10-24).** `AvatarUrlPolicy` (Modulus.Identity): the controlled photo first; a legacy `AvatarUrl` only when it is an absolute
  https address with no credentials, no whitespace or control characters, and at most 2048 characters. Anything else falls back to the default
  avatar. External hosts are allowed under this rule; an application that must restrict them checks `IsAllowedExternal` against its own list. The
  browser loads the picture with `referrerpolicy="no-referrer"`; the server never fetches it.
- **Verified address is kept.** A new work-email request does not change the verified address. It changes only the pending address, until the link is
  followed.
- **Photo precedence.** The details response carries `photoUrl` (`/api/account/profile/photo`) when a controlled photo exists. Clients should use
  `photoUrl` first and fall back to `avatarUrl` only when no photo exists. The server never fetches `avatarUrl`. `avatarUrl` is still an https address
  that the client must render under an approved URL policy; that client policy is not in this repository yet.
- **Image decoder (2026-10-24, replaces SkiaSharp).** Profile photos are decoded with SixLabors.ImageSharp 2.1.13 (Apache-2.0, pure managed
  code). A colour profile (ICC) embedded in the upload is refused (`PHOTO_COLOR_PROFILE_UNSUPPORTED`, 422): save a copy without one and upload that.
  This affects photos that a camera or editor stored with a profile. If the decoder cannot start, the upload is refused with 503
  (`PHOTO_PROCESSING_UNAVAILABLE`); validation is never skipped.
- **Advisory risk acceptance (Open).** Five advisories for ImageSharp 2.1.13 have fixes only in the Split-Licensed 4.x line. They are suppressed
  in `Directory.Build.props` with the reasons in `THIRD-PARTY-NOTICES.md`. The owner must accept this risk before the photo feature is released.
- **Third-party notices.** See `THIRD-PARTY-NOTICES.md`.

## Generated web apps: what changes for an app created earlier (2026-10-25)

- **Advisory suppressions.** Apps created before this release warn with NU1902/NU1903 for ImageSharp 2.1.13. Copy the `NuGetAuditSuppress`
  block with its comment from a freshly generated app's `Directory.Build.props` (the reasons are in `THIRD-PARTY-NOTICES.md`). The build is
  then 0 warnings again.
- **Identity column.** `ModulusUser.ExtraProperties` needs the `ExtraProperties` column: run the identity upgrade script for your provider
  (`docs/upgrades/identity-platform-2026-10.identity.*.sql`); it is idempotent.
- **Password recovery.** Apps with sign-in gain `Pages/Account/ForgotPassword`, `Pages/ResetPassword` (route `/ResetPassword`) and
  `Services/ApiClients/AccountApiClient.cs`. Regenerate the web host or copy those files; the API needs no change.
- **Error page and favicon.** New `Pages/Error.cshtml` (+ model) and `wwwroot/favicon.svg`; `PipelineSetup` re-executes `/Error/{code}`.
- **Published UI packages.** The shipped `Synvro.Modulus.Ui.*` 1.6.1 serves its assets from a path that returns 404, so pages render unstyled.
  Generated apps need a UI release that includes the fix in `modulus-ui` (asset base path) and the list empty states. Until it is published,
  generate with a local UI pack (`MODULUS_UI_VERSION` and `MODULUS_LOCAL_FEED`).
