---
sidebar_position: 7
---

# The `ui` Commands

The `modulus ui` commands add pages, components and themes to an app that has a UI. They write files into the app, using the templates in the `Synvro.Modulus.Ui.Templates` package, for the app's engine.

## Engine and location

Every command accepts two options:

| Option | Meaning |
|---|---|
| `--engine` | `mvc`, `razor-pages` or `blazor`. The default is the engine recorded in `.modulus.json`, which `modulus app` writes. |
| `-o, --output` | The app's root folder. The default is the current directory. |

Commands that build a page for an entity also take `--module`, for when the entity name exists in more than one module.

```bash
modulus ui add-admin --engine blazor
```

## Command reference

| Command | What it adds |
|---|---|
| [`ui theme`](#themes) | Named themes: create, set, list, export |
| [`ui add-component`](#ui-add-component) | Reusable components (alert, card, data-table, ...) |
| [`ui add-admin`](#ui-add-admin) | The admin starter: dashboards, users, roles, tenants, notifications, audit, settings, profile |
| [`ui add-auth`](#ui-add-auth) | Forgot-password and reset-password pages |
| [`ui add-2fa`](#ui-add-2fa) | A two-factor page |
| [`ui add-session-manager`](#ui-add-session-manager) | A page listing the signed-in user's sessions |
| [`ui add-chart`](#ui-add-chart) | A chart page with sample data |
| [`ui add-i18n`](#ui-add-i18n) | Languages and request localization |
| [`ui add-assistant`](#ui-add-assistant) | The embedded AI assistant (Razor Pages and MVC) |
| [`ui create-dashboard`](#ui-create-dashboard) | A dashboard page: overview, analytics, reports or audit |
| [`ui create-crud`](#ui-create-crud) | List, create, edit and delete pages for an entity |
| [`ui create-form-from-entity`](#ui-create-form-from-entity) | A create and edit form from an entity's properties |
| [`ui create-permission-matrix`](#ui-create-permission-matrix) | A role and permission grid for an entity |
| [`ui audit`](#ui-audit) | An accessibility scan of the pages |

Run `modulus ui <command> --help` for the full option list of your version.

## Themes

### `ui theme create`

Creates a theme in the app's `Themes` folder from a base and a brand colour.

```bash
modulus ui theme create corporate --colors blue
modulus ui theme create mono --base minimal --colors "#0b7285"
```

| Option | Meaning |
|---|---|
| `--base` | `tabler` (the default) or `minimal` |
| `--colors` | A brand colour: `#rrggbb` or a preset (`blue`, `indigo`, `purple`, `pink`, `red`, `orange`, `green`, `teal`) |

### `ui theme set`

Makes a theme the app's active theme. It writes `Modulus:Theme` in `appsettings.json`.

```bash
modulus ui theme set corporate
```

### `ui theme list`

Lists the app's themes and marks the active one.

### `ui theme export`

Writes a theme as CSS variables, SCSS variables, a Tailwind config or JSON.

```bash
modulus ui theme export corporate --format tailwind
```

| Option | Meaning |
|---|---|
| `--format` | `css`, `scss`, `tailwind` or `json` |
| `--out-file` | The file to write. The default is `<name>.<format>` in the current directory. |

## Components and pages

### `ui add-component`

Adds reusable components to the app.

```bash
modulus ui add-component --list
modulus ui add-component alert card data-table
modulus ui add-component --all
```

Name the components to add, or pass `--all`. Use `--list` to see them all.

### `ui add-admin`

Scaffolds the admin starter and its menu: a dashboard, users, roles, tenants, notifications, notification preferences, the audit log, settings, a health page, a profile, and the analytics and reports pages.

```bash
modulus ui add-admin
modulus ui add-admin --pages dashboard,users,roles
modulus ui add-admin --engine blazor --folder Backoffice
```

| Option | Meaning |
|---|---|
| `--pages` | Pages to add, comma-separated. The default is all of them. Names: `dashboard`, `analytics`, `reports`, `users`, `roles`, `tenants`, `settings`, `audit`, `health`, `profile`, `notifications`, `notification-preferences`. |
| `--folder` | The folder (`Views`, `Pages` or `Components`) the pages go in |
| `--no-menu` | Do not add the menu registration to `Program.cs` |

The command adds `AddModulusAdminUi()` to `Program.cs` unless `--no-menu` is set. Admin pages are open to anyone who can reach the host when the app has no sign-in, so the command warns about that.

### `ui add-auth`

Adds the forgot-password and reset-password pages. They call the identity module's `/account` endpoints.

```bash
modulus ui add-auth --engine blazor
```

### `ui add-2fa`

Adds a two-factor page: set up an authenticator app, show recovery codes, and turn two-factor sign-in off.

### `ui add-session-manager`

Adds a page that lists the signed-in user's sessions, with revoke for one session or all of them, and the recent sign-ins.

### `ui add-chart`

Adds a chart page with sample data.

```bash
modulus ui add-chart --type donut
modulus ui add-chart --type line --engine blazor
```

`--type` is `line`, `column`, `donut` or `heatmap`.

### `ui add-i18n`

Sets the app's languages (`Modulus:Theme:Cultures`) and wires request localization. With two or more languages, a picker appears in the settings panel.

```bash
modulus ui add-i18n --languages en,es,fr
```

The first language is the default. Use culture names such as `fr-FR` when you need a region.

### `ui add-assistant`

Hosts the AI platform's embedded assistant: the `/ai/session` endpoint, the `ai:use` permission, the `Ai:Host` settings and a layout partial. It supports Razor Pages and MVC.

## Data pages

### `ui create-dashboard`

Scaffolds a dashboard page for the app's engine. The `<template>` is one of `overview`, `analytics`, `reports` or `audit`.

```bash
modulus ui create-dashboard analytics
modulus ui create-dashboard reports --module Admin --engine blazor
```

For MVC the command also writes a controller at the dashboard's route, which passes the figures from `IMetricsSource` or `IAuditTrail` to the view, so the page renders with data from the start.

### `ui create-crud`

Creates list, create, edit and delete pages for an entity, over its API through a typed client. Create the API first with `modulus generate-crud`.

```bash
modulus generate-crud Product --module Catalog
modulus ui create-crud Product --module Catalog --engine blazor
```

| Engine | Files |
|---|---|
| `mvc` | `Controllers/<Module>/<Entities>Controller.cs`, `Views/<Entities>/Index.cshtml`, `Create.cshtml` and `Edit.cshtml` (both with `_Form.cshtml`) |
| `razor-pages` | `Pages/<Module>/<Entities>/`: `Index.cshtml` (the list, with `_List.cshtml`), `Create.cshtml` and `Edit.cshtml` (both with `_Form.cshtml`), each page with its model, and `<Entity>ViewModels.cs` |
| `blazor` | `Components/Pages/<Module>/<Entities>/`: `Index.razor`, `Create.razor` and `Edit.razor` (both with the `<Entity>Editor.razor` form component) |

Every engine also gets the typed client `Services/ApiClients/<Entity>ApiClient.cs`, registered in `Services/ApiClientExtensions.cs`,
and the menu entry `Components/Navigation/Menu/<Entities>MenuContributor.cs`. The Web project finds menu contributors by scan, so
nothing is added to `Program.cs`.

The entity's API must exist, because the pages call it through the client.

### `ui create-form-from-entity`

Creates a create and edit form whose fields come from the entity's properties.

```bash
modulus ui create-form-from-entity Order --module Orders --engine blazor
```

The command adds a reference to the module's Application project, so the form can use the module's DTOs. For Blazor, create the entity's client first with `ui create-crud`. The command stops with a message if the client is missing.

### `ui create-permission-matrix`

Scaffolds a role and permission grid for an entity. A tick grants a permission and a cleared box revokes it. The grid uses the authorization grants endpoint.

```bash
modulus ui create-permission-matrix Order
modulus ui create-permission-matrix Order --roles Admin,Sales --permissions orders:order:manage,orders:order:export
```

| Option | Meaning |
|---|---|
| `--roles` | Roles to show as rows. The default is `Admin,Manager,User`. |
| `--permissions` | Permissions to show as columns. The default is the entity's `<module>:<route>:manage`. |

## Accessibility

### `ui audit`

Scans the app's pages for accessibility problems: missing alt text, labels, names, a missing `lang` attribute, and similar. The scan covers the app's `src` folder.

```bash
modulus ui audit
modulus ui audit --format markdown --fail-on-issues
```

| Option | Meaning |
|---|---|
| `--level` | The WCAG level to check: `A`, `AA` or `AAA`. Every rule so far is level A. |
| `--format` | A report as `json`, `markdown` or `html`, written next to `--out-file` |
| `--out-file` | The report file. The default is `accessibility-audit.<format>` in the current directory. |
| `--fail-on-issues` | Exit with code 1 when issues are found, for CI |

## Sign-in and the API host

A `webapp+api` app signs users in through the Web host's BFF session (`/bff/login`, `/bff/logout`). Three things
changed in how the generated apps wire this:

- **Return URLs.** The Web BFF client's `AllowedReturnOrigins` lists the Web origin. A `returnUrl` on the sign-in
  page is honoured only when it stays on one of those origins; anything else falls back to the home page.
- **External providers.** With an external OpenID Connect provider (`--auth keycloak`, Auth0, Okta, Entra ID and
  others) the Web host sends the browser to `/bff/login` and the provider's own login page answers. The sign-in
  page reports a service outage with its own message, distinct from a wrong password.
- **Sign-in rate limiting.** The Web host limits sign-in attempts per client address. A client that exceeds the
  limit gets `429 Too Many Requests`.

The API host is set up in files, not in one `Program.cs`. Its `Program.cs` reads as a composition of named steps
(`AddApiTokenServer`, `AddApiModules`, `AddApiCore`, `AddApiAuthentication`, `AddApiOpenApi`, `AddApiFeatures`,
`InitializeApiAsync`, `UseApiPipeline`, `MapApiEndpoints`), and each step lives in its own folder: `Composition/`,
`Configuration/`, `Security/`, `OpenApi/`, `Hosting/`, `Middleware/` and `Endpoints/`. The CLI reads the whole host
when it needs to know what is wired, so `modulus add-webhooks`, `add-realtime` and the other `add-*` commands find
their registrations wherever they were written.

The CLI detects whether the API host has authentication by reading the whole host, not only `Program.cs`, so the
`app.UseAuthentication()` call in a setup file counts. With `--auth none` no scheme is registered: endpoints need a
signed-in user by default, so the API answers `500` until you add one. `modulus app` warns about this when you
generate such an app.

## Next steps

- [Getting started](./getting-started): create an app and run it
- [Components](./components): what the generated pages use
- [Theming and localization](./theming): the theme options
