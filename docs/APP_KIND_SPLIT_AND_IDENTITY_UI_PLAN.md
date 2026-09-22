# App-kind split (API / Web app / Web app + API) + module UI customizability

Implementation plan for two independent efforts:

1. Replace the current two-value `api`/`web` app kind with a genuine three-way split, where
   `webapp+api` is **two separately deployable projects** that talk over HTTP (not one process).
2. Make **every** prebuilt feature UI package (Identity, Users, Tenancy, Permissions, Settings,
   AuditLogging, Notifications, Files) and every CLI-generated business-module UI as customizable
   as the CRUD scaffolding already is — extension fields, new pages, per-page styling — not just
   Identity/Account.

Status: planning complete, implementation not started. Update the step checkmarks below as work
lands; keep this doc in sync the way `ROADMAP_TIER3.md` is kept in sync with `AGENTS.md`.

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

- [ ] A0.1. `cli/Services/AppKind.cs` — replace the 2-value enum with 3: `Api`, `WebApp`,
      `WebAppApi`. `Names = ["api", "webapp", "webapp+api"]`.
- [ ] A0.2. Keep `Parse` accepting the legacy `"web"` as a **deprecated alias** mapping to
      `WebApp` (backward compat, see A4).
- [ ] A0.3. `cli/Services/ModuleDiscovery.cs` — `AppInventory` currently exposes one
      `ApiProjectPath`/`ProgramCsPath` (`Inventory()` hard-codes
      `Directory.EnumerateFiles(apiDir, "*.Api.csproj", ...)`). Add a parallel
      `WebProjectPath`/`WebProgramCsPath` (nullable).
- [ ] A0.4. Add a convenience property so most call sites migrate with a one-line swap:
      ```csharp
      public string? UiProjectPath => Kind == AppKind.WebAppApi ? WebProjectPath : ApiProjectPath;
      public string? UiProgramCsPath => Kind == AppKind.WebAppApi ? WebProgramCsPath : ApiProgramCsPath;
      ```
- [ ] A0.5. For `webapp+api`, read `Kind` from the API csproj (source of truth) — the Web csproj
      carries a matching `<ModulusAppKind>` for self-description only, never authoritative.
- [ ] A0.6. Before touching call sites: `grep -rn "ApiProjectPath\|ProgramCsPath" cli/` to produce
      the full call-site list (roughly a dozen across `cli/Commands/*.cs`) so A3 isn't a surprise.
- [ ] A0.7. Add a third fixture tree (webapp+api) to the existing `ModuleDiscovery.Inventory` unit
      tests, alongside the existing api-only/webapp-only fixtures.

### A1. `api` / `webapp` kinds (single project, smallest delta)

`webapp` = today's `web` minus the exposed API surface. Single project, pages still call
`IMediator` in-process.

- [ ] A1.1. `cli/Templates/app/Program.sbn` — gate only `app.MapControllers()` /
      `app.MapModulusEndpoints(...)` behind a new Scriban var `expose_api` (true for `api` and
      `webapp+api`'s API project, false for `webapp`). Leave `AddControllers()` (service
      registration), module registration, DbContext migration, mediator, UI wiring (`use_ui`)
      untouched — `webapp` still sets `use_ui = true`.
- [ ] A1.2. `cli/Services/AppKind.cs` — `AppKinds.ResolveCrudUi` gets a third arm for `WebAppApi`
      (see A2.3 for what it controls there).
- [ ] A1.3. `cli/Commands/NewAppCommand.cs`, `ResolveKind` (~L733) — 3-choice interactive menu;
      `--kind` accepts `api|webapp|webapp+api`; `--kind web` still parses (via the alias) with a
      one-line console note.
- [ ] A1.4. `GenerateAll` (~L300) — existing single-project path runs for `Api`/`WebApp` unchanged
      except passing `expose_api` into the template model. `webapp+api` is new (A2).
- [ ] A1.5. `AppModel.UseUi` (`Models.cs`) changes from `Kind == AppKind.Web` to
      `Kind is AppKind.WebApp or AppKind.WebAppApi`; same pattern for `UseTablerTheme`.
- [ ] A1.6. Grep `AppKind.Web\b` across `cli/` and convert each remaining comparison
      (`WithSignInPage`, `AuthNote`, the "Next steps" console output block) to the 3-way form.
- [ ] A1.7. Validate end-to-end (regenerate off a packed build, matching framework convention):
      `--kind api` and `--kind webapp` both build 0 warnings; `webapp` boots, sign-in works over
      HTTP, but `curl /api/<route>` returns 404 (proves the API surface is genuinely gone, not
      just hidden nav); `api` boots with no `/Account/*` routes.

### A2. `webapp+api` — the two-project HTTP split (highest risk/effort)

**A2.1 — Project layout & templates**

- [ ] A2.1.1. New `src/Web/{App}.Web/{App}.Web.csproj` (`cli/Templates/app/web.csproj.sbn`),
      `Microsoft.NET.Sdk.Web`, referencing `Modulus.UI.Core`, `Modulus.Platform`, the chosen
      theme — **no module Infrastructure/Presentation references at all**.
- [ ] A2.1.2. Check whether `Shared.Application` is already dependency-free enough to share DTOs
      from; if it references `Modulus.Mediator`/`Modulus.EntityFrameworkCore`, create a new
      lightweight `{App}.Shared.Contracts` project instead and share DTOs through that.
- [ ] A2.1.3. New `cli/Templates/app/Program.WebApp.sbn`: Razor Pages + typed clients + auth only,
      no `AddModulus(...)`, no DbContext/migration code.
- [ ] A2.1.4. The API project for this kind never gets `AddModulusUi`/`AddRazorPages`/theme
      packages — that responsibility moves entirely to Web.
- [ ] A2.1.5. `NewAppCommand.GenerateAll` — factor into `GenerateApiHost(...)` (today's logic,
      parameterized) plus a new `GenerateWebHost(...)`.
- [ ] A2.1.6. `WireUiModules` (~L430) needs a target-project parameter so UI packages install into
      the Web project for this kind.

**A2.2 — Auth: webapp process → API process on behalf of the signed-in user**

- [ ] A2.2.1. Keep the API as the OpenIddict token server, unchanged (it owns Identity's
      DbContext).
- [ ] A2.2.2. Build a new HTTP-calling Login page for the Web project (**not**
      `Modulus.UI.Identity`'s in-process page, which assumes local `UserManager`) that POSTs
      credentials to the API's existing `POST /connect/token` (password grant).
- [ ] A2.2.3. Store the access/refresh token in the Web project's own auth cookie
      (`.SaveTokens`-style).
- [ ] A2.2.4. Add a `DelegatingHandler` (`TokenRelayHandler`) attached to every generated typed
      client that reads the token off the cookie and refreshes via `/connect/token`
      (grant_type=refresh_token) near expiry.
- [ ] A2.2.5. New templates: `cli/Templates/webappapi/Program.Web.sbn`, `Login.cshtml(.cs).sbn`,
      `TokenRelayHandler.sbn`.
- [ ] A2.2.6. Scope the first cut to Login + typed-client auth only; Register/ForgotPassword-over-
      HTTP for `webapp+api` is an explicit fast-follow once Phase B1's page shapes exist
      (mechanical HTTP port, do not duplicate effort building it twice).
- [ ] A2.2.7. Flag prominently and review/test with the same rigor as the existing OIDC/code-flow
      work: this hand-rolled token relay is the single most security-sensitive net-new code in the
      whole plan (expired token, refresh, revoked, wrong audience, replay).

**A2.3 — Typed HTTP client per module, replacing in-process `IMediator` calls**

- [ ] A2.3.1. One typed client per module (`{Module}ApiClient`, mirrors the existing
      one-`{Module}Module`-per-module convention), registered via
      `AddHttpClient<{Module}ApiClient>()`.
- [ ] A2.3.2. `GenerateCrudCommand.GenerateUiCompanion` (~L283) — add a kind-aware branch: for
      `webapp+api`, write pages into the Web project.
- [ ] A2.3.3. New `ui/CrudIndexPageModel.Http.sbn` template whose PageModel calls
      `{Module}ApiClient` methods instead of `IMediator.Send(...)`, mapping non-2xx responses to
      `ModelState` in the same shape `ValidateEntityFields` already produces (existing `.cshtml`
      partials don't need to change).
- [ ] A2.3.4. `ResolveHost` (~L224) becomes kind-aware via the new `AppInventory` fields from A0.
- [ ] A2.3.5. For extension fields/columns/actions (`IEntityUiRegistry`) when the page has no
      local DB access: add a new small server-authoritative endpoint per module
      (`GET /{module}/{route}/ui-schema`, reusing
      `EntityUiRegistry.GetVisibleFields/Columns/Actions` server-side), returning a new
      `EntityUiSchemaDto` (small addition to `Modulus.UI.Core`).
- [ ] A2.3.6. Add a Web-side overload of the `m-fields`/`m-datatable` tag helpers that consumes
      the schema DTO. This is the **one piece of new framework abstraction** in Phase A (not just
      CLI scaffolding) — get explicit sign-off on this specific design before building it.
- [ ] A2.3.7. Cheaper fallback if schedule-constrained: serialize all extra fields unfiltered (no
      per-field permission gating), documented as a known v1 gap — not the default choice given
      the framework's existing rigor around permission-filtered rendering.

**A2.4 — Cross-process consistency**

- [ ] A2.4.1. No new dual-write logic needed — non-issue by construction. The Web project
      performs no local persistence; every mutation is one HTTP call to the API, which keeps
      using its existing single-process transactional-outbox/inbox/`TransactionBehavior`
      pipeline unchanged.

**A2.5 — Validate end-to-end**

- [ ] A2.5.1. Both projects build 0 warnings.
- [ ] A2.5.2. API boots standalone and answers its endpoints directly.
- [ ] A2.5.3. Web boots, logs in via `/connect/token`, a CRUD create/list/update/delete
      round-trips purely over HTTP (confirm via request logging that no in-process call happens).
- [ ] A2.5.4. Kill the API process and confirm the Web project fails gracefully, not a crash.

### A3. Propagate the kind model through the rest of the CLI

- [ ] A3.1. `ui add` (`cli/Commands/UiAddCommand.cs`) — swap `inventory.ApiProjectPath`/
      `ProgramCsPath` → `inventory.UiProjectPath`/`UiProgramCsPath` at the refusal check (~L43)
      and in `GetInstalledUiModules`/`WireModule` (~L81-155).
- [ ] A3.2. `ui eject`/`ui diff` (`cli/Services/UiEject.cs`) — same swap in `ResolveApiDir`
      (~L37).
- [ ] A3.3. `generate-crud` — covered by A2.3.
- [ ] A3.4. `migrate` commands — audit (don't assume) that no migrate command reads
      `ApiProjectPath` to *find* modules; they should already be kind-agnostic since modules live
      under `src/Modules` independent of host project.
- [ ] A3.5. `doctor` — add a `webapp+api`-specific guard: warn if the Web project has picked up a
      module/DbContext package reference (violates the "no DB access" invariant).
- [ ] A3.6. `info`/`list` — print both project paths for `webapp+api`.
- [ ] A3.7. Extend existing CLI command tests with `webapp+api` fixtures for each command touched
      above.

### A4. Backward compatibility

- [ ] A4.1. `AppKinds.Parse("web")` keeps working, mapped to `AppKind.WebApp` (closest existing
      semantics, non-breaking — no forced restructuring).
- [ ] A4.2. Existing apps with `<ModulusAppKind>web</ModulusAppKind>` already in their csproj need
      zero changes; `generate-crud`/`ui add`/etc. behave exactly as before for them.
- [ ] A4.3. No auto-migration from `web` to `webapp+api` is offered — that's a deliberate manual
      restructuring; document as a known limitation, not a gap to silently fix.

### A5. Testing story for the two-process kind

- [ ] A5.1. Generate `ApiSmokeTest.cs` for `webapp+api` (unchanged pattern from today).
- [ ] A5.2. Add a new `WebAppSmokeTest.cs` template that boots the API via
      `ModulusWebAppFactory<Program>` (existing pattern) **and** the Web project via a second
      `WebApplicationFactory`.
- [ ] A5.3. Repoint the Web factory's typed clients at the API factory's `TestServer`
      (`ConfigurePrimaryHttpMessageHandler(() => apiFactory.Server.CreateHandler())` — standard,
      supported `WebApplicationFactory` pairing pattern).
- [ ] A5.4. Optionally add a small `Modulus.Testing` convenience helper to avoid duplicating this
      wiring boilerplate in every generated app.
- [ ] A5.5. Validate: the generated test creates an entity via the Web page and asserts it's
      visible through the API factory's own client — proving the HTTP hop is real.

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
- **A2**: both `webapp+api` projects build clean; login issues a token via `/connect/token`; CRUD
  create/list/update/delete round-trips purely over HTTP (verified via request logging, not just
  success); killing the API process fails the Web app gracefully.
- **A3**: `generate-crud`, `ui add`, `ui eject`, `modulus info` each target the right project(s)
  for a `webapp+api` app and the app still builds/boots after each.
- **A5**: generated smoke tests pass for all three kinds; the `webapp+api` test proves the HTTP
  boundary is real (entity visible via the API factory's own client).
- **B0**: extra-properties round-trip test passes; a regenerated `webapp` app with
  `--auth openiddict` migrates and boots with the new column.
- **B1**: Forgot/Reset Password complete a full cycle over real HTTP with a stub email sender.
- **B2**: a configured `Identity.User` field renders on both `/Account/Register` and the admin
  `/Users/Create` page, validates server-side on each, and persists.
- **B3**: a contributed `AuditLogging.Entry` column renders in the audit log browser.
- **B5**: app-added sibling pages under at least two different prebuilt packages' routes resolve
  alongside each package's own pages.
- **B6**: path-conditional slot markup appears only on the targeted page.
