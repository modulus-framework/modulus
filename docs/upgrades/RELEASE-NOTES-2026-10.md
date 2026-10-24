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
- **Identity administration.** `identity:accounts:manage` is registered as a Critical permission in every generated OpenIddict app and
  granted to no role. Generated apps expose no identity-account administration route. A route added later must map to it and fail closed.
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
