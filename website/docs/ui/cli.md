---
title: "CLI and the admin starter"
description: "The modulus ui commands that scaffold pages, CRUD, the admin starter, auth pages, charts, languages and themes into a Modulus web app."
sidebar_position: 13
---

The `modulus ui` commands add UI to a Modulus web app: pages, CRUD screens, the admin starter, auth pages, charts and themes. They read the app's engine from `.modulus.json`, which `modulus app --ui-engine` writes, or you can pass `--engine mvc`, `--engine razor-pages` or `--engine blazor`. Fluid is not scaffolded yet.

Every command accepts `--dry-run`, `--force`, `--json`, `-v`, `-q` and `-o <app root>`. Use `--dry-run` first to see the files a command would write.

## Start a web app with UI

```bash
modulus app --kind webapp+api --ui-engine mvc     # or razor-pages
```

This builds a web project on the Modulus UI framework: `AddModulusMvc`, the `_ModulusLayout` shell, static assets, an auth frame for the sign-in pages, and a rule that every page needs a signed-in user except `/Account`. `fluid` and `none` have page templates but no app host yet, so `modulus app` refuses them for a web app.

## Commands

| Command | Adds |
| --- | --- |
| `ui add-component <names…> \| --all \| --list` | 85 components in nine groups: Inputs (auto-complete, button, cascader, checkbox, checkbox-group, color-picker, date-picker, date-range-picker, form-group, form-input, input-number, money-input, otp-input, radio-group, rate, search-input, segmented, select, slider, switch, tag-input, textarea, time-picker, transfer, tree-select, upload), Data display (avatar, badge, calendar, carousel, collapse, countdown, data-table, descriptions, image, list, popover, qr-code, stat-card, statistic, status-badge, tag, timeline, tooltip, tree), Navigation (back-top, breadcrumb, dropdown, menu, page-header, pagination, steps, tabs), Layout (card, divider, grid, space, splitter), Feedback (alert, drawer, empty-state, modal, popconfirm, progress, result, skeleton, spinner), Documents (document, pdf-viewer, spreadsheet), Editors (code-editor, rich-text-editor), Data grids (data-grid, import-wizard, pivot-grid) and Pro (file-manager, flow-editor, chat, approval-card, document-upload, gantt, kanban, query-builder, scheduler, schema-form). Run `--list` for descriptions. |
| `ui create-dashboard <overview\|analytics\|reports\|audit> [--module]` | A dashboard page. |
| `ui create-form-from-entity <Entity> [--module]` | A create and edit form. Its fields come from the public properties of the entity class. |
| `ui add-admin [--pages a,b] [--engine] [--folder Admin] [--no-menu]` | The admin dashboard starter: 14 pages (dashboard, analytics, reports, users, roles, tenants with settings and members, settings, audit log, system health, profile, notifications and preferences). For MVC it adds an `AdminController`, and a `_ViewStart.cshtml` if the app has none. It also adds `AddModulusAdminUi()` with the matching menu. `--pages` limits both the files and the menu. The pages read data through `Modulus.Ui.Abstractions.Admin` services and show sample data until you register your own. They need a signed-in user. |
| `ui add-auth` | Forgot-password and reset-password pages (`/auth/forgot-password`, `/auth/reset-password`). |
| `ui add-2fa` | The two-factor page (`/auth/two-factor`): authenticator setup, recovery codes, and turning it off. |
| `ui add-session-manager` | Sessions and recent sign-ins (`/auth/sessions`). |
| `ui add-chart --type line\|column\|donut\|heatmap\|radar\|candlestick\|sankey\|org` | A chart page (`/charts/<type>`) built on the framework's chart components, with sample data to replace. |
| `ui add-i18n --languages en,es,fr` | Sets `Modulus:Theme:Cultures` (the first is the default) and adds `app.UseModulusLocalization()`. With two or more languages the settings panel shows a language picker. |
| `ui audit [--level A\|AA\|AAA] [--format json\|markdown\|html] [--out-file f] [--fail-on-issues]` | A static accessibility scan of every `.cshtml`, `.razor` and `.html` file under `src`. It checks seven rules, all WCAG level A: image alt text, form labels, button and link names, keyboard access, page language, table headers and positive tabindex. It does not check colour contrast or focus order, and it reads the markup, not the running page. |
| `ui create-permission-matrix <Entity> [--module] [--roles Admin,Sales] [--permissions a:b:c,...]` | A role by permission matrix at `/permissions/<entity>`. It reads and changes grants through the `/authorization/grants` endpoints, so the host needs `MapModulusAuthorizationManagement`. |
| `ui create-crud <Entity> [--module] [--engine razor-pages\|blazor]` | List, create and edit, and delete pages (`/<entities>`) over the entity's API, through a typed `<Entity>ApiClient`, plus a menu entry. Needs a `webapp+api` app, with `Api:BaseUrl` set in the web project. |
| `ui add-assistant` | Hosts the AI platform's embedded assistant, disabled by default. It needs the SDK script under `wwwroot/lib/ai-sdk/`. Razor Pages and MVC. |
| `ui theme create <name> [--base tabler\|minimal] [--colors]` | `Themes/<name>/theme.json` and `variables.css`. |
| `ui theme set <name>` | Writes the theme to `Modulus:Theme` in `appsettings.json`. |
| `ui theme list` | Lists the app's themes, and marks the active one. |
| `ui theme export <name> --format css\|scss\|tailwind\|json` | The theme in another format. |

The auth pages call the `/account/*` endpoints of Modulus.Identity on the same host. MVC gets a controller beside each view; Razor Pages and Blazor keep their route in the page.

## Packages

The CLI reads the packages from three places, in this order:

1. `~/.modulus/cache`: templates unpacked from the package, unpacked again when the local `.nupkg` is newer.
2. `~/.modulus/feed`, or the folder named by `MODULUS_LOCAL_FEED`: a local feed, used until the packages are on nuget.org.
3. nuget.org, for any package that is in neither.

To fill the local feed from a copy of the repository, run `dotnet pack Modulus.slnx -c Release -o ~/.modulus/feed`. Delete the feed once the packages are published.

## Update notices

When a newer `modulus` CLI is on nuget.org, the command you run ends with a short notice and the update command. The check asks nuget.org at most once a day and never delays a command. It stays silent for `--json`, `--quiet`, redirected output and CI. To turn it off, set `MODULUS_NO_UPDATE_CHECK=1`. Run `modulus doctor` to see the installed and latest versions at any time.

## Not built yet

`add-chat`, `add-form-builder`, `add-collaboration`, `add-pwa`, `create-user-dashboard` (Identity has no endpoint that lists users) and `add-access-review`. They need template content or backend endpoints that do not exist yet. Self-service registration is not offered, because Identity supports only invitations.

## Known issues

- The pages that use identity (`add-auth`, `add-2fa`, `add-session-manager`, the permission matrix) call same-origin `/account/*` and `/authorization/*` endpoints. In a `webapp+api` app the web host forwards only the recovery routes.
- The `mvc` engine adds MVC to the web host for the landing page only: `HomeController` with `Views/Home/Index.cshtml`, plus `Views/_ViewStart.cshtml` and `Views/_ViewImports.cshtml`. The sign-in and status pages stay Razor Pages. Add your own controllers and views beside them.
- The `admin-users`, `admin-settings`, `module-home` and `system-health` templates exist, but no command scaffolds them yet.
- The `_content` assets of the UI packages are served by `dotnet run` only in the Development environment. A published app carries them in `wwwroot`.
