---
sidebar_position: 1
---

# UI Overview

Modulus UI is the admin-panel layer of the framework. It gives a web app a finished shell (header, menu, footer, theme settings), data tables and grids, forms, charts, and sign-in pages. You can use it from **Blazor**, **ASP.NET Core MVC** or **Razor Pages**, and the three render the same layout, menu and theme.

It is built on [Ant Design Blazor](https://antblazor.com) and its Pro layout, on .NET 10. Blazor uses the real Ant Design components. MVC and Razor Pages use tag helpers that write the same Ant Design markup, with [htmx](https://htmx.org) and [Alpine.js](https://alpinejs.dev) for interactivity. No build step is needed for the front end.

## What it looks like

The same shell, menu and theme in each engine. Pages are from the framework samples.

| Blazor: analytics dashboard | MVC: eCommerce dashboard, dark theme |
|---|---|
| ![Blazor analytics dashboard](/img/ui/blazor-dashboard.png) | ![MVC eCommerce dashboard in dark theme](/img/ui/mvc-ecommerce-dark.png) |

| Razor Pages: kanban board | Blazor: data table, dark theme |
|---|---|
| ![Razor Pages kanban board](/img/ui/razorpages-kanban.png) | ![Blazor data table in dark theme](/img/ui/blazor-data-table-dark.png) |

| MVC: form wizard | Razor Pages: sign-in |
|---|---|
| ![MVC form wizard](/img/ui/mvc-wizard.png) | ![Razor Pages sign-in](/img/ui/razorpages-login.png) |

## Who this is for

- **Backend developers** who need an admin screen for a module (a list, a form, a permission grid) and do not want to write HTML and CSS.
- **Front-end developers** who want Ant Design components in Blazor, or Ant Design markup in MVC and Razor Pages.
- **Architects** who want one look and one menu definition across engines, so a team can pick the engine per app.

## The three engines

Every web app created with `modulus app --kind webapp+api` has one UI engine. Choose it with `--ui-engine`.

| | Blazor (`blazor`) | MVC (`mvc`) | Razor Pages (`razor-pages`) |
|---|---|---|---|
| Rendering | Blazor Server (interactive components) | Server-rendered views and controllers | Server-rendered pages and page models |
| Components | Ant Design Blazor components, with `App*` aliases | Tag helpers (`app-button`, `app-table`, ...) | Tag helpers, the same as MVC |
| Interactivity | Blazor events and state | htmx for requests, Alpine.js for widgets | htmx for requests, Alpine.js for widgets |
| Forms | `EditForm` and the typed API client | Plain HTML forms that post normally | Plain HTML forms that post normally |
| Best when | The team writes C# components and wants rich interactivity | The team writes controllers and views | The team prefers page-per-file routing |

`fluid` appears in the framework's packages as a Liquid template engine for pages and documents (see [Liquid templates](#liquid-templates)). It is not a host kind for `modulus app`.

## The NuGet packages

Each package is named `Synvro.Modulus.<project>`. Add only the ones you use; `modulus app` adds the right set.

| Package | What it is for |
|---|---|
| `Synvro.Modulus.Ui.Abstractions` | Engine-neutral code: theme options and palettes, menu contributors, the shell renderer, the `ProTableRequest` and `TableQuery` types, form rules, chart geometry |
| `Synvro.Modulus.Ui.Assets` | The CSS and JavaScript (`modulus*.css`, `modulus-*.js`), with htmx, Alpine.js and the plugin libraries |
| `Synvro.Modulus.Blazor` | `AddModulusBlazor`, the `App*` component aliases, the shell, Pro components, charts |
| `Synvro.Modulus.AspNetCore.Mvc` | `AddModulusMvc`, the tag helpers, the `_ModulusLayout` view and the htmx response helpers |
| `Synvro.Modulus.AspNetCore.Hosting` | Hosting defaults: compression, security headers, the Content Security Policy with a nonce, `/health` |
| `Synvro.Modulus.AspNetCore.Fluid` | Liquid templates for pages and documents |
| `Synvro.Modulus.Ui.Templates` | The Scriban templates the CLI uses to generate pages, components and hosts |

## What you get

- **A shell that looks the same in every engine**: a header with page search (Ctrl+K), an app switcher, a tenant switcher, notifications and an account menu. Choose a horizontal navbar, a condensed header or a vertical sidebar. Users change the layout in a settings panel, and their choices are kept in cookies, so the first paint is already correct.
- **A menu from code**: implement `IMenuContributor` and the menu follows the same rules in every engine. Each item can require an authorization policy.
- **Data components**: a data table with server paging, sorting and filters; an editable grid; a nested grid; a pivot grid; groups and totals. See [Data tables and grids](./data-grids).
- **Forms and business fields**: money, quantity, duration, phone, masked input, lookups, a rich-text editor, a signature pad, and a form drawn from a schema. See [Components](./components).
- **Charts with no JavaScript dependency**: line, column, bar, donut, heatmap, sparkline, and business charts such as waterfall, funnel and gauge.
- **Plugins**: a WYSIWYG editor, a map, a PDF viewer, a spreadsheet, an uploader, an image cropper, a code editor, a media player and a sortable list.
- **Sign-in and admin starters**: sign-in, sign-out and password pages, plus an admin starter with users, roles, tenants, notifications, audit log and settings. See [Authentication](./authentication).
- **Theming from one options object**: brand colours, radius, density and layout, with a generated 10-step palette and dark mode. See [Theming and localization](./theming).
- **Localization**: list languages in the theme options and the settings panel gets a language picker.
- **Accessibility checks**: `modulus ui audit` scans the pages for missing labels, alt text and language attributes. See [The ui commands](./cli).

## Where to start

1. [Getting started](./getting-started): create an app with a UI and run it.
2. [Components](./components): the tag helpers and Blazor components by category.
3. [Data tables and grids](./data-grids): the list pages most apps need.
4. [Authentication](./authentication): the sign-in flows and the startup security guard.
5. [The ui commands](./cli): scaffold pages and components from the CLI.

## Liquid templates

`Synvro.Modulus.AspNetCore.Fluid` renders pages and documents from `.liquid` files, with a layout (`{{ content }}` is the page body) and the same markup as the components. Every printed value is HTML-escaped. Tags include `{% modulus_head %}` (the stylesheets and scripts), `{% icon "name" %}`, `{% barcode "value" %}`, and the filters `status_badge` and `money`. A model's properties are readable only after its type is registered with `ModulusLiquidOptions.Register<T>()`.

## Project map of the framework repository

| Project | Purpose |
|---|---|
| `src/Modulus.Ui.Abstractions` | Engine-neutral code (above) |
| `src/Modulus.Ui.Assets` | CSS, scripts, vendored htmx, Alpine.js, MapLibre, Tiptap, Univer, jsVectorMap, pdf.js |
| `src/Modulus.Blazor` | Blazor components and the shell |
| `src/Modulus.AspNetCore.Mvc` | MVC tag helpers and the layout |
| `src/Modulus.AspNetCore.Hosting` | Hosting defaults |
| `src/Modulus.AspNetCore.Fluid` | Liquid templates |
| `samples/Modulus.AdminTemplate.*` | The Blazor, MVC and Razor Pages admin templates, with the same pages and URLs |

The samples sign in with the password `modulus` for every sample user. Set `Samples:RequireSignIn` to `true` to require sign-in on every page.
