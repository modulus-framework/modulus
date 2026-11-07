---
title: "Getting started"
description: "Install Modulus UI, understand the four pieces every app has, and show a first page in Blazor, MVC or Razor Pages."
sidebar_position: 2
---

In this page you check the prerequisites, install the packages, and see the four pieces every Modulus app has. Then you show a first page in the flavour you chose. If you want a full app straight away, go to the [Blazor tutorial](./tutorial-blazor.md) or the [MVC tutorial](./tutorial-mvc.md).

## Prerequisites

- **.NET 10 SDK.** Check with `dotnet --version`; it should print `10.0.x`.
- **An editor.** Visual Studio, Rider or VS Code with the C# extension. Any of them can run the `dotnet` commands in these pages.
- **Optional: the Modulus CLI** for scaffolding. See [CLI and the admin starter](./cli.md).

## Get the packages

Modulus ships as NuGet packages, all at the same version. This documentation uses `2.0.0`.

| Package | Use it for |
| --- | --- |
| `Synvro.Modulus.Blazor` | Blazor apps: `AddModulusBlazor`, the shell, the Pro components, charts and extras. It brings Ant Design Blazor with it. |
| `Synvro.Modulus.AspNetCore.Mvc` | MVC and Razor Pages apps: `AddModulusMvc`, the tag helpers, `_ModulusLayout` and the htmx helpers. |
| `Synvro.Modulus.AspNetCore.Fluid` | Liquid templates: `ModulusLiquid`, the Modulus tags and filters. |
| `Synvro.Modulus.Ui.Assets` | The stylesheets, scripts and the vendored libraries (htmx, Alpine.js, the plugins). Every flavour needs it. |
| `Synvro.Modulus.AspNetCore.Hosting` | `AddModulusHardening`, `AddModulusObservability` and the `/health` endpoint. |
| `Synvro.Modulus.Ui.Abstractions` | Shared models: theme options, menus, `ProTableRequest`, `TableQuery`. It comes in as a dependency; you rarely add it yourself. |
| `Synvro.Modulus.Documents` | Writing PDF and Word files from HTML, and spreadsheets (xlsx). Only if you need documents. |

Install from nuget.org, or from a local copy of the packages. To build your own copy from the repository:

````bash
dotnet pack Modulus.slnx -c Release -o artifacts/packages
````

Then add that folder as a source to any command, for example `dotnet add package Synvro.Modulus.Blazor --version 2.0.0 --source ./artifacts/packages`.

## The four pieces every app has

Whichever flavour you choose, a Modulus app has the same four parts. The tutorials show each one in full.

| Piece | Blazor | MVC or Razor Pages |
| --- | --- | --- |
| **1. Services** | `builder.Services.AddModulusBlazor(...)` | `builder.Services.AddModulusMvc(...)` |
| **2. Menu** | An `IMenuContributor` class, registered with `AddTransient` | The same class |
| **3. Layout** | `<AppShell>@Body</AppShell>` in `MainLayout.razor` | `Layout = "_ModulusLayout"` in `_ViewStart.cshtml` |
| **4. Pages** | Razor components with `@page` | Views or Razor Pages, with `app-*` tag helpers |

## A first page: Blazor

Follow [steps 1 to 9 of the Blazor tutorial](./tutorial-blazor.md#step-by-step). They create the project, add the packages, and set up `Program.cs`, the layout, the menu and the data, so the project builds. Then add this page, which uses the same two components as the tutorial:

````markup title="Components/Pages/Hello.razor"
@page "/hello"

<PageHeading Pretitle="Hello" Title="My first page" Subtitle="Modulus gives this page the header, sidebar and theme.">
    <Actions>
        <Button Type="@ButtonType.Primary">Click me</Button>
    </Actions>
</PageHeading>

<p>The page is rendered inside the Modulus shell.</p>
````

Run the app and open `/hello`.

## A first page: MVC or Razor Pages

Follow [steps 1 to 10 of the MVC tutorial](./tutorial-mvc.md#step-by-step). They set up the project, the layout, the menu, the data and the controllers, so the project builds. Then add a controller and a view:

````csharp title="Controllers/HelloController.cs"
using Microsoft.AspNetCore.Mvc;

namespace ContosoMvc.Controllers;

public class HelloController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "My first page";
        return View();
    }
}
````

````markup title="Views/Hello/Index.cshtml"
<app-page-header pretitle="Hello" title="My first page" subtitle="Modulus gives this page the header, sidebar and theme.">
    <app-button type="primary">Click me</app-button>
</app-page-header>

<p>The page is rendered inside the Modulus shell.</p>
````

Run the app and open `/Hello`. For Razor Pages, put the same markup in a page with `@page`.

:::tip[Check that it worked]

The page shows the Modulus header and sidebar, and the button is blue. If it looks like plain HTML, see the troubleshooting tables in the tutorials.

:::

## Where to next

- [Build an app with Blazor](./tutorial-blazor.md) or [build an app with MVC](./tutorial-mvc.md), if you have not yet.
- [How the pieces fit together](./concepts.md), for how the menu, the shell and htmx connect.
- [Components](./components.md), to find the control you need.
