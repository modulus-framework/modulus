---
sidebar_position: 2
---

# Getting Started with the UI

This page creates a web app with the Modulus UI, runs it, and shows where the code lives. It assumes you have the prerequisites from [Prerequisites](../getting-started/prerequisites) and the `modulus` CLI installed.

## 1. Create the app

Pick the engine with `--ui-engine`: `blazor`, `mvc` or `razor-pages`. The app kind must be `webapp+api`, which creates a web project on the UI framework and an API project for it to call.

```bash
modulus app Shop --kind webapp+api --ui-engine blazor --database SQLite --auth openiddict --no-example
```

Replace `blazor` with `mvc` or `razor-pages` to get the same app in another engine. Omitted options are asked for interactively.

| Option | Meaning |
|---|---|
| `--kind webapp+api` | A web project (the UI) plus an API project. The single-project kind is retired. |
| `--ui-engine` | `blazor`, `mvc` or `razor-pages` |
| `--auth` | `openiddict` (the local token server, with sign-in pages), an external provider, or `none` |
| `--no-example` | Do not add the example `Catalog` module |

The generated solution has two projects that run side by side:

```
Shop/
└── src/
    ├── API/Shop.Api/          the API and the token server (--auth openiddict)
    └── Web/Shop.Web/          the UI host for the chosen engine
        ├── Program.cs         composition root for the UI
        ├── appsettings.json   Modulus:Theme, Api:BaseUrl, Bff clients
        ├── security/loosening-allowlist.json   anonymous endpoints the app opens on purpose
        └── (Blazor) Components/   App.razor, Routes.razor, Layout/, Pages/, Security/
        └── (MVC / Razor Pages) Pages/ or Controllers/ and Views/
```

## 2. Run it

Start the API first, then the web host. The ports are in each project's `Properties/launchSettings.json`.

```bash
dotnet run --project src/API/Shop.Api
dotnet run --project src/Web/Shop.Web
```

With `--auth openiddict`, the API creates a development administrator the first time it starts. It prints the account once in the console:

```
Development administrator created: admin@shop.local / <password> (random; shown once).
```

Copy the password then. To choose your own, set `Identity:Seed:AdminEmail` and `Identity:Seed:AdminPassword` before the first start.

Open the web host's address. With sign-in configured, you go to the sign-in page first. Sign in with the administrator account.

## 3. Add a page

Each engine has its own way to add a page. Every page shows up in the shell with the same layout.

### Blazor

A page is a Razor component with a route. The shell comes from `AppShell` in `MainLayout.razor`, so the component only has the content.

```razor
@page "/customers"
@attribute [Authorize]

<PageContainer Title="Customers">
    <ProTable TItem="Customer" HeaderTitle="Customer list" RowKey="c => c.Id" Request="LoadAsync">
        <Columns>
            <PropertyColumn Property="c => c.Name" Sortable />
            <PropertyColumn Property="c => c.Company" />
        </Columns>
    </ProTable>
</PageContainer>

@code {
    private Task<ProTableResponse<Customer>> LoadAsync(ProTableRequest request) =>
        Task.FromResult(_customers.SortBy(request, "Name", c => c.Name).ToPage(request));
}
```

Add a menu entry in an `IMenuContributor`:

```csharp
public sealed class AppMenuContributor : IMenuContributor
{
    public Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        context.Add(new MenuItemDefinition("customers", "Customers", "customers", "team") { Order = 10, RequiredPolicy = "Customers.Read" });
        return Task.CompletedTask;
    }
}
```

Register it with `builder.Services.AddTransient<IMenuContributor, AppMenuContributor>();` in `Program.cs`.

### MVC

A controller returns a view. The layout comes from `_ViewStart.cshtml` (`_ModulusLayout`).

```cshtml
<app-page-container title="Customers">
  <app-card title="Customer list">
    <app-card-extra>
      <app-button type="primary" icon="plus" hx-get="/customers/new" hx-target="#modulus-drawer-body"
                  x-on:click="$store.drawer.show('New customer')">New customer</app-button>
    </app-card-extra>
    <div id="table" hx-get="/customers/table" hx-trigger="load, customers:refresh from:body"></div>
  </app-card>
</app-page-container>
```

After a save, the controller tells the page what happened with the htmx response helpers:

```csharp
Response.HxMessage(MessageType.Success, $"Created {customer.Name}");
Response.HxCloseDrawer();
Response.HxTrigger("customers:refresh");
return NoContent();
```

### Razor Pages

Razor Pages use the same tag helpers and the same response helpers as MVC. Put the page in `Pages/` with a `@page` directive, and the `_ViewStart.cshtml` gives it the shell.

## 4. Generate the list and form from an entity

If an entity already has an API (`modulus generate-crud Product`), the `ui` commands build its pages:

```bash
modulus ui create-crud Product --module Catalog --engine blazor
modulus ui create-form-from-entity Product --module Catalog --engine blazor
```

See [The ui commands](./cli) for every command.

## 5. Check the result

- `modulus ui audit` checks the pages for accessibility problems.
- `dotnet build` builds the solution as usual, and `dotnet test` runs the generated tests.

## Next steps

- [Components](./components): the tag helpers and Blazor components
- [Data tables and grids](./data-grids): paging, sorting, filters and editing
- [Authentication](./authentication): the sign-in flows, the security guard and the loosening allowlist
- [Theming and localization](./theming): brand, layout and languages
- [Hosting defaults](./hosting): compression, security headers and the Content Security Policy
