# Framework operations runbook

For the operator of a Modulus application. Each section names the framework API or setting it uses, so the steps can be checked against
the code. Procedures the framework does not carry out itself are marked **operator task**.

## 1. Company lifecycle

A company (tenant) has a status: `Trial`, `Active`, `Suspended` or `Closed`.

- **Change the status:** `TenantManager.SetStatusAsync(tenantId, status, trialEndsAt)`. The change is audited and observers are told.
- **Suspended:** the company is read-only. Any request other than GET, HEAD or OPTIONS gets `423` with `TENANT_SUSPENDED` before it
  reaches the application. A trial past its end is treated as suspended when the company is resolved.
- **Closed:** the company no longer resolves. Its data is kept; nothing is deleted by the framework.
- **Seats:** `SetMaxUsersAsync` caps active members. Adding a member over the cap throws a conflict (`409`). A returning member does not count as new.
- **Offboarding export and retention per company:** not built. This is an **operator task** until a product decision names the export.

Before suspending or closing a company, revoke its people's sessions (section 2) so access ends at once rather than at token expiry.

## 2. Revocation

Each action below takes effect at the next request unless the table says otherwise.

| What to revoke | How | When it takes effect |
|---|---|---|
| All of one person's sessions and tokens | `IUserSessionService.RevokeAllAsync(userId, reason)`; self-service `POST account/sessions/revoke-all` | At once. Cookie sessions and refresh tokens fail their next check, and every stored token is revoked |
| One session or token | `IUserSessionService.RevokeAsync(userId, id)`; `DELETE account/sessions/{id}` | At once |
| A permission grant | Grant APIs (`grants`, `scoped-grants`) | At once on this node. Other nodes follow within the grant-copy duration (D9: 30 s when the deployment sets `Authorization:GrantCache:Duration`; zero means every check reads the database) |
| An access request or delegation | Cancel or end it through the access request API, or the delegation API | At once for the next decision; delegation role snapshots refresh within `DelegationRoleRefreshOptions.Interval` (default 1 minute) |
| An integration (machine) client | `IIntegrationClientDirectory.DisableAsync(clientId)` | Ends it now and revokes every token it holds |
| A company's access | Section 1: suspend or close | At once for requests; sessions still need revoking (above) |

Checks that revocation worked: call the API with the revoked token; expect `401`. The security audit records each revocation.

The revocation objective is 60 seconds across nodes. Cross-node delay is bounded by the settings above, not by token lifetime, except where the table says otherwise.

## 3. Audit verification and restore

The security audit is one SHA-256 hash chain per company (the host chain is `Guid.Empty`). Each chain's head is written to the anchor
sinks (`Security:Audit:AnchorFile`). Records are not edited; corrections are new records.

- **Verify a chain:** `store.VerifyChainAsync(tenantId, anchor?)`. Compare the head against the most recent anchor. A mismatch means the
  stored chain differs from what was anchored.
- **Restore check:** `SecurityAuditRestoreValidator.ValidateAsync(chainId)` reads the live store and the archive and checks that they form
  one chain. An archive that cannot be read back makes the chain unprovable, and the result is invalid. Run it after every restore from a backup.
- **Spool file:** `Security:Audit:SpoolFile` journals each event before it is queued, so a crash does not lose it. Without it, queued
  events can be lost in a crash. Keep it on local disk that survives restarts.
- **Retention:** purge is opt-in (`AddModulusSecurityAuditRetention`) and off by default. The period is `Security:Retention:Retention`
  (provisional configuration value 2555 days; not a legal minimum). Per-category windows are `Security:Retention:CategoryRetention`. A purge removes only a
  contiguous prefix of entries older than their window, and never a legal hold. **Do not enable purge until legal has confirmed the minimum period.**
- **Upgrades:** the audit tables are created by `docs/upgrades/identity-platform-2026-10.audit.*.sql`. The MySQL upgrade changes the chain
  heads, so record the pre-upgrade heads first (see the release notes).

**Operator task:** the audit database and the anchor storage must be separate from the application's write access where your
policy requires it. The framework makes the table append-only with `SecurityAuditDatabaseScripts`; the database role must still be
set up so the application cannot delete rows.

## 4. Key rotation

The framework does not rotate these keys by itself. Each row says what to do and what breaks if the step is skipped.

| Key | Used for | Rotation |
|---|---|---|
| ASP.NET Data Protection key ring | Encrypts personal data columns (`[ProtectedPersonalData]`), BFF server-side tokens, and webhook secrets | **Operator task.** Persist the key ring outside the application (file share, database or Key Vault). Keep the purpose string stable. New keys are added with the old ones kept, so ciphertext under a retired key still decrypts. Losing the key ring makes that data unreadable |
| `PersonalDataProtection:SearchHashKey` | Deterministic hash for equality search over encrypted columns | Changing it changes every hash, so the hash column must be recomputed before queries work. **Operator task** |
| `Security:Redaction:HmacKey` | HMAC of personal data in logs | Changing it changes the logged values. Keep the old key if logs must stay correlated |
| Webhook secrets | Signing outgoing webhooks (Standard Webhooks, `whsec_…`) | `rotate-secret` on the subscription. The previous secret keeps signing for `SecretRotationOverlap`, so receivers can switch without losing deliveries |
| `Platform:WebhookSecret` (AI change hints) | Signing change hints to the AI platform | Rotate in the platform's configuration and the connector's secret store together |
| OpenIddict signing and encryption certificates | Token signing and encryption | **Operator task.** Not verified in this repository. Plan the rotation with the authorization server's own documentation before the first rotation |
| OpenBao (tenant secrets) | Tenant database connection strings and other secrets | Rotate the secret in OpenBao. Confirm the application picks up the new version in a test environment first. **Operator task** for OpenBao's own unseal and token policy (section 5) |

## 5. Premodule administration pages (generated web host)

The web host's administration pages call the API through the BFF. Each page checks one permission in the API; a page without the permission
shows the refusal and changes nothing. Nothing is granted to any role by default unless the table says so.

| Page | Route | Permission | Granted by default | What it does |
|---|---|---|---|---|
| Users | `/Admin/Users` | `identity:accounts:manage` | No | Accounts, invitations, lock and disable, effective access |
| Personal data in the Users page | (field guard) | `identity:accounts:personal-data` | No | Shows names and emails, which are masked without it |
| Roles | `/Admin/Roles` | `identity:accounts:manage` (roles), `authorization:manage` (role permissions) | No | Roles, permissions, deletion when unused |
| Security audit | `/Admin/Audit` | `audit:security:view` | No | Searches the security chain, verifies it, exports CSV |
| Business audit | `/Admin/BusinessAudit` | `audit:view` | No | Searches the company's business entries by entity, actor, action and time |
| Company audit summary | `/Admin/CompanyAudit` | `audit:company-summary:view` | No | The company's own security events in six fields: time, category, action, outcome, actor display name, target. No details, hashes or host-level events |
| Settings | `/Admin/Settings` | `settings:manage`; `settings:global:manage` for global values | No | Registered settings per scope |
| Companies (multi-company apps) | `/Admin/Companies` | `tenants:manage` (Critical) | No | Lists, creates, changes status and seat limit, manages members and parent |
| Profile | `/Profile` | Any signed-in person | Yes | Own display name, language, time zone, phone, email change |
| Company switcher (multi-company apps) | `/Company` | Any signed-in person, membership checked per request | Yes | Chooses the company the next writes act in |
| Branch picker (multi-company apps) | `/Branch` | Any signed-in person; the branch is checked against the caller's organizational scope on every request | Yes | Chooses the branch (organizational unit) the next calls act in; lists only open units inside the scope |

Steps the operator takes:

- **Grant an administrator the account administration permission.** Use the grant APIs or the Roles page. Nothing grants it by default.
- **Turn on the security audit viewer for auditors.** Grant `audit:security:view` to the auditors' role. Nothing grants it by default.
- **Turn on the business audit viewer.** Grant `audit:view` to the role that reads it. Entries are shown for the caller's company only.
- **Turn on security audit retention (operator task).** `Security:Retention:Enabled` is `false` until an operator sets it. The enabled period
  must match the legal advice (see the open legal decision in `docs/PREMODULES_PLAN.md`). Until counsel confirms a minimum, leave it off.
- **Set the baseline grants for new companies.** `CompanyProvisioning:Grants` maps roles to permissions. The baseline may not name an
  unregistered, Critical or wildcard permission, or an `integration:` role. It is empty unless configured.

Migrations for an application that already has users or data (the framework does not run them for you):

- **Profile columns.** `DisplayName`, `Language` and `TimeZone` were added to `ModulusUser`. An application with existing users needs a
  migration on the Identity context: `modulus migrate add <Name> --module Identity`, then `modulus migrate update`.
- **Settings values in multi-company apps.** `ModulusSettingValues` lives in the tenant store. Development creates it when no migration exists;
  production needs a migration on the tenant store before the first start.
- **Company columns.** Run the tenancy upgrade script for your database provider, `docs/upgrades/identity-platform-2026-10.tenancy.{provider}.sql`,
  to add `Status`, `TrialEndsAt`, `MaxUsers` and `ParentId` to existing company tables. `TenancyUpgradeScriptTests` checks that each script
  adds every column the entity maps.

## 6. OpenBao (operator-owned)

These are the operator's responsibility. The framework reads secrets from OpenBao through its KV v2 client. Outside Development it refuses to start when tenant
databases use a development-only secret store (`TenantSecretBackendCheck`); it does not test the OpenBao connection or deploy or unseal OpenBao:

- TLS on every listener, including cluster traffic.
- Raft: five nodes preferred, three as the minimum. A one- or two-node cluster is not highly available.
- The unseal design is Shamir, 3 of 5 shares, with five named custodians (`docs/runbooks/openbao-production.md`, section 5). Auto-unseal is not used. A static key stored next to the application is not acceptable.
- Two independent audit devices, with failure of one tested.
- Kubernetes roles bound to explicit service-account names and namespaces, with access and denial both tested.
- Snapshot and restore drill, with the result recorded.

Production readiness for OpenBao needs the evidence listed in the plan (TLS validation, quorum and recovery, seal and restart
recovery, audit-device failure, snapshot restore, Kubernetes access tests), with the configuration version and an accountable sign-off.
