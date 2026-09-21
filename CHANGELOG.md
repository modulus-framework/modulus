# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — authorization-code + PKCE flow (Modulus.Identity, Modulus.Cli)
- `Identity:AllowAuthorizationCodeFlow` now works: `ModulusAuthorizeController` serves `/connect/authorize`, signs the user in through the app's own Identity cookie login (`/account/login`, returning to the
  same request; `prompt=login` and `max_age` force a new sign-in, `prompt=none` answers `login_required`) and issues a one-time code, which `/connect/token` redeems (`ModulusTokenController` routes the
  code grant through the same re-verification as a refresh: active user, lock-out, security stamp, current roles). PKCE is mandatory (`RequireProofKeyForCodeExchange`), consent is implicit
  (first-party clients). A mobile, desktop or single-page app therefore never handles the password.
- A web app with `--auth openiddict` turns the flow on and lists `Identity:Seed:RedirectUris` in Development (`{app}://callback`, `http://localhost:5173/callback`). `IdentitySeeding` brings the first-party client in
  line with those settings on every start (authorization endpoint, code grant, PKCE requirement, exact redirect URIs), so adding a redirect URI is a settings change. A web app with the token server always
  gets the Identity UI, which is its sign-in page (`NewAppCommand.WithSignInPage`).
- Verified end to end on a freshly generated web app: anonymous authorize is sent to the login page, sign-in returns to the request, the code is redeemed with the verifier, the token calls the gated API
  (200/201), refresh works, a replayed code is rejected (and revokes what it issued), a wrong verifier, a missing challenge and an unregistered redirect URI are refused.

### Added — generated APIs require the CRUD permission (Modulus.Cli)
- With the identity backend, every endpoint of a generated CRUD set declares `Permissions("{module}:{route}:manage")`, the same permission that guards the entity's admin page, and the `Admin` role holds it
  (`AddModulusAuthorization`, `AddPermissions`, `AddPermissionGrants` in `Program.cs`, also for an API app and for a later `generate-crud`). Anonymous callers get 401, a signed-in caller without the role 403.
  Verified on a generated web app and API app. Hosts without an identity backend keep endpoints as open as the host (`--auth none` still fails closed).
- The generated test project signs in as `Admin` and checks the gate (401 / 403 / 200).
- `generate-crud` keeps an existing app in step: a second entity in a module gets its own sidebar item and manifest feature (`UiNavSidecar`), and an admin page generated before permissions gets its
  `[Authorize(Policy = ...)]` guard (`UiAccessGates.EnsurePageGuard`). Program.cs lines follow the file's own indentation and order.

### Fixed — generated app tests and the Testing harness (Modulus.Testing, Modulus.Cli)
- The generated integration tests passed only by accident of ordering: `ModulusWebAppFactory` opened the SQLite keep-alive after Program.cs had already migrated and seeded, and never forced its test scheme as the
  default (so a host with its own default scheme answered 401). Keep-alives are now owned by the database registry and opened when a context's options are first built; the test scheme is forced as default.
- The generated app ships `appsettings.Testing.json` (throwaway certificates, password grant) for the token server, `!appsettings.Testing.json` in `.gitignore`, loads its own assemblies before the module-boundary
  rules scan, and its example integration event carries `[IntegrationEventName(...)]`.

### Added — app kind: `api` or `web` (Modulus.Cli)
- `modulus app --kind api|web` (asked interactively; `--ui-modules` alone implies `web`; a non-interactive run defaults to `api`). **`api`** creates a host with
  no UI at all (no Razor Pages, theme or UI packages). **`web`** creates the web app **and** keeps the API, so the same modules serve the UI and external
  clients (mobile, desktop, other systems). A web app always gets the UI foundation (`UI.Core`, the Tabler theme, `Platform`) even with no prebuilt module;
  `--ui-modules none` is now accepted (the help always said so). `--kind api --ui-modules …` is an error.
- The kind is recorded in the host project as `<ModulusAppKind>`. `generate-crud` scaffolds the admin page **by default for `web`** (`--no-ui` for the
  API side only), never for `api` (`--with-ui` is refused, with the one-line way to change the kind), and keeps the opt-in `--with-ui` for a host without the
  property (generated before app kinds). `ui add` and `ui eject` / `ui diff` refuse an `api` host. `modulus info` shows the kind.
- `modulus app` says what the chosen auth provider means: `--auth none` warns that no authentication scheme is registered while endpoints require an authenticated user
  (every API call answers 500 until one is added), naming external clients for a web app, and the generated `Program.cs` carries a comment.
- Fixed: `EnsureCsprojPackageReference` glued the closing `</ItemGroup>` to the new `PackageReference` line; `ModuleDiscovery.Inventory` threw when `src/API` was missing.

### Added — extension fields over the API for web apps (Modulus.UI.Core, Modulus.Cli)
- `EntityApiFields` (`Modulus.UI`): `VisibleExtraProperties`, `ValidateEntityFieldsForApi` and `ReadSubmittedEntityFieldText` give an API endpoint the same registry and per-field permission
  filter the admin page has. A field the caller may not see reads exactly like one that does not exist (`Unknown extension field 'X'`), an update only touches the fields it sends, and an
  empty value clears one. They are separate from `EntityFieldValues` because a form always posts every field, an API caller only what it changes.
- In a web app, `generate-crud` (and the example module of `modulus app --kind web`) now generates an API that carries `extraProperties` on create, update and read, validated and filtered through
  the registry: a 400 with an `errors` list for bad or hidden fields, and only visible fields returned. The DTO becomes a record with the (unfiltered) stored bag that the endpoints filter; the
  Presentation project references `Cobytelabs.Modulus.UI.Core`. Only for a fresh set (files are never overwritten); an API-only app never exposes the bag.
- Verified end to end on a generated web app: create with a field 201, out-of-range 400, gated field 400, GET shows only visible fields, update leaves unsent fields alone, an empty value clears.

### Added — identity backend for `--auth openiddict` (Modulus.Cli)
- `modulus app --auth openiddict` (either kind) generates an infrastructure-only `Identity` module (`AppIdentityDbContext`, design-time factory, `IdentityModule`, `IdentitySeeding`) so the
  local token server has users, roles and a token store. It is found by `modulus migrate` (`migrate add InitialCreate --module Identity`) and ignored when `generate-crud` picks a module.
- Startup seeds the `Admin` role and a public first-party client (`client_id` = the lower-cased app name); the first administrator is created only when no user exists and either
  `Identity:Seed:AdminEmail`/`AdminPassword` are configured or, in Development only, as `admin@{app}.local` with a random password logged once. Development turns
  `Identity:AllowPasswordFlow` on; the base settings leave it off.
- A web app gets `AddModulusSmartAuth()` (bearer for the API, cookie for pages), an API app makes the bearer scheme the default. Verified end to end: a token from `/connect/token`
  authenticates `POST`/`GET /api/...`, refresh and revoke work, anonymous calls get 401, and a web app's UI shows what the API created.
- Fixed: generated Create/Update/Delete and `generate-command` commands declare `[Transactional(typeof(IUnitOfWork))]`. With two `DbContext`s registered (any second module, or the
  identity database) they failed as "ambiguous transaction scope", so every generated POST answered 500.
- Fixed: the generated test project pins `System.Security.Cryptography.Xml` (a transitive high-severity advisory) when OpenIddict is chosen.

### Fixed — `AddModulusOpenIddict` order and smart auth (Modulus.Identity, Modulus.UI.Core)
- `AddModulusOpenIddict` now `TryAdd`s its deny-everything password validator, so calling it after `AddModulusIdentity` no longer clobbers the real one (every password grant used to
  answer "username or password is incorrect").
- `AddModulusSmartAuth` now sets the authenticate and forbid defaults too (via `PostConfigure`, whichever of it and `AddIdentity` is registered first): `AddIdentity` sets its own
  cookie as the authenticate default, which beat `DefaultScheme`, so bearer callers were never authenticated (403).

### Added — extension-field storage: `IHasExtraProperties` (Modulus.Core, Modulus.EntityFrameworkCore, Modulus.UI.Core)
- `IHasExtraProperties` (`Modulus.Core.Abstractions.Entities`) marks an entity whose extension-field values live in an
  `ExtraProperties` dictionary of invariant-culture text. `ModuleDbContext` maps it as a required JSON text column on every implementing
  entity (`UseModulusExtraProperties`), with a content-based value comparer so in-place edits are saved; a new entity stores `{}`.
- `entity.SetExtraProperties(values)` merges (null or empty removes the key, unmentioned keys are kept) and
  `entity.GetExtraProperty(name)` reads one value.
- UI side: `registry.ReadEntityFieldText(entity, user, Input.Extra)` returns the visible fields as canonical text (null for an emptied
  field) for `SetExtraProperties`, and `EntityField.ToText(value)` is the canonical text form (round-trips through `TryConvert`).
  A field the user lacks the permission for is left out, so saving cannot set or erase it.
- Adding the marker to an existing entity needs a migration for the new `ExtraProperties` column (not null, default `'{}'`).

### Added — `modulus ui eject` and `modulus ui diff` (Modulus.Cli)
- `modulus ui eject <Component>… | --all` copies a framework component view (`Card`, `DataTable`, `Input`, …; `--list` shows them) into the app at
  `Views/Shared/Modulus/{Component}/Default.cshtml`, where it takes precedence over the theme and the framework default with no registration. It also
  creates the folder's `_ViewImports.cshtml` (`@using global::Modulus.UI` + tag helpers) when missing. Existing files are skipped unless `--force`.
- The first line of an ejected file records the framework version and a hash of the original, so `modulus ui diff [Component]` can say whether an override is
  *identical*, *customized*, *outdated* (the framework changed since and you did not), or in *conflict* (both changed), and prints the line diff.
  `--check` exits 1 for outdated/conflict, for CI after a framework upgrade. `--summary` lists statuses only.
- The views are embedded in the CLI from the packages (which ship them compiled). Beyond components, `ui eject` takes an installed **feature UI** (`Users`, `Identity`,
  `Tenancy`, `Permissions`, `Settings`, `AuditLogging`, `Notifications`, `Files`), Core's `Shared` partials, the **theme** (`Tabler`: layouts, shell partials, error pages), or a
  single view (`Users/Details`, `Account/Login`, `Tabler/Layouts/Application`), so no Modulus UI is out of the app's reach. A page's handlers stay in the package: the app owns
  the markup. The command writes the nearest `_ViewImports.cshtml` too (an ejected view compiles in the app's assembly) and refuses a UI the app has not installed
  (`modulus ui add X`). `ui diff [target]` classifies pages and theme views the same way.
- Verified end to end: all 20 component views, then all 66 views (components, eight feature UIs, Core partials, theme) ejected into a generated web app and built with 0 warnings;
  the overrides won at runtime with the right layouts and the ejected login signed in; `ui diff --check` reported customized, conflict and identical and exited 1.
- New `Modulus.UI.Ejection.Tests` compiles every ejectable view in a Razor host (as an app's assembly would) and asserts the app copy beats the package's.

### Fixed — feature UIs rendered in another package's layout when installed together (Modulus.UI.*)
- Each feature UI shipped `Pages/_ViewStart.cshtml` (and `_ViewImports.cshtml`) at the same virtual path; with several installed the first won for all, so with Identity
  present every admin page rendered in the login-card `Account` layout. They now live in each package's own page folders (`Account`, `Users`, `Roles`, `Tenancy`,
  `Permissions`, `Settings`, `AuditLogs`, `Notifications`, `Files`; Core: `Pages/Shared`). Regression test in both registration orders.

### Added — `AddModulusPageAuthorization()`; generated web apps no longer serve admin pages to anonymous visitors (Modulus.UI.Core, Modulus.Cli)
- A feature UI gates its pages only when the app sets `RequirePermission` (default: open), so a freshly generated web app answered `/Users` with the user list to
  anyone. `AddModulusPageAuthorization(params string[] anonymousFolders)` requires a signed-in user for every Razor Page except `/Account` (or the folders you name);
  a feature UI's `RequirePermission` still applies on top. The web app template calls it after `AddModulusSmartAuth()`; API hosts have no pages and do not.
- Implemented with `PostConfigure<RazorPagesOptions>`: `AddRazorPages()` replaces the conventions collection, so a `Configure` registered before it is lost.

### Added — admin UIs of a generated web app are restricted to administrators (Modulus.Cli)
- A sign-in is not enough for the admin pages: any signed-in user could open `/Users`. `modulus ui add` and `modulus app --ui-modules` now set the UI's
  `RequirePermission` in `appsettings.json` (`UsersUi`, `TenancyUi`, `PermissionsUi`, `SettingsUi`, `AuditLoggingUi`, `FilesUi`), register `AddModulusAuthorization()` and grant
  the permission to the `Admin` role in `Program.cs`. Identity and Notifications stay open to any signed-in user. Skipped for a host with no sign-in (nobody could hold the
  permission), and an existing `appsettings.json` section is never rewritten.
- Verified on a generated web app: the administrator gets 200, a self-registered user is redirected to the access-denied page, an anonymous visitor to the login page.

### Added — `generate-crud --with-ui` pages require a permission (Modulus.Cli)
- In a host with the sign-in (a generated web app) the admin page for `Catalog.Product` requires `catalog:products:manage`: `[Authorize(Policy = ...)]` on the page, the nav item
  hidden without it, the permission declared in the registry (listed by the Permissions UI) and granted to the `Admin` role in `Program.cs`. A host without a sign-in is unchanged.
- The wiring for the admin UIs and CRUD pages also registers `AddGrantStorePermissionChecker()`, so the sidebar shows the gated items to a user whose role holds the permission.

### Fixed — `AddModulusIdentity` left `ICurrentUser` as the anonymous default (Modulus.Identity)
- `AddModulus`, `AddMediator` and `AddModulusUi` register the fail-closed `NullCurrentUser` with `TryAdd`; one registered first turned `AddModulusIdentity`'s `TryAdd` of
  `ClaimsPrincipalCurrentUser` into a no-op, so in a generated web app even the signed-in administrator was anonymous to menu filtering, entity-field permissions and audit.
  `AddModulusIdentity` now replaces that default (and only that default; an app's own `ICurrentUser` is kept).

### Changed — `TablerShell` and `TablerAssets.IsAlpine` are public (Modulus.UI.Theme.Tabler)
- Ejected theme views compile in the app's assembly and call them.

### Added — `generate-crud` supports extension fields on the create form (Modulus.Cli)
- A generated entity now implements `IHasExtraProperties` (JSON `ExtraProperties` column, no per-field schema change), and
  `Create{Entity}Command` takes an optional `ExtraProperties` that its handler merges into the entity with `SetExtraProperties`.
- With `--with-ui` the create form renders `<m-fields entity="@Model.EntityKey" for="Input.Extra" />`, so fields another module contributes
  with `ConfigureEntityUi("Catalog.Product", e => e.Fields.Add(...))` appear with no page edit. The page validates them first (422 with the error
  under the right field, posted values kept) and saves only the fields the user may see.
- Existing files are not rewritten: when the entity or create command on disk predates this, the generated UI omits `m-fields` and still
  compiles (`GenerateCrudCommand.SupportsExtraFields`). An API-only app's endpoints and DTOs do not expose the bag (a web app's do, through the registry: see the API entry above). A new entity's table has the extra
  column, so a migration scaffolded afterwards includes it.
- Verified end to end on a generated app: build 0/0, contributed fields rendered, an out-of-range value returned 422 and stored nothing, a valid
  post stored `{"Bin":"A-7","ReorderLevel":"25.5"}`.

### Added — `generate-crud --with-ui` generates an edit modal (Modulus.Cli)
- Each row gets an Edit button that loads a modal (`_EditForm`) pre-filled with the stored name and extension values; saving validates the
  contributed fields, merges them into the entity and swaps the refreshed table in while closing the modal. A failed save (422) re-renders the modal
  in place with the error under the right field.
- `Update{Entity}Command` gains an optional `ExtraProperties` (null changes none; an emptied field removes its key; fields the user cannot see are
  untouched). The modal reads the stored values through a new UI-only `Get{Entity}ForEditQuery`, so no API endpoint or DTO exposes the bag.
- Generated only for a theme host whose update command already carries `ExtraProperties` (the modal container comes from the theme); `--no-theme`
  and older CRUD sets keep the create-only page.
- Verified end to end on a generated app: modal pre-filled (`A-7`, `25.5`), out-of-range value returned 422 in the modal, a valid save returned
  `HX-Retarget`/`HX-Reswap` plus close-modal and toast, and the stored JSON went from `{"Bin":"A-7","ReorderLevel":"25.5"}` to `{"ReorderLevel":"30"}`
  after renaming and clearing `Bin`.

### Changed — `generate-crud --with-ui` list pages use the entity registry (Modulus.Cli)
- The generated `_Table` partial is now an `m-datatable entity="@Model.EntityKey"` with `<m-entity-columns />`, per-row
  `<m-entity-cells>` and `<m-entity-actions>`, so columns and row actions another module contributes with `ConfigureEntityUi("Catalog.Product", ...)`
  appear without editing the page. The page model exposes `EntityKey` (`{Module}.{Entity}`), injects `IEntityUiRegistry` and `ICurrentUser`,
  and loads the contributed column values once per page (also on every htmx table swap). `_Table` now takes the page model instead of the item
  list. Pages already generated are not rewritten (`generate-crud` never overwrites existing files).
- Verified end to end on a generated app (build, boot, htmx create): contributed header, cell and action rendered, delete form unchanged.

### Added — entity extension columns and row actions (Modulus.UI.Core)
- `ConfigureEntityUi` now also carries `e.Columns` and `e.Actions`. An `EntityAction` is a per-row link or htmx button (exactly one of
  `url` / `hxGet` / `hxPost`, `{id}` expanded URL-encoded, target the modal or the row, optional required permission); an `EntityColumn`
  names an `IEntityColumnValueProvider` type that loads the column's values for a whole page of rows in one call.
- `<m-datatable entity="...">` appends the visible contributed headers (at an optional `<m-entity-columns />` marker, default after the
  last column); `<m-entity-cells entity row-id values />` writes the matching cells (a dash when empty) and
  `<m-entity-actions entity row-id />` the row's buttons, all overridable views (`EntityCells`, `EntityActions`).
- `registry.LoadEntityColumnsAsync(services, entity, user, rowIds)` runs each visible column's provider once per page. Contributions
  needing a permission the user lacks are neither rendered nor loaded.
- `IEntityUiRegistry` gained `GetVisibleColumns` / `GetVisibleActions`; the field-only `EntityFieldCollection` became the generic
  `EntityContributions<T>` (`Fields`, `Columns`, `Actions`).

### Added — entity extension fields and `m-fields` (Modulus.UI.Core)
- `services.ConfigureEntityUi("Catalog.Product", e => e.Fields.Add(new EntityField(...)))` lets one module contribute form fields
  to another module's entity (label, type, tab, order, `ValidationAttribute`s, optional required permission, hint). Contributions
  accumulate in module registration order, a later module can `Fields.Remove` an earlier one's field, and `IEntityUiRegistry` is
  frozen on first use.
- `<m-fields entity="Catalog.Product" for="Input.Extra" tab="Inventory" />` renders the visible contributed fields through the
  overridable `Input` component (new overridable `Fields/Default` wrapper). Values post as a `Dictionary<string, string?>`
  (`Input.Extra[ReorderLevel]`); the posted value and per-field errors survive a 422 re-render.
- `registry.ValidateEntityFields(...)` / `registry.ReadEntityFields(...)` validate a post with the fields' own rules and return
  typed values. Fields needing a permission the user lacks are neither rendered, validated nor read.
- Supported field types: string, bool, int, long, decimal, double, DateOnly, DateTime, TimeOnly (and nullable), converted with the
  invariant culture.

### Added — `m-line-items` editable child rows (Modulus.UI.Core)
- `<m-line-items for="Input.Lines" row-partial="_LineRow" add-label="Add line" />` renders one row partial per item of a
  collection (invoice lines, a bill of materials) plus an add button and a remove button per row. The row partial is an
  ordinary partial whose model is the element type, so its fields are plain `<m-input for="Sku" />`s; each row's fields are
  named `Input.Lines[i].Sku`, so the collection model-binds with the default binder, and a 422 re-render puts every row's
  posted values and errors back on the right row.
- A blank row is rendered once into a `<template>`; the new `mLineItems` Alpine component (CSP-safe) clones it for "add"
  and renumbers the remaining rows after a "remove", so indexes stay contiguous. Overridable through
  `Views/Shared/Modulus/LineItems/Default.cshtml` (keep its `data-line-*` hooks).

### Added — `m-file` upload field and `m-form multipart` (Modulus.UI.Core)
- `<m-file name="file" />` (handler parameter) or `<m-file for="Input.Attachment" />` (an `IFormFile` member) renders a
  labelled file picker with `accept`, `multiple`, `hint`, required marker and validation errors. Overridable through
  `Views/Shared/Modulus/File/Default.cshtml`.
- `<m-form multipart="true">` posts as `multipart/form-data` (plus `hx-encoding` when it has a `target`).
- The Files UI upload form now uses both, and shows the missing/oversized-file error next to the field.

### Changed — `generate-crud --with-ui` installs the Tabler theme (CLI)
- `modulus generate-crud <Entity> --with-ui` now also references `Cobytelabs.Modulus.UI.Theme.Tabler` and wires
  `AddTablerTheme(builder.Configuration)` (plus its `using`). Pass `--no-theme` to keep Core's built-in layout or
  bring your own `ITheme`. Running it again on a host that was wired without a theme adds the theme.
- Generated pages resolve their layout through `GetThemeLayout()` (the page uses `HttpContext.`, `_ViewStart` uses
  `Context.`), and fall back to Core's layout when no theme is registered.
- The generated create form resets itself with the shared `mResetOnSuccess` Alpine component instead of an inline
  `hx-on::after-request` script, so generated pages are CSP-clean; it resets only after a successful request.

### Changed — `app --ui-modules` installs the Tabler theme too (CLI)
- `modulus app <name> --ui-modules …` now also references `Cobytelabs.Modulus.UI.Theme.Tabler` and wires
  `AddTablerTheme(builder.Configuration)`; `--no-theme` opts out. Choosing no UI modules installs no theme.

### Fixed — `app --ui-modules` wired nothing, and `ui add` / `app --ui-modules` produced a non-compiling `Program.cs` (CLI)
- The app command matched module ids (`identity`) against the catalog id (`Modulus.Identity`), so no chosen UI module
  was ever installed or wired. It now resolves them through the catalog.
- `UiHostWiring` only added the theme's namespace: `using Modulus.UI;` and each module's extension namespace
  (`Modulus.UI.Files`, …) were missing, so `AddModulusUi()` / `Add…Ui()` / `Map…Ui()` did not compile. Both are now added.

### Added — `ui add` / `app --ui-modules` register the backend services a feature UI needs (CLI)
- Files → `AddFileStorage`, Settings → `AddModulusSettings`, Notifications → `AddModulusNotifications`, AuditLogging →
  `AddModulusAuditLogging`, Tenancy → `AddMultiTenancy`, Permissions → `AddModulusAuthorization` (with their `using`s).
  Before, a freshly generated app served these pages as HTTP 500 until the host registered the service by hand. A
  registration the host already has (for example `AddS3FileStorage`) is left alone. Identity/Users still need an
  app-specific user store.

### Fixed — `LocalFileStorage` threw "The path is empty" when `Storage:BasePath` was unset
- `StorageOptions.BasePath` defaults to `""`, so the `?? "storage"` fallback never applied and the first file operation
  failed. An empty or whitespace base path now falls back to `./storage`.

### Fixed — CLI host wiring inserted `using` lines between statements
- `UiHostWiring` inserted `Environment.NewLine` (CRLF on Windows) into LF `Program.cs` files; a second wiring pass then
  detected CRLF and anchored its `using` insert wrongly, producing uncompilable code (CS1529). Inserts now use the
  file's own line ending.

### Added — `Modulus.UI` components
- Display: `m-stat`, `m-empty-state`, `m-detail-list`/`m-detail`, `m-timeline`/`m-timeline-item`, `m-confirm`.
  Forms: `m-input` and `m-select` (label, required marker, errors and the posted value come from the model).
- `m-datatable source="…" refresh-on="product:changed"` re-queries its rows when an entity `*:changed` event arrives.

## [1.4.0] - 2026-09-15

### Fixed — Scriban vulnerable to High/Critical advisories (CLI)

`dotnet list package --vulnerable` flagged `Scriban 6.5.0` (High,
GHSA-wgh7-7m3c-fx25 et al.), and the `6.6.0` patch still carried a Critical
sandbox escape (GHSA-5wr9-m6jw-xx44, fixed in `7.0.0`). Upgraded to
`Scriban 7.4.0`; the only breaking change touching the CLI was
`ITemplateLoader.LoadAsync` returning `ValueTask<string?>`
(`TemplateEngine.TemplateLoaderImpl` updated). Audit is now clean across every
project and all 136 CLI template-rendering tests pass unchanged.

### Added — per-tenant database migration fan-out

`MigrateModulusDatabasesAsync` only migrated the ambient/host database, so apps
with per-tenant connection resolvers had no framework path to bring tenant
databases to the current schema. `Modulus.MultiTenancy.EntityFrameworkCore` now
exposes `TenantDatabaseMigrationExtensions.MigrateModulusDatabasesForTenantsAsync(mode, ct)`:
migrates the host database first, then enumerates `ITenantStore.ListAsync()`
(new method, default returns empty; `EfTenantStore` lists active tenants in
slug order) and re-runs the migration inside each tenant's `Change(tenant)`
scope. Intended for a dedicated migrator job / init container, not every replica.

### Added — MongoDB outbox management API + session transactions

Two gaps in the Mongo outbox path: no operator API, and the writer's documented
non-transactional limitation. `Modulus.Outbox.MongoDB` now ships
`MapModulusMongoOutboxManagement` (list/inspect/replay/purge over the outbox
collection with server-side filter/sort/skip/limit, same models and
`messaging:manage` permission as the EF endpoints) and
`IMongoOutboxSessionProvider`: when a unit of work flows its
`IClientSessionHandle`, the writer's insert joins that session's transaction
(atomic with domain writes on a replica set), otherwise it falls back to a
plain insert. Consumer-side inbox dedup still applies either way.

### Added — config-bound OpenTelemetry bootstrap

Hosts hand-rolled OTLP wiring (the TradeFlow sample carried its own
`AddAppTelemetry`). `Modulus.Observability` now ships
`ModulusOpenTelemetrySetup.AddModulusOpenTelemetry(configuration, environment)`,
binding the `OpenTelemetry` section (`Enabled`/`ServiceName`/
`EnableConsoleExporter`/`Otlp:Endpoint|ExportTraces|ExportMetrics`) and wiring
ASP.NET Core + HttpClient + Runtime instrumentation plus the Modulus
sources/meters. Exporters are only added when explicitly enabled (OTLP needs an
endpoint), so a default app boots with no export overhead. New packages:
`OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Exporter.Console`,
`Extensions.Hosting`, `Instrumentation.Http`, `Instrumentation.Runtime`.

### Fixed — outbox management list loaded every dead-letter row into memory

The EF `GET /outbox/dead-letters` endpoint pulled **all** dead-letter rows from
**every** module context into memory before filtering/paginating in process —
under a failure storm this is unbounded. Filters are now pushed to the DB and
each context contributes at most `page*pageSize` rows. Replay is also narrowed
to dead-lettered rows only (`ProcessedAt == null && RetryCount >= MaxRetries`)
via batched `ExecuteUpdateAsync`, and the cleared error is archived to the log
with the acting user instead of being nulled away silently.

### Fixed — module shutdown aborted on the first module that threw

`ModuleLoader.ShutdownAllAsync` had no per-module try/catch: one module's
`ShutdownAsync` throwing aborted the reverse-order loop, so every module still
queued (earlier in registration order, later in shutdown order) never got its
own `ShutdownAsync` called — leaking connections and dropping in-flight work
on a shutdown that was already underway. Each module's shutdown is now
individually wrapped; a failure is logged and shutdown continues with the
remaining modules. **Breaking**: `IModuleLoader.ShutdownAllAsync` now takes
`IServiceProvider` (to resolve a logger), matching `InitializeAllAsync`'s
existing signature.

### Fixed — no environment guard on `UseDevelopmentCertificates`

Nothing checked at runtime whether `Identity:UseDevelopmentCertificates` was
enabled in Production — only a doc comment said "Development only". Since
every environment-specific `appsettings.*.json` is gitignored repo-wide (only
the base `appsettings.json` is tracked, anywhere in the repo — including
samples), a deployment with no local `appsettings.Production.json` override
falls back entirely to the base file's settings, which enable both
`UseDevelopmentCertificates` and `AllowPasswordFlow` for local dev. Nothing
previously stopped that combination from silently reaching a Production
deployment and signing every token with OpenIddict's ephemeral,
regenerated-per-restart development certificate. `AddModulusOpenIddict` now
registers a `DevelopmentCertificateGuard` hosted service that fails fast at
startup if the flag is set while `IHostEnvironment.IsProduction()` is true —
this is the actual enforcement point regardless of which config file (or
none) a given deployment supplies.

### Fixed — inbox dedup was silently broken in both registration orderings the framework ships (breaking, migration required)

`AddInbox<TContext>`/`AddMongoInbox` decorated `IIntegrationEventHandler<T>`
registrations by mutating `IServiceDescriptor`s at the moment they ran — which
only worked if every handler was already registered first, and only if
`AddInbox` ran once. Neither held:

- **CLI-generated apps** call `AddModulus(...)` (which runs every module's
  `AddInbox`) *before* `AddModulusEvents(...)` registers any handlers — the
  decorator ran against zero handlers, so the inbox silently provided **no
  deduplication at all**.
- **Multi-module apps calling `AddModulusEvents` first** (e.g. TradeFlow, 15+
  modules each calling `AddInbox`) re-wrapped the already-wrapped descriptor on
  every subsequent call, nesting up to N decorators per handler. The outer
  claim always deferred on the inner claim for the same EventId, so every
  integration event dead-lettered without the real handler ever running —
  logged and counted as a dedup hit (`modulus.inbox.dedup_hits`),
  indistinguishable from healthy deduplication.
- Independently, the claim key was the bare EventId with no handler
  discriminator: an event with **more than one** handler had the first to
  claim mark it `Processed`, silently skipping every other handler forever.

**Fixed** by moving the wrap from DI-registration time to dispatch time.
`AddInbox`/`AddMongoInbox` now register a stateless
`IIntegrationEventHandlerDecorator` (new seam in `Modulus.Events.Abstractions`,
`TryAddSingleton` — idempotent across repeated `AddInbox` calls);
`IntegrationEventDispatcher` and `InProcessModuleBus` both wrap each handler
they resolve, at the moment they dispatch — after every handler *and* every
inbox registration has run, regardless of which came first in `Program.cs`.

- **Breaking schema change**: `InboxMessage`/`MongoInboxMessage` gain a
  `HandlerName` column (the wrapped handler's `Type.FullName`); the EF Core
  primary key becomes `(Id, HandlerName)` and the Mongo claim key becomes a
  unique compound index on `(EventId, HandlerName)` instead of relying on
  `_id` alone. **Apps must run `modulus migrate add` per module using
  `AddInbox`** before deploying this version.
- **Migration safety**: rows written before this column existed are honoured
  for *any* handler claiming that EventId (a legacy `Processed`/dead-lettered
  row is skipped for every handler; a legacy row still eligible to claim is
  "adopted" by the first handler that claims it) — an in-flight upgrade
  neither reprocesses an already-handled event nor drops one still mid-flight.
- `IInboxStore.TryClaimAsync`/`MarkProcessedAsync`/`MarkFailedAsync` all gained
  a `handlerName` parameter — a breaking change to the (rarely
  directly-implemented) `IInboxStore` interface.

### Fixed — Quartz delayed/recurring jobs ran with no ambient tenant

`QuartzJobScheduler.EnqueueAsync` populated `tenantId`/`correlationId` in the
job's `JobDataMap`, but `ScheduleAsync` (delayed) and `AddRecurringAsync`
(cron) built theirs with only `["args"]` — `QuartzJobAdapter` silently opens
no tenant scope when those keys are absent, so on the framework's only
production-durable scheduler, every delayed or recurring job ran outside
tenant isolation while immediate jobs worked correctly, with nothing logged.
All three scheduling paths now build their `JobDataMap` through one shared
helper. New `Modulus.BackgroundJobs.Quartz.Tests` project (none existed) adds
regression coverage for all three paths.

### Fixed — `MemoryCacheService` had no tenant scoping, unlike `RedisCacheService`

`RedisCacheService.TagKey` prefixes tag keys with the ambient tenant id so two
tenants sharing a tag name (e.g. `"catalog"`) can't invalidate each other's
entries; `MemoryCacheService`'s tag index was a flat dictionary keyed on the
raw tag with no tenant awareness at all. In a multi-tenant app running the
in-memory cache — the default — two tenants using the same tag name could
invalidate each other's cache entries. `MemoryCacheService` now applies the
identical `TagKey` scoping as `RedisCacheService`, so the two
`ICacheService` implementations behave the same regardless of which one an
app has wired up.

### Fixed — background-job/lock failures were compiled out of Release builds

Three catch blocks reported failures via `System.Diagnostics.Debug.WriteLine`,
which is `[Conditional("DEBUG")]` and therefore removed entirely from Release
builds: `QuartzJobScheduler`'s recurring-job schedule/remove failures, and
`RedisDistributedLock`'s failed lock-release. All three ran on fire-and-forget
paths with nothing else to surface the error, so a production failure to
schedule a recurring job or release a distributed lock was silently invisible
— no log, no metric, no exception. Both types now take an injected `ILogger`
and log at `LogError`/`LogWarning`, matching `ChannelJobQueue`'s existing
pattern.

### Changed — explicit module registration (breaking)

The `[DependsOn]` module-dependency mechanism has been removed. Modules are now
registered explicitly in `Program.cs`; registration order is authoritative for
every lifecycle phase (config phases, `InitializeAsync`; `ShutdownAsync` runs in
reverse).

- **Removed**: `[DependsOn]` attribute, `IModule.DependsOn`, `ModuleGraph`
  (topological sort / cycle detection), `ModuleDependencyNotFoundException` /
  `ModuleDependencyResolutionException` / `ModuleCycleException`, and the
  `AddModulus<TStartupModule>(configuration)` startup-module overload.
- **Added**: `AddModulus(IConfiguration, Action<ModulusBuilder>)` +
  `ModulusBuilder.AddModule<T>()` / `AddModule(Type)` — Program.cs is the
  composition root; duplicate registrations are ignored (idempotent). Generated
  apps no longer ship an `{App}HostModule`.
- **CLI**: `modulus add-module` now wires `modules.AddModule<{Module}Module>()`
  into Program.cs instead of `[DependsOn]` on a host module (detects the
  `AddModulus(` anchor; refuses with migration guidance on legacy
  `AddModulus<` apps).
- **Observability**: `GET /health/graph` now returns an ordered module
  inventory (`name`, `type`, `initOrder`) instead of a mermaid dependency graph.
- Migrating: replace `builder.Services.AddModulus<HostModule>(config)` with an
  explicit `AddModulus(config, modules => modules.AddModule<A>()...)` listing
  every module (the old host module's `[DependsOn]` list is the source of
  truth for the order), then delete the host module class.

### Fixed — CLI template & testing-harness bugs (found by regenerating an app e2e)

- **`NuGet.config` template generated invalid XML** — the guidance comment
  contained `--package-source`, and `--` is illegal inside XML comments, so
  every freshly generated app failed restore with "NuGet.Config is not valid
  XML". Comment reworded to avoid the double dash.
- **`Program.cs` template missed namespace imports** — the host usings never
  included `Modulus.AspNetCore.{Correlation,Cors,FeatureFlags,HealthChecks,
  Idempotency,OpenApi,RateLimiting,Security,Versioning}`, so every generated
  app failed to compile its own middleware wiring (`AddModulusCorrelation`,
  `AddModulusRateLimiting`, …). All nine imports added.
- **`ModuleBoundaryRules` was inoperative** — it scanned only `Modulus.*`
  framework assemblies (never the app's own modules/events) and resolved
  `typeof(IModule)` against a local placeholder interface nothing implements,
  so `FindModuleTypes()` always returned empty and app-owned integration
  events were never name-checked. Now scans all non-dynamic assemblies
  (ReflectionTypeLoadException-safe), skips abstract bases (no more
  `IntegrationEventBase` false positive), and uses the real
  `Modulus.Core.Abstractions.IModule`.

### Fixed — Production-hardening pass

Security & correctness fixes across transports, identity, tenancy, and the
request pipeline:

- **Repo hygiene** — tracked `build/*.props` (imported unconditionally by
  `Directory.Build.props` but previously gitignored); removed
  `.claude/settings.local.json` from the index.
- **Kafka consumer** — failed deliveries are re-seeked with capped exponential
  backoff (`MaxDeliveryAttempts`, default 5); exhausted messages are committed
  past and logged instead of hot-looping the partition.
- **Broker dispatch context** — RabbitMQ and Kafka consumers restore the
  ambient tenant/correlation scope around handler invocation via the shared
  `EnvelopeAmbientScope`, matching HTTP-request semantics downstream.
- **`TransactionBehavior`** — dedupes resolved contexts by runtime type and
  begins transactions inside the try block, so a failure while starting a
  transaction rolls prior contexts back cleanly.
- **Identity token endpoint** — the password-grant subject-activity check now
  inspects the principal produced by sign-in (previously the ambient
  controller user, anonymous during token issuance); default granted scopes
  include `openid` and `offline_access`.
- **External IdP token validators** — OIDC discovery validators are cached per
  metadata-address/audience set; all five adapters (Auth0, Okta, AzureAd,
  Duende, Authentik) gained optional `Audience` validation; fixed the AzureAd
  v2.0 authority's token-endpoint path; Duende userinfo calls authenticate with
  client credentials; subject lookups are URL-escaped.
- **CLI templates** — `Modulus.Data.Abstractions` package references follow the
  framework version instead of a hardcoded `1.0.0`; generated apps can embed a
  local package feed via `modulus app --package-source`; `--database` /
  `--migration-engine` values are validated up front with clear errors.
- **Query cache isolation** — `CachingBehavior` includes the ambient tenant id
  in cache keys, closing a cross-tenant response leak between tenants whose
  requests serialise identically.
- **Rate limiting** — a custom evictable fixed-window limiter plus background
  sweeper removes idle partitions, bounding memory under per-user/per-IP churn;
  options are bound once so `IOptions` and middleware behaviour cannot diverge.
- **HTTP idempotency** — replay keys are scoped by tenant *and* authenticated
  caller; bodies larger than `IdempotencyOptions.MaxResponseBytes` (default
  1 MB) are executed but not cached; `Date` and `Set-Cookie` headers are never
  replayed.
- **Subdomain tenant resolver** — host matching requires the dot boundary
  (`.baseDomain`), blocking spoofed hosts such as `attacker-modulus.app`;
  requests to the bare domain resolve to no tenant instead of throwing; slugs
  are length- and charset-validated before hitting the store.
- **Outbox writer routing** — direct `IOutboxWriter.WriteAsync` callers resolve
  the owning module context through `IEntityContextMap` instead of whichever
  `DbContext` happened to be registered last.
- **SQLite in-memory provider** — each context gets its own uniquely named
  shared-cache database (sharing one name made second+ modules' schema setup
  silent no-ops), and a real opened keep-alive connection replaces the
  lazily-created keyed singleton nothing ever resolved.
- **Role claims** — server-side permission resolution accepts both
  `ClaimTypes.Role` and short-form `role`, so tokens validated with inbound
  claim mapping disabled authorise correctly.

### Added — Production-hardening pass

- **Outbox purge** — EF Core and MongoDB processors delete dispatched and
  dead-lettered rows older than `OutboxOptions.PurgeAfterDays` (default 7,
  0 disables) in bounded batches, running before the empty-poll short-circuit
  so steady-state tables stay bounded.
- **dbsh migration engine** — SQL-first alternative to EF Core migrations.
  Each module can independently use EF Core or dbsh; the CLI auto-detects
  the engine per module and dispatches `dotnet ef` or `dbsh` accordingly.
  `--migration-engine dbsh` on `modulus app` / `modulus add-module` generates
  `Database/Config/migration.json` (provider + `${VAR}` connection) and
  `Database/Migrations/{Module}/` for hand-written `.sql` files.
  `ExternallyManaged<TContext>()` marks the context so startup skips it.
  `modulus doctor` validates `dbsh` availability. `modulus migrate add`
  scaffolds a SQL stub; `modulus migrate update` runs `dbsh init && dbsh migrate`.
- **Release workflow gates** — tag-triggered releases run unit *and*
  integration tests before packing/publishing.
- Package `<Description>` metadata filled in for every library; README and
  AGENTS.md project counts/layout brought in line with the actual tree
  (31 src projects).

### Changed — Package consolidation (55 → 23)

The framework was consolidated from 55 packages down to 23 focused packages
(the solution has since grown back to 31 as new opt-in providers landed).
Namespaces are preserved — types keep their original namespaces (e.g.
`Modulus.Core.Abstractions.IModule`) even when compiled into a different
assembly. Only `<ProjectReference>` / `<PackageReference>` names changed.

- **Merged abstractions into implementations:**
  `Core.Abstractions` → `Core`, `EFCore.Abstractions` → `EFCore`,
  `Mediator.Abstractions` → `Mediator`, `Events.Abstractions` → `Events`,
  `Inbox.Abstractions` → `Inbox`, `SignalR.Abstractions` → `Platform`,
  `Identity.Abstractions` → `Identity`.
- **`Outbox.Abstractions` kept separate** — it is the seam that prevents a
  circular dependency (`EFCore` → `Outbox.Abstractions`, `Outbox` → `EFCore`).
- **Merged platform services:** `MultiTenancy`, `Authorization`,
  `BackgroundJobs`, `Caching`, `Storage`, `SignalR` → `Modulus.Platform`.
- **Merged identity adapters:** 6 external IdP validators + EF Core mapping →
  `Modulus.Identity`.
- **Merged observability:** `Diagnostics` + `OpenTelemetry` →
  `Modulus.Observability`.

### Removed — Dropped stubs

The following stub/unmaintained packages were removed (can be re-added as
needed): Cassandra, CosmosDB, DynamoDB, Elasticsearch, Redis, Dapper,
`EventBus.ServiceBus`, `EventBus.Sqs`, `SignalR.Azure`, `SignalR.Redis`,
`BackgroundJobs.Hangfire`, `BackgroundJobs.Quartz`, and `Modulus.Benchmarks`.

### Added

- **CLI scaffolding tool** (`Modulus.Cli`) — Spectre.Console.Cli + Scriban
  `dotnet tool` that generates complete solutions, modules, and CRUD code
  (`modulus app`, `modulus module`, `modulus add-module`, `modulus generate-crud`),
  replacing the previous `dotnet new` templates and `Modulus.App` sample.

### Fixed — Architectural defects

- **Transactional outbox (dual-write):** `ModuleDbContext.SaveChangesAsync`
  now enqueues domain events that implement `IIntegrationEvent` to
  `IIntegrationEventOutbox` (backed by `EfOutboxWriter`) *before* calling
  `base.SaveChangesAsync`, so the outbox row(s) participate in the same DB
  transaction. The outbox was previously registered but completely unwired.
  `EfOutboxWriter` now implements both `IOutboxWriter` and
  `IIntegrationEventOutbox`, resolving `DbContext` lazily via
  `IServiceProvider` to break the circular DI dependency.
- **Outbox row-locking & retries:** `OutboxProcessor` claims rows atomically
  via an `ExecuteUpdateAsync` whose `WHERE` re-checks `LockedUntil`
  (provider-agnostic `FOR UPDATE SKIP LOCKED` equivalent), so multiple app
  instances no longer duplicate-dispatch every event. Failed dispatches
  schedule exponential backoff (`NextAttemptAt`) and dead-letter after
  `MaxRetries` instead of being silently dropped. `OutboxProcessor` is now
  registered in DI.
- **Inbox dedup (EF Core & MongoDB):** `AddInbox<TContext>` and
  `AddMongoInbox` now decorate all `IIntegrationEventHandler<T>`
  registrations with an idempotent decorator backed by a common
  `IInboxStore` (`EfInboxStore` / `MongoInboxStore`). Previously the inbox
  stores were registered but the handler pipeline was never decorated —
  providing zero dedup. The EF Core decorator also resolves the inner handler
  via `ActivatorUtilities.CreateInstance` instead of
  `GetRequiredService(ImplementationType)`.
- **`IdempotentIntegrationEventHandler`:** claims the row atomically via the
  EventId PK (the loser defers via `DbUpdateException` →
  `InboxDeferralException`). No longer double-executes on redelivery and
  dead-letters after `InboxOptions.MaxRetries` instead of hot-looping.
- **`TransactionBehavior`:** now starts an explicit `BeginTransactionAsync`
  on *every* resolved `DbContext`. `AddModuleDatabase<TContext>` now also
  registers the context as `DbContext` so the behavior can discover it (the
  previous `GetServices<DbContext>()` returned zero items). Each context runs
  in its own independent DB transaction; for cross-module consistency prefer
  the transactional outbox.
- **Multi-tenancy query filter:** `ModuleDbContext` now captures the
  `ICurrentTenant` service field (not a value), registers the filter
  unconditionally, and degrades to match-all when no tenant is in scope (no
  more `Guid.Empty` leak). Soft-delete + tenant predicates are combined to
  honour EF's one-filter-per-entity rule.
- **`ICurrentTenant` async flow:** backed by a static
  `AsyncLocal<TenantInfo?>` with a `Change(...)` scope API, so tenant context
  flows into background jobs / message consumers / hosted services.
- **Identity password grant (auth bypass):** the token endpoint previously
  minted tokens for *any* username with zero credential check. It now
  delegates to an `IPasswordGrantCredentialValidator`;
  `AddModulusOpenIddict` registers a `NullPasswordGrantCredentialValidator`
  (deny-by-default) until `AddModulusIdentity` replaces it with
  `IdentityPasswordGrantValidator<TUser>` (SignInManager +
  `CheckPasswordSignInAsync`, honours `IsActive` and lock-out). Granted scopes
  are intersected with a registered allow-list. The refresh-token branch now
  returns a proper `invalid_grant` error instead of a bare `Forbid()`.
- **External IdP token validation:** the Auth0, Okta, Azure AD, Duende, and
  Authentik adapters previously validated bearer tokens by GETting the
  userinfo endpoint and treating `200` as valid. They now use a shared
  `OidcDiscoveryValidator` that fetches the provider's JWKS via OIDC
  discovery and locally checks the signature, issuer, and lifetime
  (1-min clock skew). Audience validation is opt-in. Keycloak is unchanged
  (already used RFC 7662 introspection).
- **NoSQL tenant fallback:** `MongoTenantFilter` and `ElasticRepository` no
  longer filter on `Guid.Empty` in host context; they return match-all.
- **Other defects:** `LocalFileStorage` path traversal;
  `GlobalExceptionHandler` caught the wrong `ValidationException` type;
  `OutboxPollingService` aborted on any non-OCE exception;
  `NullCurrentUser`/`NullPermissionRegistry` were fail-open;
  `PagedList.TotalPages` divide-by-zero; `ModuleNotFoundException` literal
  message; SignalR `EnableDetailedErrors` shipped to all clients.

## [1.0.0] - 2025-01-01

### Added

- Initial framework release.
- Modular monolith architecture with topological module loading.
- DDD building blocks (`AggregateRoot<TId>`, `IDomainEvent`, `IModule`).
- CQRS mediator with pipeline behaviors (logging, validation, authorization,
  transaction).
- Event bus with InMemory, RabbitMQ, and Kafka providers.
- Transactional Outbox and Inbox patterns.
- Multi-tenancy with header, JWT claim, and subdomain resolution.
- OpenIddict identity with 6 external IdP adapters.
- Data providers: SQL Server, PostgreSQL, MySQL, SQLite, MongoDB.
- OpenTelemetry observability.
