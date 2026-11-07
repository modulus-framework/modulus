---
title: "Modulus UI overview"
description: "Modulus UI is a set of admin UI components for Blazor, ASP.NET Core MVC, Razor Pages and Liquid templates, with one shell, theme and menu across all of them."
sidebar_position: 1
---

Modulus UI helps you build admin panels and business apps from ready-made components. Pick Blazor, ASP.NET Core MVC, Razor Pages or Liquid templates. The shell, theme, menu and components look and behave the same in each.

It is built on [Ant Design Blazor](https://antblazor.com/) for Blazor, and on Ant Design markup for MVC, so one stylesheet styles both flavours.

`.NET 10` · `Ant Design` · `Blazor` · `MVC` · `Razor Pages` · `Liquid`

## What you get

- **A complete shell.** A header with search, notifications and an account menu; a horizontal navbar or a vertical sidebar; light and dark mode; right-to-left; responsive down to phone width.
- **Every Ant Design Blazor component**, plus Modulus components for what it lacks: charts, business fields (money, phone, lookup), QR codes, a colour picker and a product tour.
- **Pro components** for real business screens: a data table with server paging, sorting, filters and batch actions; an editable grid; a nested grid; a pivot grid; a kanban board; and more.
- **One menu definition** for every flavour, filtered by policy.
- **Plugins** such as a rich-text editor, maps, a PDF viewer, a spreadsheet, uploads and a code editor, loaded only on the pages that use them.
- **Production defaults**: compression, security headers, a Content-Security-Policy with a nonce, a health endpoint, OpenTelemetry, localization and themes saved per user.

## Choose a flavour

| Flavour | Choose it when | Main package | Start with |
| --- | --- | --- | --- |
| **Blazor** (interactive server) | You want components and state in C#, with live interaction and no hand-written JavaScript. | `Synvro.Modulus.Blazor` | [Tutorial: Blazor](./tutorial-blazor.md) |
| **ASP.NET Core MVC** | You want server-rendered pages with tag helpers, and small live updates with htmx. | `Synvro.Modulus.AspNetCore.Mvc` | [Tutorial: MVC](./tutorial-mvc.md) |
| **Razor Pages** | You prefer one page per URL. Same tag helpers and layout as MVC. | `Synvro.Modulus.AspNetCore.Mvc` | [Tutorial: MVC](./tutorial-mvc.md#razor-pages-instead-of-mvc) |
| **Liquid** templates | You render pages or documents (invoices, letters) from templates. | `Synvro.Modulus.AspNetCore.Fluid` | [Liquid templates](./fluid.md) |

## See it running

The repository has three complete admin apps with the same pages and URLs, plus a Liquid sample. Each one takes a few seconds to start:

| Sample | Command | Address |
| --- | --- | --- |
| Blazor admin | `dotnet run --project samples/Modulus.AdminTemplate.Blazor` | http://localhost:5100 |
| MVC admin | `dotnet run --project samples/Modulus.AdminTemplate.Mvc` | http://localhost:5101 |
| Razor Pages admin | `dotnet run --project samples/Modulus.AdminTemplate.RazorPages` | http://localhost:5102 |
| Liquid invoices | `dotnet run --project samples/Modulus.Samples.Fluid` | http://localhost:5103 |

Run them in the Development environment. Sign in with any sample user's email and the password `modulus`.

![The Blazor sample's analytics dashboard](/img/ui/blazor-dashboard.png)

![The Razor Pages sample's kanban board](/img/ui/razorpages-kanban.png)

## How these docs are organised

- **[Getting started](./getting-started.md)** installs the packages and shows a first page in each flavour.
- **The two tutorials** build a complete app, one step at a time, and end with a list of what to try next.
- **[Concepts](./concepts.md)** explains how the shell, menu, services and htmx fit together, and what is known to be limited.
- **Reference** covers the [components](./components.md), the [Pro components](./pro.md), [forms](./forms.md), [plugins](./plugins.md), [theming](./theming.md), [production](./production.md), [Liquid](./fluid.md) and the [CLI](./cli.md).
