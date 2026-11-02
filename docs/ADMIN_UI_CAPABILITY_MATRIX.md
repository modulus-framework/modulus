# Framework administration and application settings: capability matrix

Status of the "framework administration UI and application settings" request, measured against this repository (Modulus 1.8.1, audited
2026-10-26). Each item is classified as **Implemented** (working, tested backend in this repo), **Partial** (some backend exists, a
piece is missing), or **Missing** (not in the framework). The request said to use the framework and to note missing features here
rather than build a competing system.

## Where the UI lives

The shared shell, module switcher, navigation, settings screens and account pages are **not in this repository**. They live in the
separate `modulus-ui` repository (`Synvro.Modulus.Ui.*`, `Synvro.Modulus.AspNetCore.*`, page templates in `Synvro.Modulus.Ui.Templates`).
Everything in the UI rows below marked "UI" therefore belongs to `modulus-ui`. This repository provides the APIs, contracts and
validation those screens call. This was verified: `modulus-ui/src` contains only the UI packages above.

## Capability matrix

| # | Area | Status | What exists (repository and file) | Gap |
|---|------|--------|-----------------------------------|-----|
| 3 | Shared application shell | UI, Partial | `modulus-ui` (layouts, `ui add-admin`, menu endpoint `/_ui/menu`, filtered per caller) | Module switcher, tenant switcher and global search are not confirmed in `modulus-ui`; not checked here |
| 4 | Module-wise navigation contract | Partial | `/_ui/menu` filtered per caller (`modulus-ui`); `IEntityUiRegistry` for extension fields | No framework contract for a module to contribute navigation groups, icons, order and localization keys. Needed in `Modulus.Core` and consumed by `modulus-ui` |
| 5 | Module switcher | Missing | `IModuleLoader` / `ModuleContext` (`src/core/Modulus.Core`) and `modulus describe` list modules | No runtime "installed + permitted + entitled" module list endpoint for a switcher |
| 6 | Tenant management (CRUD, status, lifecycle) | Implemented (API) | `TenantAdminEndpoints`, `TenantManager` (`src/platform/Modulus.MultiTenancy.EntityFrameworkCore`): create, list, status (`SetStatusAsync`: Trial, Active, Suspended, Closed), `SetMaxUsersAsync`, members. `TenantDataStoreProvisioner` writes tenant database connection strings to OpenBao (`SecretReference`) | Tenant UI pages are in `modulus-ui`. Search, filter and sort on the tenant list are not confirmed against the API |
| 7 | Tenant switcher | Partial | `TenantMiddleware`, membership required by default, `X-Tenant-Id` / `tid` claim, `RequireMembership`, `423 TENANT_SUSPENDED` | No endpoint that lists the caller's switchable companies for a header dropdown is documented. Client cache invalidation is a UI concern |
| 8 | User management | Implemented (API), Partial (verification) | `UserAdminEndpoints` (`Modulus.Identity`): list with `search`, `status`, `role`, `page`, `pageSize`; CSV export; get one (field-authorized); effective access; invitations; sessions (list and revoke); login history; roles (`PUT /{id}/roles`); lock and unlock. `ModulusUserManager` for disable and delete | Email verification flow and phone verification are not confirmed; phone verification is not built (AGENTS.md). Account activation through invitation exists (`account/accept-invitation`) |
| 9 | Role management | Implemented (API) | `RoleAdminEndpoints`: list, get, create, rename, delete (`RoleManager`), permission registry listing; composite roles (`/authorization/roles/inclusions`), positions (`/authorization/positions`). `integration:` role names are reserved | Built-in protection of system roles and a dedicated "assigned users" endpoint for a role were not verified. Protecting `Admin` and sensitive grants relies on `authorization:grant-any` and the grant caps; confirm before exposing role delete in the UI |
| 10 | Permission management (catalogue, grants) | Implemented (API) | `Modulus.Authorization.Management` admin API, `grants`, `scoped-grants`, `assignments`; permissions registered by modules via `AddPermissions`; Critical sensitivity; `authorization:manage` | Reusable permission-tree component is UI (`modulus-ui`) |
| 11 | Permission and data scopes | Implemented | `IPermissionScopeResolver`, `IScopeEnforcer`, `AddScopeMap<T>`, `ForExport`, `EnsureAllInScope`, `Own/Assigned/OrgUnit/Tenant` scopes, `WithinApprovalAuthority` | Scope configuration screens are UI. Effective-scope summary per user exists as the effective-access report, not as a UI |
| 12 | Field-level protection | Partial | `IFieldAuthorizer` (used by `UserAdminEndpoints` to shape user responses and exports), `AuditableActions.FieldWrite`, `AccessReasonCodes.FieldProtected`, `[ProtectedPersonalData]` encryption, log redaction, AI masking | Coverage is per endpoint: each response must call the authorizer. No central declaration of read, write and mask rules per field |
| 13 | Authentication and passkeys | Implemented (API) | `PasskeyService`, `AddModulusPasskeys` (Fido2, UV required, `Identity:Passkeys`), `MapModulusPasskeys`; password grant; 2FA authenticator app with recovery codes; step-up (`amr`); sessions with `sid`; SAML; LDAP | Passkey enrolment page and its reauthentication rule are UI. Password recovery endpoints exist in the account controller |
| 14 | Profile and account management | Partial | `ModulusAccountController`: `account/sessions`, `account/login-history`, `account/2fa/*`, `account/accept-invitation`, passkeys | No profile GET/PUT for personal information, avatar, language and time zone. Preferences exist as user-scoped settings (`Branding.ThemeMode` is `User`-scoped) |
| 15.1 | Settings dashboard | Partial | `GET /api/settings` (`MapModulusSettingAdministration`) returns every definition with its effective value, source and group | Dashboard screen is UI |
| 15.2 | General application settings | **Implemented in this change** | `ApplicationSettingsCatalog`: name, short name, subtitle, description, support email and URL, public website, terms, privacy, legal notice (URL-validated), maintenance message | Default landing page, maintenance mode switch and its behaviour are not wired (no maintenance middleware) |
| 15.3 | Branding assets (logos, favicon, background) | **Partial, backend in this change** | `IBrandingAssetStore`: content-sniffed PNG/JPEG/GIF/WebP/ICO, size cap, SVG refused, server-generated keys, tenant-scoped folders, via `IFileStorage` | Upload endpoint and asset-reference settings are not built. Dimension checks, crop/fit, and cache invalidation are not built. Public serving path for branding images is not defined (private storage is the safe default) |
| 15.4 | Theme and appearance | **Partial, in this change** | `Branding.PrimaryColor`, `SecondaryColor`, `AccentColor` (validated `#rgb`/`#rrggbb`), `ThemeMode` (light/dark/system), `BorderRadius` presets | Typography and density choices, and applying values to the UI, are not built (`modulus-ui`) |
| 15.5 | Login-page branding | Missing | None | Needs login background asset setting and text settings (text settings can use the general catalog) |
| 15.6 | Localization and regional | **Partial, in this change** | `Localization.DefaultLanguage`, `DefaultTimeZone` (catalog, no format validation yet) | Culture and time-zone identifiers are not validated server-side yet; enabled-language list, number and currency format settings are not built |
| 15.7 | Email settings | Partial | `Email.SenderName`, `SenderAddress`, `ReplyToAddress` (catalog, validated email). `IIdentityEmailSender` / `IIdentityEmailQueue` in Identity | **No general email delivery abstraction** (no SMTP/provider config, no test-email action, no template management). Per the request, this needs a new framework abstraction: noted, not built |
| 15.8 | Notifications | Partial (in-app only) | Notification center in `Modulus.Platform/Notifications` (`INotificationPublisher`, `INotificationStore`), now with categories, per-person channel preferences (`INotificationPreferenceStore`), `INotificationSender` and `INotificationInbox` for the signed-in person. Only the in-app channel delivers | Email, SMS and push channels are not implemented and are not offered. Store is in memory; a durable EF store is needed for production. No HTTP endpoints yet |
| 15.9 | File storage and uploads | Implemented (storage), Partial (settings) | `IFileStorage`, `LocalFileStorage`, `S3FileStorage`, `AzureBlobFileStorage`, `SizeLimitedFileStorage`, `TenantScopedFileStorage`, `StorageOptions` | No admin settings for permitted types, upload limits, retention, link expiry. Limits live in configuration, not in the settings registry |
| 15.10 | Integrations and external services | Partial | `Modulus.Secrets.OpenBao` (secret references), `Modulus.Identity` providers (OIDC adapters), `Modulus.Webhooks`, `Modulus.AI.Connector`, `Modulus.Realtime.Redis` | No central integration registry with provider selection, per-provider schema and a test-connection action. Each integration is configured in its own options section |
| 15.11 | Authentication and security settings | Partial | Policy in configuration: `RateLimiting`, `Identity:Passkeys`, `Identity:Sessions`, `Identity:StepUp`, password policy and history (`EfPasswordHistoryStore`), lock-out | These are configuration options, not settings-registry entries, so they are not editable from the admin UI. Left as deployment policy on purpose: the request asks that mandatory protections stay out of UI controls |
| 15.12 | Modules and feature settings | Partial | Entitlements admin API (`MapEntitlements`, permission `authorization:entitlements:manage`: plans and per-company feature overrides), `IFeatureEntitlementStore`, `IFeatureEntitlementResolver`, `FeatureGate`, feature flags (`Microsoft.FeatureManagement`) | The default store is in-memory (`InMemoryFeatureEntitlementStore`); an EF store for production was not found. Module install and enable state is not a managed setting. Subscription limits: `TenantInfo.MaxUsers` only |
| 15.13 | Audit, retention, operational | Partial | `ISecurityAuditLog`, `Modulus.AuditLogging.EntityFrameworkCore` (hash-chained, append-only), `ISecurityAuditStore.QueryAsync` | No audit-log admin query API for a UI page beyond login history. Retention rules are not enforced by the framework; no maintenance-mode toggle |
| 15.14 | Module-contributed settings | **Implemented in this change** | `ISettingDefinitionContributor` (contract), `AddModulusApplicationSettings()`, registry built from contributors once; last definition wins | Typed editors per setting and a `Sensitivity` tier beyond `IsSensitive` are not built |
| 15.15 | Scope and inheritance | Implemented | `SettingScope` (Global/Tenant/Branch/User), `SettingScopes` per definition (`AllowedScopes`), resolution user → branch → tenant → global → default, `SettingSource`, host pins | Already in place; catalog uses scope restrictions (for example sender address is Global only) |
| 15.16 | Validation, history, change audit | Partial | Server-side validation (`SettingValueValidator`, URL, email, colour added here), `IsSensitive` masking in every admin read, scope checks, pins, `ISettingChangeNotifier` | **No settings change audit.** Setting writes do not call `ISecurityAuditLog` yet. Next step: record name, scope, and old/new for non-sensitive values only |
| 16 | Settings architecture and secrets | Partial | Registry, scopes, validation, cache (`CachedSettingStore`, `SettingCacheVersion`), EF store (`EfSettingStore`), OpenBao client, and `SecretReference` used for tenant database secrets | Settings do not yet store OpenBao references. `IsSensitive` masks reads, but a sensitive setting's value is still stored in the settings table. Real credentials should go to OpenBao, which is not wired to the settings registry yet |
| 17 | Remaining administration pages | Backing APIs exist; screens are UI | Tenants, users, roles, permissions and scoped grants, approval authorities, access requests and break-glass, delegations, governance (recertification), entitlements, org units and company profiles, sessions, login history, audit (`ISecurityAuditStore.QueryAsync`) | Module registry endpoint missing (see section 5). Audit query has no HTTP endpoint for an admin screen. Dashboards are UI |
| 18 | UI and UX requirements | UI | `modulus-ui` | Out of this repository |
| 19 | Security and architecture | Implemented | Fail-closed authorization, closed-by-default fallback policy, security guard, tenant write guard, branch scope, outbound address guard, step-up | Applies to new code (see tests) |
| 20 | Tests | Partial | See "Tests added" below | Authentication flows, user/role admin, tenant switching HTTP tests, and audit-of-settings are not covered by new tests |

## Built in this change

- **Settings value types** (`SettingValueType`): `Url` (absolute http/https, no user info), `Email`, `Color` (`#rgb`/`#rrggbb`). Appended at the end, so
  stored numeric values are unchanged.
- **Sensitive settings** (`SettingDefinition.IsSensitive`): an administrator's read shows `SettingMasking.Mask` only when a scope or pin stores
  the value, and never shows a definition default for a sensitive setting. Server code still reads the real value through `ISettingManager`.
- **Settings contribution contract** (`ISettingDefinitionContributor`): the registry is built once from every registered contributor, in order.
  `AddModulusApplicationSettings()` adds the framework's catalog (`ApplicationSettingsCatalog`) and is opt-in.
- **Branding asset store** (`Modulus.Branding`): `IBrandingAssetStore.UploadAsync` reads at most `MaxBytes + 1`, detects the format by magic bytes
  (PNG, JPEG, GIF, WebP, ICO; SVG refused), stores via `IFileStorage` under `branding/{company or host}/{slot}/{random}.{ext}`, and returns the
  key. `RemoveAsync` deletes only keys under `branding/` without `..`.

## Modules added (2026-10-26)

- **Media library** (`src/platform/Modulus.Media`, `Modulus.Media.Tests`): `IMediaLibrary` uploads, lists, opens and deletes files of the
  current company. Images use the branding content check (PNG, JPEG, GIF, WebP, ICO) and PDFs are identified by `%PDF-`. SVG and renamed
  scripts are refused. Display names keep only the last path segment and drop control characters. Storage keys come from the media id
  and the company (`media/{company or host}/{id}.{ext}`). Metadata and bytes are removed together, and failed metadata writes remove the
  bytes. Permissions `media:read` and `media:manage` are registered. Metadata is held in memory by default (`InMemoryMediaStore`): an
  EF store is the next step. No HTTP endpoints yet. Tests: 17 passing (net8.0 and net10.0).
- **Notification center extensions** (`src/platform/Modulus.Platform/Notifications`, tests in `NotificationInboxTests.cs`): notifications
  carry a category; channel preferences per person, per company and per category; `INotificationSender` skips muted channels;
  `INotificationInbox` acts only on the signed-in person's notifications in their company. A notification from another company cannot be
  marked read by its recipient. Tests: 13 added; the Platform suite has 640 passing (net8.0 and net10.0).
- Note: a separate `Modulus.Notifications` project was drafted and removed. The framework already had that namespace, so the work
  extends the existing center rather than duplicating it.

## Continued (2026-10-26, second pass)

- **HTTP endpoints.** `MapModulusMedia()` (`src/platform/Modulus.Media/MediaEndpoints.cs`): `POST /api/media` (multipart `file`, needs
  `media:manage`), `GET /api/media` (search, kind, paging; `media:read`), `GET /api/media/{id}`, `GET /api/media/{id}/content` (served with the
  detected type and `X-Content-Type-Options: nosniff`), `DELETE /api/media/{id}` (`media:manage`). The storage key is never in a response.
  `MapModulusNotifications()` (`src/platform/Modulus.Platform/Notifications/NotificationEndpoints.cs`): the signed-in person's inbox
  (`GET`, `unread-count`, `{id}/read`, `read-all`) and channel preferences (`preferences/{category}/{channel}`). No route takes a person id.
  Only delivered channels are accepted (`InApp`); other names return 400.
- **Durable stores.** `Modulus.Media.EntityFrameworkCore` (`AddModulusMediaStore<TContext>()`) and `Modulus.Notifications.EntityFrameworkCore`
  (`AddModulusNotificationsStore<TContext>()`). As in the audit package, the application derives its own DbContext
  (`ModulusMediaDbContext`, `ModulusNotificationsDbContext`), so the **migrations are generated in the application**
  (`dotnet ef migrations add ... --project <app infrastructure>`); the framework ships none. Timestamps are UTC-tick columns: SQLite cannot
  order by `DateTimeOffset`, and EF Core 8 cannot apply value converters in `ExecuteUpdate`, so the read claim is a conditional update that
  works on net8.0 and net10.0. The media search is case-insensitive on every provider.
- **Tests added this pass.** Media endpoints (`MediaEndpointTests`, 3 cases, with a policy that refuses `media:manage` → 403), notification
  endpoints (`NotificationEndpointTests`, 11 cases, anonymous 401, cross-person and cross-company isolation, atomic read, preferences, 400 for
  undelivered channels), Media EF (`EfMediaStoreTests`, 5), Notification EF (`EfNotificationStoreTests`, 6, including the registration
  check that the EF stores replace the in-memory ones). Platform suite: 651 passing; media: 20; media EF: 5; notification EF: 6 (net8.0 and net10.0).

### Third pass (2026-10-26)

- **Settings change audit (15.16).** `SettingAdministration` records one `SecurityAuditEvent` per successful change
  (`settings` / `setting.changed` or `setting.removed`): the scope, the actor, the company chain, and the effective value before and after.
  A sensitive setting is recorded as changed with no values at all. A refused change records nothing. Without an audit log the change still
  works. Tests: `SettingAuditTests` (5). Note: "before" is the effective value, not the value at that scope alone.
- **Branding upload and serving (15.3).** `MapModulusBranding()` (`src/platform/Modulus.Platform/Branding/BrandingEndpoints.cs`):
  `POST /api/branding/{slot}` (a company's image, `settings:manage`), `POST /api/branding/platform/{slot}` (the installation image,
  `settings:global:manage`), the matching `DELETE` routes to restore the default, and `GET /api/branding/{slot}` (anonymous, so the sign-in
  page can show it). Each slot has a setting `Branding.Asset.{slot}` that holds the stored key, so the image inherits and is audited like
  any other setting. Replacing or restoring removes the file only when that scope owned it. The read route opens a file only for a key that
  matches the exact pattern the upload writes (`BrandingAssetStore.IsIssuedKey`), so a settings value cannot point it at other storage.
  Tests: `BrandingEndpointTests` (8). The platform suite now has 664 passing tests (net8.0 and net10.0).

### Still missing (not built)

- Email, SMS and push channels: no email abstraction exists in the framework (only Identity's password and invitation mail).
- Migration files for the EF stores: intentionally generated in each application (see above).
- The UI adapters that connect `modulus-ui` pages (`IAdminSettingsStore`, `IUserDirectory`, ...) to these APIs.
- Settings change audit, the branding upload endpoint, profile endpoints, and an integration registry with OpenBao references (see the next steps above).

## Tests added

- `tests/unit/Modulus.Platform.Tests/BrandingAssetStoreTests.cs` (7 tests): PNG and JPEG accepted by content, SVG refused, script renamed
  to PNG refused, oversize and empty refused, server-generated tenant-scoped key, remove refuses foreign keys.
- `tests/unit/Modulus.Platform.Tests/ApplicationSettingsTests.cs` (20 test cases): URL, email and colour validation through the real
  manager, theme list, sender address scope, company override and isolation, sensitive masking in the list and raw value on the server,
  sensitive default never listed, contributor replacement.
- Result: `dotnet test tests/unit/Modulus.Platform.Tests --filter Category=Unit`: 627 passed, 0 failed, on net10.0 and net8.0.

## Next steps, in priority order

1. Record settings changes in the security audit (name, scope, old/new for non-sensitive values, no values for sensitive ones) — 15.16.
2. Branding upload endpoint with the existing `settings:manage` permission (company slots) and `settings:global:manage` (installation slots),
   storing the returned key as a setting value; add dimension checks and a serving endpoint for public assets.
3. Validate `Localization.DefaultLanguage` (culture) and `DefaultTimeZone` (time-zone id) server-side.
4. A general email abstraction (`IEmailSender` with SMTP/provider implementations, test-send with rate limit, template settings) — 15.7. Needs a
   new framework contract; not built.
5. Notification channel abstraction — 15.8. Not built.
6. An EF-backed feature-entitlement store for production (the entitlements admin API exists; its default store is in memory) — 15.12.
7. Module registry endpoint (installed, enabled, permitted, entitled) for the module switcher — section 5.
8. Navigation contribution contract in `Modulus.Core` for `modulus-ui` — section 4.
9. Profile endpoints (personal information, language, time zone) — section 14.
10. Integration registry with credential references in OpenBao and a test-connection action — 15.10.

## Verified facts used above

- `Modulus.Platform/Settings`: `SettingManager` precedence user → branch → tenant → global → default; `SettingAdministration` only writes registered names.
- `SettingsAdministrationEndpoints`: `settings:manage` (Sensitive), `settings:global:manage` (Critical).
- `Modulus.Platform/Storage`: `IFileStorage` has local, S3 and Azure implementations; `TenantScopedFileStorage` isolates tenants.
- `Modulus.Identity/Extensions`: `UserAdminEndpoints`, `RoleAdminEndpoints` (user and role APIs listed above).
- `Modulus.Authorization.Management`: entitlements, delegations, governance endpoints.
- `modulus-ui` contains UI packages only (`Modulus.Ui.*`, `Modulus.AspNetCore.*`, `Modulus.Blazor`, `Modulus.Documents`).
- No general email sender or notification abstraction exists in `src/` (only Identity's password and invitation mail).
