# Modulus UI Framework — Detailed Plan (ABP-style prebuilt UI modules)

Chosen direction: **Razor Pages + Tabler + HTMX/Alpine in Razor Class Libraries (RCLs),
server-rendered, with every UI action backed by the same Application handlers as the
JSON API (SPA-ready)**. Delivery is **both**: versioned NuGet RCL packages for
framework modules + CLI source-generation for business modules. First milestone:
**UI foundation + Identity**.

## 0. Architecture

```text
Prebuilt UI modules (NuGet RCLs: UI.Core, UI.Identity, UI.Permissions, UI.Tenancy, …)
                          │
UI framework (IUiModule, UiNavigationRegistry, menu/manifest endpoints, Tabler shell)
                          │
UI foundation (ASP.NET Core Razor Pages, Tabler, HTMX/Alpine)
```

API side stays as-is (Domain → Application → Infrastructure → Presentation).
UI modules are a **sidecar**: `Inventory` = API module + `Inventory.UI` RCL.
`IUiModule.ConfigureNavigation()` contributes menu; `ModuleManifest` declares
id/version/dependencies/features for CLI resolution. Registration order stays
authoritative (same rule as `IModule`).

Dual contract per screen: Razor Page (HTML, HTMX partials) + minimal/REPR JSON
endpoint calling the same mediator command/query. SPA clients use JSON only.

## 1. Packages

| Package | Contents | Depends on |
|---|---|---|
| `Modulus.UI.Core` (this step) | `IUiModule`, `UiModule`, `UiNavigationBuilder/Registry`, `ModuleManifest`, `AddModulusUi/AddUiModule`, `MapModulusUiMenu` (`/_ui/menu`, `/_ui/modules`) | `Modulus.Core` |
| `Modulus.UI.Identity` (next) | Login, users/roles/claims pages + nav; delegates user/role CRUD to app stores (framework has no user admin API by design) | UI.Core, Identity, Authorization.Management |
| `Modulus.UI.Permissions` | Role×permission matrix on `Authorization.Management` REST APIs | UI.Core |
| `Modulus.UI.Tenancy` | Tenant CRUD on `ITenantStore` | UI.Core |
| `Modulus.UI.Settings/Audit/Notifications/Files` | After Phase 0 backends land | UI.Core |

Backend gaps (Phase 0, after UI.Core): `ISettingStore` (+EF + mgmt API),
`IAuditLogStore` query over `[Audited]` history, `INotificationStore` + API,
localization resources. Each per-module `DbContext`-owned.

## 2. Navigation model (v1, implemented)

- `UiMenuItem(Id, Title, Url, Icon?, RequiredPermission?, Order, Children)`.
- `UiNavigationBuilder.AddGroup/AddItem` — dedup by `Id` (first wins), ordered by
  `Order` then title. Validation: non-empty id/title/url.
- `IUiNavigationRegistry` singleton; `AddUiModule<T>()` instantiates once and
  applies `ConfigureNavigation` immediately (idempotent per type).
- `MapModulusUiMenu(prefix="/_ui")`: `GET /_ui/menu` (full tree, includes
  `requiredPermission` so SPA can hide; server-side filtering is a later step
  once Authorization is wired), `GET /_ui/modules` (manifests).
- No hardcoded sidebar: host shell renders `/_ui/menu`.

## 3. CLI spec (later step)

`module.json` manifest per UI package (same shape as `ModuleManifest`).
Commands: `modulus ui list/search/add <Id>/remove <Id>/update <Id>/info <Id>` →
resolve deps → `dotnet add package Cobytelabs.Modulus.UI.<Id>` → append
`services.AddUiModule<<Id>UiModule>()` + `app.MapModulusUiMenu()` wiring.
`add-module`/`generate-crud` stay as the source-gen path; add `--with-ui` to
scaffold a companion Razor page + nav contribution.

## 4. Milestones

- [x] M1: UI.Core (navigation, manifest, menu endpoints, tests).
- [ ] M2: Phase 0 backend contracts (settings/audit/notifications/localization).
- [ ] M3: UI.Identity + UI.Permissions + UI.Tenancy RCLs.
- [ ] M4: CLI manifest + ui commands.
- [ ] M5: One vertical slice (Catalog API + UI, HTML + JSON, menu-gated).
