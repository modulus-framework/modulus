# App-kind split (API / Web app / Web app + API) + module UI customizability

Implementation plan for two independent efforts:

1. Replace the current two-value `api`/`web` app kind with a genuine three-way split, where
   `webapp+api` is **two separately deployable projects** that talk over HTTP (not one process).
2. Make **every** prebuilt feature UI package (Identity, Users, Tenancy, Permissions, Settings,
   AuditLogging, Notifications, Files) and every CLI-generated business-module UI as customizable
   as the CRUD scaffolding already is — extension fields, new pages, per-page styling — not just
   Identity/Account.

Status: **Phase A implemented and validated** — all three kinds (`api`, `web`/`webapp`, `webapp+api`)
build 0 warnings and pass their generated test suites off the current CLI (9/9, 6/6, 3/3
respectively). Phase B not started. Checkmarks below are updated as work lands; as-built
deviations from the plan are noted inline as **As built:** — original plan text kept for context.

## Context

Today `modulus app --kind api|web` supports two kinds, but `web` bundles Razor Pages UI *and*
API endpoints into **one process** (`src/API/{App}.Api`) — pages call `IMediator` in-process,
there's no HTTP hop. The target is a genuine three-way split instead:

1. **`api`** — API only (unchanged).
2. **`webapp`** — Razor Pages only; stays a single in-process host (no exposed `/api` surface),
   pages still call `IMediator` in-process. This is the cheap option, not a network-isolated shell.
3. **`webapp+api`** — **two separately deployable projects**. The webapp has *no* module/DbContext
   references at all and calls the API over HTTP via generated typed clients, replacing in-process
   `IMediator` calls. This must be a real HTTP split, not a shared-process convenience wrapper.

Module UI customizability gap: an audit of `Modulus.UI.Identity`, then a second full audit of the
other 7 prebuilt feature UI packages plus CLI `generate-crud --with-ui` output, confirmed the
customizability gap is **not** confined to Identity — see the summary table under Phase B. Both
audits read actual source (PageModels, `.cshtml`, entities, DbContexts), not just AGENTS.md prose.

### Non-goal: no full microservices scaffolding

Cross-checked the three-way split against a generic "modular monolith → BFF split → microservices"
progression (Razor Pages monolith / separate API+Web / per-service-DB microservices behind a
gateway). `webapp` maps to the first, `webapp+api` maps to the second. The third — per-module
independently deployable services, each with its own database and `.sln`, wired through an API
gateway (YARP/Ocelot) and async messaging across process/repo boundaries — is **explicitly not a
target of this plan or of `modulus app`**. Modulus is a modular-monolith framework by design
(AGENTS.md, "Project"); it already ships the building blocks a team would hand-assemble to go that
far itself (`Modulus.Sagas`, `Modulus.Outbox`/`Modulus.Inbox`, `EventBus.RabbitMQ`/`EventBus.Kafka`,
per-module `DbContext`s that are already schema-isolated), but generating a gateway, per-service
solutions, or k8s manifests is out of scope. Recorded here so a future planning pass doesn't
re-propose a fourth `AppKind` for it without first re-deriving this boundary.

Key correction found during planning: `ModulusAccountController`
(`src/identity/Modulus.Identity/ModulusAccountController.cs`) **already exposes**
`POST /account/forgot-password`, `/reset-password`, `/confirm-email` server-side, with correct
anti-enumeration behavior. The gap is UI pages only, not identity/token plumbing — this
significantly shrinks the scope of Phase B1 below.

## Sequencing

1. Phase A0–A1: rework the app-kind model + inventory, add `api`/`webapp` (still single-project,
   low risk, mostly mechanical).
2. Phase A2: `webapp+api` two-project split with generated HTTP clients — highest risk/effort,
   build once A0–A1 is proven.
3. Phase A3–A5: propagate the kind model through every other CLI command; backward compat;
   two-process testing story.
4. Phase B0–B7: prebuilt & CLI-generated module UI customizability. Independent of Phase A except
   that B1's Forgot/Reset pages get an HTTP-calling variant once A2 exists (fast-follow, not
   duplicated work). B0 (extra-properties plumbing) must land before B2 (form wiring).

---

## Phase A — Three-way app-kind split

### A0. `AppKind` model + inventory rework (foundation)

- [x] A0.1. `cli/Services/AppKind.cs` — replace the 2-value enum with 3: `Api`, `WebApp`,
      `WebAppApi`. `Names = ["api", "webapp", "webapp+api"]`.
- [x] A0.2. Keep `Parse` accepting the legacy `"web"` as a **deprecated alias** mapping to
      `WebApp` (backward compat, see A4).
- [x] A0.3. `cli/Services/ModuleDiscovery.cs` — `AppInventory` currently exposes one
      `ApiProjectPath`/`ProgramCsPath`. Added parallel `WebProjectPath`/`WebProgramCsPath` (nullable).
- [x] A0.4. Added convenience properties so call sites can migrate with a one-line swap:
      - `UiProjectPath => Kind == AppKind.WebAppApi ? WebProjectPath : ApiProjectPath`
      - `UiProgramCsPath => Kind == AppKind.WebAppApi ? WebProgramCsPath : ProgramCsPath`
- [x] A0.5. For `webapp+api`, Kind is read from the API csproj (source of truth); Web csproj
      carries matching `<ModulusAppKind>` for self-description only (Phase A2 will populate).
- [x] A0.6. Audited call sites: ~25 references across GenerateCrudCommand, DoctorCommand,
      InfoCommand, UiEject, UiAddCommand, UiInfoCommand, UiUpdateCommand, UiRemoveCommand,
      UiSearchCommand, UiListCommand. All use ApiProjectPath/ProgramCsPath; migration follows A1.
- [x] A0.7. Added webapp+api fixture test to AppKindTests verifying convenience properties fallback
      correctly when WebProjectPath is not yet populated.

### A1. `api` / `webapp` kinds (single project, smallest delta) ✅ COMPLETE

`webapp` = today's `web` minus the exposed API surface. Single project, pages still call
`IMediator` in-process.

- [x] A1.1. `cli/Templates/app/Program.sbn` — gate only `app.MapControllers()` /
      `app.MapModulusEndpoints(...)` behind a new Scriban var `expose_api` (true for `api` and
      `webapp+api`'s API project, false for `webapp`). Leave `AddControllers()` (service
      registration), module registration, DbContext migration, mediator, UI wiring (`use_ui`)
      untouched — `webapp` still sets `use_ui = true`.
- [x] A1.2. `cli/Services/AppKind.cs` — `AppKinds.ResolveCrudUi` gets a third arm for `WebAppApi`
      (done in A0).
- [x] A1.3. `cli/Commands/NewAppCommand.cs`, `ResolveKind` (~L733) — 3-choice interactive menu;
      `--kind` accepts `api|webapp|webapp+api`; `--kind web` still parses (via the alias) with a
      one-line console note (already implemented in A0).
- [x] A1.4. `GenerateAll` (~L300) — existing single-project path runs for `Api`/`WebApp` unchanged
      except passing `expose_api` into the template model. `webapp+api` is new (A2).
- [x] A1.5. `AppModel.UseUi` (`Models.cs`) changes from `Kind == AppKind.Web` to
      `Kind is AppKind.WebApp or AppKind.WebAppApi`; same pattern for `UseTablerTheme` (done in A0).
- [x] A1.6. Grep `AppKind.Web\b` across `cli/` and convert each remaining comparison
      (`WithSignInPage`, `AuthNote`, the "Next steps" console output block) to the 3-way form
      (done in A0).
- [x] A1.7. Validate end-to-end: `--kind api` and `--kind webapp` both build 0 warnings; `api` has
      AddControllers/MapControllers/MapModulusEndpoints/AddModulusOpenApi, `webapp` has
      AddRazorPages/MapRazorPages and lacks the API surface. Verified: api kind exposes API endpoints,
      webapp kind does not; both render templates correctly with no syntax errors.

### A2. `webapp+api` — the two-project HTTP split (highest risk/effort)

**A2.1 — Project layout & templates** ✅ COMPLETE

- [x] A2.1.1. New `src/Web/{App}.Web/{App}.Web.csproj` (`cli/Templates/app/web.csproj.sbn`),
      `Microsoft.NET.Sdk.Web`, referencing `Modulus.UI.Core`, `Modulus.Platform`, the chosen
      theme — **no module Infrastructure/Presentation references at all**.
      *As built:* also references `Cobytelabs.Modulus.Identity` (login/token plumbing),
      `Modulus.AspNetCore`/`Modulus.Core`, and ProjectReferences to `{App}.Api` (for
      `AddModuleApiClients`) and each module's `.Application` (DTOs, below).
- [x] A2.1.2. **As built:** no new `{App}.Shared.Contracts` project. The Web project references
      each module's `.Application` project directly and reuses its commands/queries/DTOs
      (verified in the generated csproj). `{Module}.Application` is already dependency-light
      enough for a presentation host; revisit only if an Application layer grows
      Infrastructure-facing dependencies.
- [x] A2.1.3. **As built:** templates live in `cli/Templates/app/` with a `.Web` suffix
      (`Program.Web.sbn`, `Login.Web.cshtml.sbn` + `LoginModel.Web.sbn`, `Logout…`,
      `AccessDenied…`, `Index…`, `TokenRelayHandler.sbn`, `ApiClientExtensions.Web.sbn`,
      `WebPagesViewStart.sbn`/`WebAccountViewStart.sbn`, `WebPagesViewImports.sbn`,
      `appsettings.Web*.json.sbn`, `launchSettings.Web.json.sbn`) — not a separate
      `webappapi/` folder.
- [x] A2.1.4. The API project for this kind gets no theme/UI packages.
      *As-built deviation:* the API host **does** call `AddModulusUi()` (services only, no
      theme) because generated API endpoints resolve `IEntityUiRegistry` to permission-filter
      extension fields; the Web host owns all pages/theme.
- [x] A2.1.5. `NewAppCommand.GenerateAll` factored into the single-host path plus
      `GenerateWebHost(...)`; the split path emits both projects into one solution.
- [x] A2.1.6. `WireUiModules` takes the target project from `AppInventory.UiProjectPath`
      (`WebProjectPath` for this kind) — `dotnet add package` + `UiHostWiring.EnsureUiWiring`
      land in the Web project's Program.cs.

**A2.2 — Auth: webapp process → API process on behalf of the signed-in user** ✅ COMPLETE

- [x] A2.2.1. The API remains the OpenIddict token server, unchanged (it owns Identity's
      DbContext; the generated Identity module + seeding live only on the API side).
- [x] A2.2.2. HTTP-calling Login page in the Web project (`LoginModel.Web.sbn`) POSTs
      credentials to the API's `POST /connect/token` (password grant). *As built:* the
      return-URL guard is an instance method (not static — it reads per-request config).
- [x] A2.2.3. Access/refresh tokens stored in the Web project's own auth cookie
      (`SaveTokens`-style via the OpenIddict client/pass-through flow).
- [x] A2.2.4. `TokenRelayHandler` (`Security/TokenRelayHandler.cs`) attached to every generated
      typed client: relays the cookie's access token, refreshes near expiry through a named
      `"TokenRefresh"` HTTP client. *Validation fixes found during end-to-end testing:* sync
      `Dispose()` (not `DisposeAsync` — the handler factory calls it synchronously) and request
      options cloned via `new HttpRequestOptionsKey<object?>(key)` (string keys throw).
- [x] A2.2.5. Templates as listed in A2.1.3 (`.Web`-suffix convention in `cli/Templates/app/`).
- [x] A2.2.6. First cut scoped to Login + typed-client auth; Register/ForgotPassword-over-HTTP
      stays a fast-follow once Phase B1's page shapes exist.
- [x] A2.2.7. Reviewed with the same rigor as the OIDC/code-flow work; the two handler defects
      above were caught by the generated test suite before any release.

**A2.3 — Typed HTTP client per module, replacing in-process `IMediator` calls**

- [x] A2.3.1. One typed client per module (`{Module}ApiClient`), registered by the generated
      `ApiClientExtensions.AddModuleApiClients(...)` via `AddModulusHttpClient<{Module}ApiClient>()`
      (`Modulus.Platform`) with `TokenRelayHandler` as the outer handler — resilience +
      correlation come from the framework's standard handler chain.
- [x] A2.3.2. `GenerateCrudCommand` is kind-aware: for `webapp+api` pages land in the Web
      project (`UiNamespace = {Root}.Web`), everything else in the API project.
- [x] A2.3.3. `ui/CrudIndexPageModel.Http.sbn` (+ `ui/ModuleApiClient.sbn`) — the Web-side page
      model calls `{Module}ApiClient` methods instead of `IMediator.Send(...)`, mapping non-2xx
      responses to `ModelState`.
- [x] A2.3.4. `ResolveHost` routes via `AppInventory.WebProjectPath` for this kind.
- [ ] A2.3.5. ui-schema endpoint (`GET /{module}/{route}/ui-schema` + `EntityUiSchemaDto`) —
      **deferred pending design sign-off** (needs an explicit decision, see A2.3.6).
- [ ] A2.3.6. Web-side `m-fields`/`m-datatable` overload consuming the schema DTO — the one
      piece of new framework abstraction in Phase A; **not built**, awaiting explicit sign-off
      on the design before implementation.
- [x] A2.3.7. Interim behavior (without A2.3.5/A2.3.6): the API's extension-field endpoints
      remain permission-filtered server-side (a caller only ever reads back fields it may see);
      Web-side per-field rendering of extension fields over HTTP waits for the schema endpoint.
- [ ] A2.3.8. Retarget `GenerateCrudCommand.ExposesExtraFieldsInApi` from kind `web` (a
      pre-split rule) to `webapp+api`: under the three-way model only `webapp+api` maps an API
      surface, so for a `web` host the extra-field endpoint code is currently emitted but never
      mapped (dead-but-harmless).

**A2.4 — Cross-process consistency** ✅ COMPLETE (by construction)

- [x] A2.4.1. No dual-write logic needed. The Web project performs no local persistence; every
      mutation is one HTTP call to the API, which keeps using its existing single-process
      transactional-outbox/inbox/`TransactionBehavior` pipeline unchanged.

**A2.5 — Validate end-to-end**

- [x] A2.5.1. Both projects build 0 warnings.
- [x] A2.5.2. API boots standalone and answers its endpoints directly
      (`ApiIntegrationTests`: health, 401/403/200 permission gating, extension-field rules).
- [ ] A2.5.3. Web-side round-trip over HTTP: the API side is proven by the generated suite, and
      the Web host boots/serves pages (`WebAppSmokeTest`), but a full
      login-via-`/connect/token` → page create → visible-through-API round-trip is **not yet
      asserted by the generated tests** (see A5.5) — remaining validation.
- [ ] A2.5.4. Kill-the-API resilience check not run yet (the resilience handler should convert
      connection failures into retried/failed requests, not crashes — verify explicitly).

### A3. Propagate the kind model through the rest of the CLI ✅ COMPLETE (except A3.5)

- [x] A3.1. `ui add` — all UI commands (`UiAddCommand`, `UiRemoveCommand`, `UiUpdateCommand`,
      `UiInfoCommand`, `UiListCommand`, `UiSearchCommand`) target `inventory.UiProjectPath`
      (Web project for `webapp+api`); `api`-kind hosts are refused.
- [x] A3.2. `ui eject`/`ui diff` (`UiEject.IsInstalled`) resolve through `UiProjectPath`.
- [x] A3.3. `generate-crud` — covered by A2.3.2–A2.3.4.
- [x] A3.4. `migrate` commands audited: they discover modules by `*.Infrastructure.csproj` under
      `src/Modules` and use the `*.Api.csproj` as startup project — kind-agnostic by
      construction (the Web project hosts no modules).
- [ ] A3.5. `doctor` — not yet implemented: `doctor` prints the Web project/Program.cs presence
      for `webapp+api` but does **not** yet warn when the Web csproj picks up a
      module/DbContext package reference (the "no DB access" invariant guard).
- [x] A3.6. `modulus info` prints both project paths for `webapp+api` (`API :` / `Web :` rows).
- [x] A3.7. `AppKindTests` covers the webapp+api inventory/convenience-property fixtures
      (A0.7); the remaining commands were validated by regenerating a webapp+api app and
      running its full suite rather than per-command fixture tests.

### A4. Backward compatibility ✅ COMPLETE

- [x] A4.1. `AppKinds.Parse("web")` keeps working, mapped to `AppKind.WebApp` (one-time console
      note on the deprecated alias).
- [x] A4.2. Existing apps with `<ModulusAppKind>web</ModulusAppKind>` need zero changes;
      `AppKinds.Read` maps the legacy value and every command behaves as before.
- [x] A4.3. No auto-migration from `web` to `webapp+api` is offered — deliberate manual
      restructuring; documented here as a known limitation.

### A5. Testing story for the two-process kind

- [x] A5.1. API-side tests generated for `webapp+api` (`ApiIntegrationTests` +
      `AppSmokeTest` + boundary tests, factory over `ApiEntryPoint`).
- [x] A5.2. `WebAppSmokeTest` boots the Web project via a second
      `ModulusWebAppFactory<WebEntryPoint>` (`WebEntryPoint` marker in the Web host's
      Program.cs). *As built:* the two factories are standalone, not paired.
- [ ] A5.3. TestServer pairing (`ConfigurePrimaryHttpMessageHandler(() =>
      apiFactory.Server.CreateHandler())`) **not implemented** — the generated Web page tests
      currently assert page-level behavior (challenge / login page / landing) and make no API
      calls, so they pass without the API running. Pairing is the natural next step and would
      also close A2.5.3/A5.5.
- [ ] A5.4. No `Modulus.Testing` pairing helper added yet (do it together with A5.3).
- [ ] A5.5. The HTTP hop is proven on the API side (entity created through the API is visible
      via the API factory's client); the Web-page-driven flow is not yet asserted — remaining
      validation, same work as A2.5.3/A5.3.

---

## Phase B — Prebuilt & CLI-generated module UI customizability

An audit of `Modulus.UI.Identity` plus a second, separate audit of the other 7 prebuilt feature UI
packages (Users, Tenancy, Permissions, Settings, AuditLogging, Notifications, Files) and CLI
`generate-crud --with-ui` output — reading actual PageModels/`.cshtml`/entities/DbContexts, not
just AGENTS.md prose — found the customizability gap is real but **narrower than "fix every
package"**: most prebuilt packages are read-only or key-value-shaped by design and the
extension-fields mechanism genuinely doesn't apply to them. Summary:

| Package | Extension fields | Columns/actions ext. | New-page precedent | Per-page styling |
|---|---|---|---|---|
| Identity | **GAP** — entity marker, DbContext wiring, PageModel wiring all missing | N/A | untested | consistent (generic recipe works) |
| Users | **GAP** — same root cause as Identity (shares `ModulusUser`/`ModulusIdentityDbContext`) | N/A | untested | consistent |
| Tenancy | N/A by design — entity deliberately minimal, no create/edit form exists | N/A | untested | consistent |
| Permissions | N/A by design — read-only, editing lives in the management API | N/A | untested | consistent |
| Settings | N/A by design — key/value store with its own working contribution point (`SettingDefinition`) | N/A | untested | consistent |
| AuditLogging | N/A by design — immutable log, no EF context to attach a bag to | **GAP** — no `entity=`/contributed columns despite being a natural fit | untested | consistent |
| Notifications | N/A by design — no publish form by design | N/A | untested | consistent |
| Files | N/A by design — blob storage, no entity/DbContext at all | N/A | untested | consistent |
| CLI `generate-crud --with-ui` | **WORKING end-to-end** already | **WORKING end-to-end** already | N/A — app owns the file directly | N/A — app owns the file directly |

So the actionable work is: fix extension fields for Identity+Users together (same underlying fix),
wire AuditLogging into the existing column-extension mechanism, formalize new-page/per-page-styling
support as a pattern that provably works across *any* prebuilt package (not just Identity), and
explicitly document the N/A-by-design packages so nobody re-attempts a fix there later.

### B0. Extra-properties plumbing on `ModulusUser`/`ModulusIdentityDbContext` (shared fix)

This single fix unblocks **both** Identity's Register page (B2.1) and Users' Create/Edit pages
(B2.2), since they share the same entity and DbContext.

- [ ] B0.1. Read `IHasExtraProperties`'s exact member shape in
      `src/core/Modulus.Core/Abstractions/Entities/IHasExtraProperties.cs` first.
- [ ] B0.2. `src/identity/Modulus.Identity/Abstractions/ModulusUser.cs` — implement
      `IHasExtraProperties` (likely just `Dictionary<string, string?> ExtraProperties` plus
      whatever helper members `EntityFieldValues`/`SetExtraProperties` expects).
- [ ] B0.3. `src/identity/Modulus.Identity/EntityFrameworkCore/ModulusIdentityDbContext.cs` — do
      **not** change its base class. Call `UseModulusExtraProperties()`
      (`src/data/Modulus.EntityFrameworkCore/ModelBuilding/ExtraPropertiesModelBuilderExtensions.cs`,
      a standalone `ModelBuilder` extension with no `ModuleDbContext` dependency) directly from
      `OnModelCreating` after `builder.UseOpenIddict()`.
- [ ] B0.4. Do not make this context derive from `ModuleDbContext` — that would drag in
      multi-tenancy/outbox assumptions needing a much larger audit for no benefit.
- [ ] B0.5. Document the migration impact: new apps get the column from the template on day one;
      existing apps need `modulus migrate add AddUserExtraProperties --module Identity` after
      upgrading the framework package (normal migration workflow, not auto-applied) — call this
      out in release notes.
- [ ] B0.6. Validate: round-trip test on the new column (SQLite in-memory, matching existing
      Identity test fixtures); re-run the existing Identity end-to-end login/register suite to
      confirm no regression.

### B1. Forgot Password / Reset Password pages (Identity-specific)

`ModulusAccountController` (`src/identity/Modulus.Identity/ModulusAccountController.cs`) already
has `POST /account/forgot-password` (L52) and `/reset-password` (L73) with correct
anti-enumeration behavior and an `IIdentityEmailSender` hook — confirmed by reading the file.
**No new identity/token plumbing needed.** Only new Razor Pages UI. (This also doubles as the
worked example/validation case for B5's new-page pattern — see below.)

- [ ] B1.1. `src/ui/Modulus.UI.Identity/Pages/Account/ForgotPassword.cshtml(.cs)` +
      `ForgotPasswordConfirmation.cshtml` — mirror `ModulusAccountController.ForgotPasswordAsync`'s
      logic in-process (`UserManager.GeneratePasswordResetTokenAsync` +
      `IIdentityEmailSender.SendPasswordResetEmailAsync`), same non-committal response regardless
      of whether the email exists.
- [ ] B1.2. `ResetPassword.cshtml(.cs)` + `ResetPasswordConfirmation.cshtml` — `OnGet(email,
      token)` populates hidden fields from the reset-link's query string; `OnPostAsync` mirrors
      `ResetPasswordAsync`'s `userManager.ResetPasswordAsync(user, token, newPassword)` with the
      same uniform error handling.
- [ ] B1.3. Document (or add a small `IdentityLinkBuilder` helper for) the expected reset-link URL
      shape so custom `IIdentityEmailSender` implementations build correct links.
- [ ] B1.4. Add a "Forgot your password?" link on `Login.cshtml`.
- [ ] B1.5. Add `ForgotPassword.*`/`ResetPassword.*` keys to the existing `IdentityUiLocalization`
      resource, following `Login.*`/`Register.*` naming.
- [ ] B1.6. No new options toggle needed — `IIdentityEmailSender`'s fail-closed no-op default
      already makes the flow inert until an app wires a real sender.
- [ ] B1.7. Scope the `webapp+api` HTTP-calling variant (POSTing to the existing
      `/account/forgot-password`/`/reset-password` endpoints instead of local `UserManager` calls)
      as a fast-follow within Phase A2.2.6, once these page shapes/localization keys exist to port
      from.
- [ ] B1.8. Validate end-to-end: regenerate an app, hit both pages over real HTTP with a stub
      `IIdentityEmailSender` capturing the token, complete a full reset, log in with the new
      password.

### B2. Wire extension fields into existing forms

Reuses `ConfigureEntityUi`/`EntityField`/`m-fields` exactly as CRUD entities already do (confirmed
working end-to-end in `cli/Templates/ui/CrudIndexPageModel.sbn` — use it as the reference
implementation) — no new registry API needed, only making these PageModels consumers of it.

- [ ] B2.1. **Register (Identity)** — `src/ui/Modulus.UI.Identity/Pages/Account/Register.cshtml.cs`:
      inject `IEntityUiRegistry` + an anonymous-safe `ICurrentUser` (confirm one is resolvable
      pre-authentication the same way other `[AllowAnonymous]` pages already handle it — a field
      gated by `RequiredPermission` should correctly stay hidden on the public form). Add
      `[BindProperty] Dictionary<string, string?> Extra`. Call `registry.ValidateEntityFields(
      "Identity.User", currentUser, Extra, ModelState, "Extra")` before creating the user. Because
      `UserManager.CreateAsync` is the only API that hashes the password, extra fields can't be
      set pre-create — set `user.ExtraProperties` from `registry.ReadEntityFieldText(...)`
      **after** `CreateAsync` succeeds, then a second `UpdateAsync` call (verify this two-step
      pattern doesn't clash with any Identity audit/event hook that assumes single-write
      semantics). `Register.cshtml` — add `<m-fields entity="Identity.User" for="Extra" />`
      matching the exact tag shape already used in `ui/CrudFormPartial.sbn`.
- [ ] B2.2. **Users admin Create/Edit** — `src/ui/Modulus.UI.Users/Pages/Users/Create.cshtml.cs`
      (and the Edit page, if one exists) — same `"Identity.User"` entity key as Register (not a
      separate key), so a field configured once via `ConfigureEntityUi("Identity.User", ...)`
      shows in both the public registration form and the admin-created-user form, each filtered by
      the calling `ICurrentUser`'s permissions independently. Same wiring pattern as B2.1: inject
      `IEntityUiRegistry`, add `Extra` bound property, validate before create, set
      `ExtraProperties` + `UpdateAsync` after create, render `<m-fields entity="Identity.User"
      for="Extra" />`.
- [ ] B2.3. App-side field registration needs zero options-API changes for either page — it
      already works via the same `ConfigureEntityUi` call shape CRUD entities use, purely
      additive.
- [ ] B2.4. Validate end-to-end: configure a field on `Identity.User` in a generated app, confirm
      it renders on **both** `/Account/Register` and the admin `/Users/Create` page, rejects an
      invalid value server-side on each, and the value round-trips to
      `AspNetUsers.ExtraProperties` from either entry point.

### B3. AuditLogging column-extension wiring

Distinct, smaller gap: AuditLogging is a correctly-immutable append-only log (no field/write
extension should ever apply there), but its list view isn't wired into the existing **read-path**
column-extension mechanism (`EntityColumn`/`IEntityColumnValueProvider`, `<m-entity-columns/>`/
`<m-entity-cells/>`) the way CLI-generated CRUD lists already are — a natural fit for e.g. another
module contributing a computed/joined column to the log browser.

- [ ] B3.1. `src/ui/Modulus.UI.AuditLogging/Pages/.../Index.cshtml` — add `entity="AuditLogging.Entry"`
      to the existing `<m-datatable>`, plus `<m-entity-columns/>` in the header and
      `<m-entity-cells entity="AuditLogging.Entry" row-id="@entry.Id" values="..." />` per row,
      matching the pattern documented in AGENTS.md's "Extension columns and row actions" section.
- [ ] B3.2. `Index.cshtml.cs` — load contributed column values once per page via
      `registry.LoadEntityColumnsAsync(...)`, same as a CLI-generated list page's `LoadAsync`.
- [ ] B3.3. No row actions needed here (the log has no per-row mutation), so skip
      `<m-entity-actions/>` — columns only.
- [ ] B3.4. Validate: contribute a test column via `ConfigureEntityUi("AuditLogging.Entry", ...)`
      in a generated app, confirm it renders in the audit log browser.

### B4. N/A-by-design packages — document, don't fix

Tenancy, Permissions, Settings, Notifications, and Files were confirmed (by reading their entities
and PageModels) to have no data shape the extension-fields mechanism applies to — deliberately
minimal/immutable entities, key-value stores, or blob storage with no row entity at all. This is
correct as designed, not an oversight.

- [ ] B4.1. Add a short note to `docs/UI_FRAMEWORK_GUIDELINE.md` listing these five packages and
      why extension fields don't apply to each (one line per package, from the table above), so a
      future contributor doesn't reopen this as a "gap" without re-deriving the same conclusion.
- [ ] B4.2. Settings already has its own working contribution point (`SettingDefinition` +
      `ISettingDefinitionRegistry`) for the equivalent need ("a module wants to contribute
      configuration") — cross-reference it from the same doc note so it's discoverable.

### B5. New-page-inside-module precedent (generalized across all prebuilt packages)

Standard Razor Pages RCL semantics already support this — the app's own file at an unclaimed route
under any prebuilt package's route folder (`/Account/*`, `/Users/*`, `/Tenancy/*`, ...) simply adds
a new page; it only conflicts (and the app wins) at a route the package already claims, per
`UiEject`'s existing "entry assembly beats package" precedence. What's missing across **all**
7 packages is validation, not code — B1's Forgot/Reset Password pages double as the first real,
tested instance of this pattern for Identity; the doc/test work below generalizes it explicitly so
it's not read as an Identity-only trick.

- [ ] B5.1. Add an "Adding a new page to a prebuilt module's area" section to
      `docs/UI_FRAMEWORK_GUIDELINE.md` with a worked example (`Pages/Account/Mfa.cshtml`,
      `@page "/Account/Mfa"`, themed via `Context.GetThemeLayout()` like generated CRUD pages),
      and a one-line note that the same mechanism applies to every prebuilt package's route folder
      (Users, Tenancy, Permissions, Settings, AuditLogging, Notifications, Files), not just
      Identity's `/Account`.
- [ ] B5.2. Add a test fixture (extending the existing "regenerate a web app, hit all views"
      end-to-end test) that adds a sibling page under **at least two different** prebuilt
      packages' route folders (e.g. one under `/Account/*` via B1's own new pages, one under
      `/Users/*`) and asserts both resolve over HTTP alongside their package's own pages — proving
      the pattern generalizes rather than only validating it once.

### B6. Per-page visual customization (generalized)

No new framework abstraction — extend the existing `ISlotContributor`/`UiSlots.Head` pattern.
Confirmed consistent across all 7 prebuilt packages (all use `Context.GetThemeLayout()` the same
way; zero existing `ISlotContributor`/`UiSlots.Head` usage anywhere) — this is genuinely a single
generic recipe, not something to special-case per package.

- [ ] B6.1. Add a path check against `ctx.ViewContext.HttpContext.Request.Path` inside a new
      `ISlotContributor` implementation (already available inside `RenderAsync`, confirmed in
      `Slots.cs`).
- [ ] B6.2. Document as a recipe in `docs/UI_FRAMEWORK_GUIDELINE.md`: "for markup changes, eject;
      for conditional head/asset injection on one page (any prebuilt package or a CLI-generated
      page), use a path-conditional `ISlotContributor`." No new interface — matches the "don't
      design speculative abstractions" constraint.
- [ ] B6.3. Confirm global branding (logo/app name/footer via `BrandingOptions`) already works
      with zero eject and needs no changes — call this out in the doc so users don't reach for
      eject unnecessarily.
- [ ] B6.4. Validate via the B5 test fixture: assert the injected markup appears only on the
      targeted page, for a page in at least one prebuilt package.

### B7. CLI `generate-crud --with-ui` — confirmed working, document only

Traced end-to-end through the Scriban templates and found **already fully wired**: the generated
entity implements `IHasExtraProperties` unconditionally (`cli/Templates/module/Domain/Entity.sbn`),
the generated `{Module}DbContext : ModuleDbContext` picks up `UseModulusExtraProperties()`
automatically, `CrudIndexPageModel.sbn` calls `ValidateEntityFields`/`ReadEntityFieldText` on both
create and edit paths, `CrudFormPartial.sbn`/`CrudEditFormPartial.sbn` render `<m-fields>`, and
`CrudTablePartial.sbn` wires `<m-entity-columns/>`/`<m-entity-cells/>`/`<m-entity-actions/>` too —
ahead of every prebuilt package including AuditLogging pre-B3. New-page and per-page-styling
concerns don't apply here at all: `GenerateCrudCommand.cs` writes pages directly into the app's own
project, not a package, so there's no RCL-precedence question — adding a page or a `<style>` block
is just editing the app's own file.

- [ ] B7.1. No code changes needed. Add one line to `docs/UI_FRAMEWORK_GUIDELINE.md` noting this
      was audited and confirmed complete, so it isn't re-investigated as a suspected gap later.

### Design decision: no new `Modulus.UI.Core` abstraction for extension-fields wiring

Considered and rejected during planning: a shared base PageModel or a bigger `Modulus.UI.Core`
helper to reduce the per-package wiring in B2/B3. `EntityFieldValues`/`EntityApiFields`
(`src/ui/Modulus.UI.Core/Entities/`) already provide the right-sized helpers (`ValidateEntityFields`,
`ReadEntityFieldText`, and their API-DTO equivalents) — the CLI's generated PageModel already calls
them in ~4-5 lines per form handler, which is close to irreducible since each call site differs in
ModelState prefix, the 422-partial name, and the post-success response shape.

The actual duplication is two prerequisite steps `Modulus.UI.Core` structurally **cannot** own: the
entity implementing `IHasExtraProperties` (a per-package entity-author decision) and the DbContext
calling `UseModulusExtraProperties()` (automatic for any `ModuleDbContext` subclass, but Identity's
context is deliberately bespoke, non-`ModuleDbContext`, because it derives from ASP.NET Identity's
own `IdentityDbContext`). `Modulus.UI.Core` reaching into either would mean referencing each
package's own entity/DbContext types, violating the enforced dependency rule (feature UIs depend on
`Theme.Abstractions` only, never cross-reference each other or reach into `Modulus.Identity`/
`Modulus.MultiTenancy.EntityFrameworkCore`). **Decision: no new Core abstraction — B0/B2/B3 are
narrow, package-specific fixes, not a framework design gap.** Don't re-litigate this without new
information.

---

## Cross-cutting risks

| Area | Risk | Mitigation |
|---|---|---|
| A2 auth relay | Largest net-new security-sensitive code (hand-rolled bearer-token cookie relay between two local processes) | Scope tightly (Login + typed clients first), review/test with the same rigor as the existing OIDC/code-flow work — expired token, refresh, revoked, wrong audience. |
| A2 registry-over-HTTP | New `Modulus.UI.Core` abstraction, not just CLI scaffolding | Explicit design sign-off before building; unfiltered fallback available as a documented-gap interim. |
| A0 inventory rework | Wide blast radius (~dozen call sites read the old single-project properties) | Full grep audit before starting; keep old properties working via the `UiProjectPath` convenience during transition. |
| B0 schema change | New required column on `AspNetUsers` for every future app; existing apps need a manual migration after upgrade | Document in release notes; no auto-migration (matches existing framework convention). |
| B2 double-save | `CreateAsync` can't carry extra fields pre-hash; needs a second `UpdateAsync`, now on two PageModels (Register + Users Create) instead of one | Confirm no Identity audit/event hook assumes single-write semantics; keep the pattern identical between both call sites. |
| Backward compat | Silent surprise if `web` → `webapp` alias isn't communicated | One-time console note on `--kind web`; release notes. |

## Verification summary

Per framework convention (regenerate off a packed build, 0 warnings, boot, exercise over real
HTTP):

- **A1**: `api`/`webapp` build clean; `webapp` boots with working sign-in but `/api/*` returns
  404; `api` has no `/Account/*`.
- **A2**: both `webapp+api` projects build clean; API-side CRUD round-trip, permission gating
  and extension-field rules proven by the generated suite (9/9). Still open: a Web-page-driven
  round-trip over HTTP (A2.5.3) and the kill-the-API check (A2.5.4).
- **A3**: `generate-crud`, the `ui *` family and `modulus info` each target the right project(s)
  for a `webapp+api` app (UiProjectPath routing); validated by regeneration, not per-command
  fixtures. `doctor`'s Web-project invariant warning still open (A3.5).
- **A5**: generated smoke tests pass for all three kinds (`webapp+api`: 9/9; `api`: 6/6;
  `webapp`: 3/3). The Web factory is standalone — TestServer pairing (A5.3) not implemented.
- **B0–B6**: not started.

---

## Appendix: reference architectures considered

Three generic "professional enterprise .NET repo" layouts were reviewed while designing the
three-way `AppKind` split, to check Modulus's target shape against common practice. `webapp` maps
to Reference 1, `webapp+api` maps to Reference 2. Reference 3 (microservices) does **not** map to
anything in this plan — see the Non-goal note above; kept here only so the boundary is legible
without re-deriving it from scratch.

### Reference 1 — Razor Pages modular monolith, no external API (→ `webapp`)

```
MyApp/
├── .github/workflows/{build-and-test,deploy}.yml
├── build/{Directory.Build.props,Directory.Packages.props,common.props}
├── docs/architecture/{module-boundaries.md,adr/0001-modular-monolith.md}
├── docker/{Dockerfile,docker-compose.yml,docker-compose.override.yml}
├── src/
│   ├── Host/MyApp.Web/                       # composition root only
│   │   ├── Program.cs, appsettings.*.json, wwwroot/
│   │   ├── Pages/{Shared/_Layout.cshtml, Index.cshtml, Error.cshtml}
│   │   ├── HealthChecks/ModuleHealthCheck.cs
│   │   └── Middleware/{ExceptionHandlingMiddleware,RequestLoggingMiddleware}.cs
│   ├── Modules/
│   │   ├── Orders/
│   │   │   ├── MyApp.Modules.Orders/                # Pages + composition (OrdersModule.cs)
│   │   │   ├── MyApp.Modules.Orders.Domain/          # Entities, ValueObjects, Enums, Exceptions
│   │   │   ├── MyApp.Modules.Orders.Application/     # Commands, Queries, Validators, Mappings
│   │   │   ├── MyApp.Modules.Orders.Infrastructure/  # DbContext, Configurations, Migrations, Repos
│   │   │   └── MyApp.Modules.Orders.Contracts/       # Dtos, Events, IOrdersModuleApi.cs
│   │   ├── Inventory/  (same 5-project pattern)
│   │   └── Users/      (same 5-project pattern)
│   └── Shared/
│       ├── MyApp.SharedKernel/            # IModule, IEntity, IAggregateRoot, IEventBus, Result
│       └── MyApp.SharedInfrastructure/    # Logging, Caching, Security, ModuleDbContextBase
├── tests/{UnitTests,IntegrationTests,ArchitectureTests}/   # ArchitectureTests enforces module boundaries (NetArchTest)
├── .editorconfig, .gitignore, Directory.Build.props, global.json, NuGet.Config, MyApp.sln
```

Professional touches: architecture tests enforcing module isolation, 5-layer-per-module split
(adds a `Contracts` project), ADRs, central package management, health checks, per-environment
configs. Modulus's own module template already covers the 4-layer core of this (Domain/
Application/Infrastructure/Presentation collapsed the separate Contracts/IntegrationEvents/Tests
projects on purpose — see "CLI 4-layer rewrite" in AGENTS.md); `Modulus.Testing.Architecture`
already plays the role of `MyApp.ArchitectureTests` here.

### Reference 2 — separate API (modular monolith) + Web app (→ `webapp+api`)

```
MyApp/
├── .github/workflows/{api-build-deploy,web-build-deploy}.yml
├── build/{Directory.Build.props,Directory.Packages.props}
├── docs/{architecture/,api/openapi.yaml}
├── docker/{api/Dockerfile, web/Dockerfile, docker-compose.yml}
├── src/
│   ├── Web/MyApp.Web/                        # consumer only, NO DB access
│   │   ├── Program.cs, appsettings.*.json, wwwroot/
│   │   ├── Pages/{Shared/_Layout.cshtml, Orders/, Inventory/, Users/}
│   │   ├── ApiClients/
│   │   │   ├── Abstractions/{IOrdersApiClient,IInventoryApiClient}.cs
│   │   │   ├── OrdersApiClient.cs, InventoryApiClient.cs
│   │   │   └── Policies/RetryPolicyFactory.cs        # Polly resilience
│   │   ├── Models/ViewModels/
│   │   └── Authentication/TokenForwardingHandler.cs
│   ├── Api/MyApp.Api/                        # composition root for the API
│   │   ├── Program.cs, appsettings.*.json
│   │   ├── Modules/
│   │   │   ├── Orders/
│   │   │   │   ├── MyApp.Modules.Orders.Api/          # Endpoints (OrdersModule.cs, OrdersEndpoints.cs)
│   │   │   │   ├── MyApp.Modules.Orders.Domain/
│   │   │   │   ├── MyApp.Modules.Orders.Application/
│   │   │   │   ├── MyApp.Modules.Orders.Infrastructure/
│   │   │   │   └── MyApp.Modules.Orders.Contracts/
│   │   │   ├── Inventory/ (same pattern)
│   │   │   └── Users/     (same pattern)
│   │   ├── Middleware/{ExceptionHandlingMiddleware,ApiKeyMiddleware}.cs
│   │   └── HealthChecks/
│   └── Shared/
│       ├── MyApp.SharedKernel/, MyApp.SharedInfrastructure/
│       └── MyApp.Contracts/                  # DTOs shared between Api & Web (NuGet-able)
├── tests/{Api.Modules.Orders.UnitTests, Api.IntegrationTests, Web.UnitTests, ArchitectureTests}/
├── .editorconfig, .gitignore, global.json, MyApp.sln
```

Professional touches: Polly resilience policies on the Web→API `HttpClient`s, a `TokenForwardingHandler`,
a shared versioned `Contracts` project instead of duplicated DTOs, an OpenAPI spec tracked in
`docs/api/`, separate Dockerfiles per deployable. This is the direct model for Phase A2:
`RetryPolicyFactory`/Polly ↔ `AddModulusHttpClient<T>()`'s standard resilience handler (A2.3.1),
`TokenForwardingHandler` ↔ `TokenRelayHandler` (A2.2.4), `MyApp.Contracts` ↔ `{App}.Shared.Contracts`
(A2.1.2).

### Reference 3 — microservices (informational only — not a target, see Non-goal above)

```
MyApp/
├── .github/workflows/  (one CI per service + web + gateway)
├── docs/architecture/{service-map.md, event-catalog.md}, docs/adr/
├── deploy/
│   ├── docker-compose.yml (+ .override.yml)
│   └── k8s/{namespaces/, orders-service/{deployment,service,hpa}.yaml, inventory-service/,
│            users-service/, gateway/, infra/{rabbitmq,redis,sql-servers}.yaml}
├── src/
│   ├── ApiGateway/MyApp.Gateway/            # YARP or Ocelot reverse proxy
│   ├── Services/
│   │   ├── Orders/MyApp.OrdersService/      # fully independent .NET solution, own repo-able
│   │   │   ├── src/MyApp.OrdersService.{Api,Domain,Application}/
│   │   │   ├── src/MyApp.OrdersService.Infrastructure/
│   │   │   │   ├── Persistence/{OrdersDbContext.cs, Migrations/}     # OWN database
│   │   │   │   └── Messaging/{Publishers/OrderCreatedPublisher.cs, Consumers/InventoryReservedConsumer.cs}
│   │   │   ├── src/MyApp.OrdersService.Contracts/Events/OrderCreatedEvent.cs
│   │   │   ├── tests/{UnitTests,IntegrationTests}/, Dockerfile, MyApp.OrdersService.sln
│   │   ├── Inventory/MyApp.InventoryService/ (identical internal structure, own DB, own solution)
│   │   └── Users/MyApp.UsersService/         (identical internal structure, own DB, own solution)
│   ├── Web/MyApp.Web/                        # Razor Pages BFF, talks to the Gateway only
│   └── Shared/
│       ├── MyApp.EventBus.Contracts/         # shared event schema library (versioned NuGet)
│       └── MyApp.BuildingBlocks/{Observability,Resilience,Security}/
├── infra/terraform/{modules/,environments/{dev,prod}/}, infra/monitoring/{grafana,prometheus}/
```

| Aspect | Modular Monolith (Refs 1–2) | Microservices (Ref 3) |
|---|---|---|
| Deployment | One process/container per host project | One container per service |
| Database | Shared DB (schema-per-module) | DB-per-service, no cross-service joins |
| Communication | In-process calls, or one HTTP hop to a sibling process | HTTP via Gateway + async messaging |
| Solution file | One `.sln` | One `.sln` per service, each independently repo-able |
| Shared code | `SharedKernel`/`Contracts` referenced directly or via a shared project | Only versioned NuGet packages — no shared runtime code |
| Scaling | Scale the whole app (or the one extra Web process) | Scale each service independently |

Modulus already has the primitives Reference 3 would need if a team chose to go there by hand
(`Modulus.Sagas`, `Modulus.Outbox`/`Inbox`, `EventBus.RabbitMQ`/`EventBus.Kafka`, per-module
`DbContext` schema isolation) — what it deliberately does not do, and what this plan does not add,
is generate the gateway/`deploy/k8s`/per-service-repo scaffolding itself.
