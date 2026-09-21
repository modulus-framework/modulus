# Modulus.UI Framework Guideline (v2)

> **Provenance & repo status.** This document is the adopted v2 design guideline for the
> Modulus UI framework. It supersedes [`UI_FRAMEWORK_PLAN.md`](UI_FRAMEWORK_PLAN.md)
> (the v1 plan). Note that a **v1 UI framework already exists in the repository**
> (`src/ui/` — `Modulus.UI.Core` + 8 module UI RCLs, `modulus ui` CLI commands): scoped
> `HtmxResponse`, `HtmxPageModel` (PageOrPartial / 422 flow), `IsHtmxFragment()`,
> `UiNavigationRegistry` + permission-filtered menus, `PermissionTagHelper`, vendored
> htmx/Alpine/Tabler assets, and the `_UiLayout` shell. Per review, **v1 is evolved in
> place** rather than duplicated — no parallel `Modulus.UI.Htmx` package until a
> breaking major retires `Modulus.UI.Core`. Two naming deviations from this document:
> `HtmxResponse.Refresh(entityEvent)` is implemented as **`NotifyChanged(entityEvent)`**
> (name collision with the existing HX-Refresh `Refresh()`), and the contributor model
> lives in `Modulus.UI.Core` (not `UI.Htmx`).

---

## 1. Purpose & Non-Negotiables

Modulus.UI is the built-in, server-rendered web UI framework for Modulus applications.

Design principles (non-negotiable):

1. **Server-first & HTMX-driven.** Server-rendered HTML fragments; no SPA framework
   (no React/Vue), no Blazor. Interactivity is progressive (htmx + Alpine.js).
2. **Theme packages over core templates.** The framework ships only abstractions +
   a default reference theme. Themes are modules (RCLs) that can be replaced.
3. **Module-first extensibility.** Every UI concern — navigation, toolbars, dashboard
   widgets, CRUD lists/forms — is a module contribution. Core never hard-codes feature
   UI.
4. **Security by default.** Permission-aware navigation, XSS-safe by construction,
   CSP-safe JS, CSRF tokens everywhere, no inline scripts.
5. **Progressive enhancement.** Pages render fully without JS; htmx enhances.
6. **Monolith-first, tiered-ready.** UI calls Application handlers via an abstraction
   so the same view model pipeline works in monolith or tiered deployment.

## 2. Layering & Package Layout

Packages (see §8 for the review status):

| Package | Role |
|---|---|
| `Modulus.UI.Theme.Abstractions` | ITheme, ThemeAsset, StandardLayouts, ThemeOptions, slot names, design token contract |
| `Modulus.UI.Core` | HTMX response model, page model, fragment detection, contributor model, view resolver, slot renderer, asset & endpoint wiring |
| `Modulus.UI.Htmx` | Merged into Core for now (v1 seam) |
| `Modulus.UI.Theme.Tabler` | Default reference theme: 4 layout variants, shell partials, error views, tokens → Tabler variable mapping |
| `Modulus.UI.{Module}` | Prebuilt feature modules: Identity, Users, Permissions, Tenancy, Settings, AuditLogging, Notifications, Files |
| `Modulus.UI.Contracts` | Merged into Core for now (no separate consumer project yet) |

Dependency direction (strict, no upward references):

```
Module UIs ──► UI.Core ──► Theme.Abstractions ──► (Modulus.Core)
     │                                    ▲
     └────────► Theme.Tabler ◆────────────┘
```

## 3. Core Building Blocks

### 3.1 HTMX Response Model (scoped)

```csharp
public sealed class HtmxResponse
{
    public HtmxResponse Trigger(string eventName, object? detail = null);
    public HtmxResponse CloseModal();                    // modulus:modal:close
    public HtmxResponse NotifyChanged(string entityEvent); // raises {module}.{entity}:changed
    public HtmxResponse Redirect(string url);            // HX-Redirect
    public HtmxResponse Refresh();                       // HX-Refresh (full reload)
    public HtmxResponse PushUrl(string url);             // HX-Push-Url
    public HtmxResponse Retarget(string selector, string swap = "innerHTML");
    public HtmxResponse Reswap(string strategy);
    public HtmxResponse Noop();
}
```

The response model is **scoped per request**, injectable into handlers and page models.
All triggers are merged into a single `HX-Trigger` header.

### 3.2 HtmxPageModel

Base class for pages that work in both full-page and fragment modes:

```csharp
public abstract class HtmxPageModel : PageModel
{
    protected abstract Task HandleAsyncCore(CancellationToken ct);

    public Task<IActionResult> PageOrPartial(string pageView, string partialView);
    // full → pageView; htmx fragment → partialView;
    // on ValidationException → 422 + form partial re-render with errors
}
```

Fragment detection: `Htmx-Request: true` **and not** `HX-History-Restore-Request` **and
not** `HX-Boosted` (boosted requests must return the full shell).

### 3.3 View Resolution Chain

For every reusable UI component (tables, forms, cards, tabs, pagination, toasts):

1. App override: `/Views/Shared/Modulus/{Component}/{View}.cshtml`
2. Active theme: `/Themes/{Theme}/Views/{Component}/{View}.cshtml`
3. Framework default: `/Views/Shared/Modulus/_Default/{Component}/{View}.cshtml`

`IModulusViewResolver.Resolve(component, view = "Default")` probes this chain
(`IRazorViewEngine.GetView` absolute-path probes, cached). Missing everywhere →
`InvalidOperationException` (fail fast).

## 4. Themes

### 4.1 ITheme

```csharp
public interface ITheme
{
    string Name { get; }
    string GetLayout(string layoutName);          // absolute path to the layout view
    IReadOnlyList<ThemeAsset> Styles { get; }     // css bundle(s)
    IReadOnlyList<ThemeAsset> Scripts { get; }    // js bundle(s)
}
public sealed record ThemeAsset(string Path, bool Defer = true, string? Integrity = null);
```

### 4.2 Layouts (standard)

| Name | Use |
|---|---|
| `Application` | Authenticated app: sidebar + topbar + content |
| `Account` | Login, MFA, reset password: centered card, no chrome |
| `Empty` | Bare host page: only assets + RenderBody |
| `Public` | Marketing/landing: topbar + footer, no sidebar |

`ThemeOptions.Layouts` maps page layout names → shell variants, seeded with identity
by default; apps remap (e.g. `"Admin" → "Application"`).

### 4.3 Tabler reference theme

- `Themes/Tabler/Layouts/{Application,Account,Empty,Public}.cshtml`
- Shell partials: `_Sidebar`, `_Topbar`, `_Footer`
- Error views: `403`, `404`, `500` (standalone, render without a layout so they work
  even when everything else is broken)
- `css/modulus.css` — design-token layer only: `--m-*` variables mapped onto Tabler
  variables; no component CSS
- `js/modulus.js` — the framework runtime (below)

### 4.4 Design tokens

Canonical tokens (namespace `--m-*`): Primary, PrimaryForeground, FontSans, Radius,
SidebarWidth, SidebarBg, PageMaxWidth, TableDensity. Theme CSS maps them to the
vendor's variables. Apps override `--m-*` only.

## 5. modulous.js Runtime (CSP-safe, no inline JS)

Global `Modulus` object; data-attribute driven; **no inline JavaScript anywhere**:

```js
window.Modulus = {
  config: { csrfHeader, antiforgeryToken, timezone, locale },
  onLoad(el, cb),                    // runs on initial + every hx swap-in
  components: { /* name → { mount, unmount } */ },
  toast(type, message),
  confirm(options) : Promise<bool>,
  getAntiforgeryToken()
};
document.body.addEventListener('htmx:afterSwap', ...);  // dispatches modulus:loaded
```

- **422 handling:** `htmx:responseError` with status 422 swaps the returned form
  fragment (server re-render with model errors) — no client-side validation engine.
- **Entity events:** `HX-Trigger` events matching `*.*:changed` are picked up by
  `m-datatable[source]` grids (listen on `from:body`) and refresh themselves.
- **Modals:** `hx-get` into `#m-modal-container`; `modulus:modal:close` removes it and
  restores focus.

## 6. Component Library (server-rendered partials + tag helpers)

| Component | Notes |
|---|---|
| `m-datatable` | Attribute-driven grid. `source` = server endpoint returning **rendered `<tr>` fragments** (htmx-style). Client-side sort/filter is delegated to the server query. |
| `m-form` | Wraps `<form hx-post>`, injects antiforgery, renders validation summary/field errors |
| `m-modal` | htmx-loaded dialog, focus trap, ESC/backdrop close |
| `m-tabs` | URL-hash-synced tabs |
| `m-card` / `m-stat` | Dashboard tiles (slots-fed) |
| `m-breadcrumbs` | From `IBreadcrumbContributor` |
| `m-toolbar` | Page actions from `IToolbarContributor` |
| `m-user-menu`, `m-theme-switch` | Shell components |
| `PermissionTagHelper` | `<m-permission permission="…">…</m-permission>` |

## 7. Extensibility Model (the module contract)

```csharp
public interface IMenuContributor      { ValueTask ConfigureAsync(MenuConfigurationContext ctx, CancellationToken ct = default); }
public interface IToolbarContributor   { ValueTask ConfigureAsync(ToolbarContext ctx, CancellationToken ct = default); }
public interface ISlotContributor      { string Slot; int Order; Task<IHtmlContent> RenderAsync(SlotContext ctx); }
public interface IBreadcrumbContributor{ … }
public interface IWidgetContributor    { … }   // dashboard widgets → Slot "Dashboard"
```

- Contributions run in **module registration order**; registries freeze after the
  module configuration phase.
- `MenuConfigurationContext.Main` exposes `AddGroup/AddItem/Find/Remove/MoveTo/Order`,
  so later modules can *restructure* earlier contributions (e.g. App web module moves
  "Users" under "Settings").
- Permission-filtering happens at render time (`ICurrentUser.HasPermission`), never at
  contribution time.
- Layout slots: `Topbar.Start/End`, `Sidebar.Top/Bottom`, `UserMenu`, `Page.Before/
  AfterContent`, `Footer`, `Head`, `Scripts`, `Dashboard`. Rendered by `ISlotRenderer`
  (`Html.ModulusSlotAsync("Topbar.End")`), ordered, permission-filtered.

## 8. Module UIs (v1 → v2 mapping)

| v1 package (exists today) | v2 direction |
|---|---|
| `Modulus.UI.Identity` | Login/MFA/profile pages (Account layout) |
| `Modulus.UI.Users` | User CRUD (m-datatable + m-form + m-modal) |
| `Modulus.UI.Permissions` | Permission matrix |
| `Modulus.UI.Tenancy` | Tenant management |
| `Modulus.UI.Settings` | Settings pages, per-tenant overrides |
| `Modulus.UI.AuditLogging` | Audit viewer |
| `Modulus.UI.Notifications` | Notification bell (Topbar.End slot) + list |
| `Modulus.UI.Files` | File browser (m-datatable) |

Each registers its `IMenuContributor`/`IToolbarContributor`/`ISlotContributor` from its
`ModulusModule.ConfigureServices`. The App web module runs **last** and restructures
the merged menu (groups, ordering, moving items under Settings).

## 9. Asset & Config

- Vendored libraries (htmx, Alpine CSP build, Tabler) are served from the theme's
  static web assets with SRI hashes; no CDN by default. `UiAssets.ContentBase`
  resolves the base path (works under `/_static` rewrites and RCL hosting).
- Config section `Modulus:Ui`:

```json
{
  "Modulus": {
    "Ui": {
      "Branding": { "AppName": "MyApp", "LogoUrl": "/img/logo.svg", "FaviconUrl": "/favicon.png", "FooterText": "© MyApp" },
      "Theme": { "Name": "Tabler", "ColorMode": "light", "AllowUserThemeSwitch": true },
      "Features": { "Boost": true, "Morph": true, "Breadcrumbs": true, "GlobalSearch": true }
    }
  }
}
```

- `IUserUiPreferenceStore` (theme switch, table density persistence) — abstraction in
  Core; EF/Redis implementations ship with the platform packages (placement TBD).

## 10. Security Checklist

- [x] CSP-safe (no inline `<script>`; Alpine `.build.csp` only)
- [x] CSRF: antiforgery token auto-injected into every `hx-post/put/delete` header
- [x] XSS: server-side encoding only; no `hx-on` inline JS by default
- [x] Permission-aware menu/toolbar/slots; deny-by-default (`NullCurrentUser` denies)
- [x] 403/404/500 themed pages; re-execute via `UseStatusCodePagesWithReExecute`
- [ ] Audit-log sensitive UI actions (Phase 2)

## 11. Phased Rollout

| Phase | Scope | Exit criteria |
|---|---|---|
| **1** | Theme.Abstractions, contributor model (menu/toolbar/slots), view resolver, Tabler theme (4 layouts + shell + errors + modulus.css/js), tests | Static pages render with the menu; fragment/full switching and 422 flow pass integration tests |
| 2 | Component library (m-datatable, m-form, m-modal, m-tabs), breadcrumbs, design-token migration of v1 module UIs | All 8 module UIs render on the Tabler theme with zero custom CSS |
| 3 | CLI: `modulus ui` module commands updated to new layouts/contributors; templates | Generated app boots with theme + sample CRUD end-to-end |
| 4 | Breaking: merge/retire v1 Core surface per this guideline (major version) | 0 references to removed types |

## 12. Open Questions (tracked)

1. View-resolver RCL path resolution must be proven by tests; fall back to view-location
   expanders if `GetView` cannot see RCL views.
2. `IUserUiPreferenceStore` package placement (abstractions in Core; EF/Redis impl in
   Platform packages?).
3. Tiered mode: add a dev-time guard when `IModulus` mediator seam is misconfigured.
4. Localization: wrap `IStringLocalizer` rather than replacing it with `IModulusLocalizer`
   (DataAnnotations validation is synchronous).
5. ValidationException error mapping should move to a structured
   `IDictionary<string, string[]>` instead of `"Prop: msg"` strings.
6. JS budget: ~50KB gz framework + vendor is realistic; "120KB total" is not with full
   Tabler.
