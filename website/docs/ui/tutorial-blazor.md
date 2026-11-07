---
title: "Build an app with Blazor"
description: "Step-by-step: build a working admin app with Modulus UI and Blazor, from an empty project to a customer list with search, sorting, batch delete and a validated form."
sidebar_position: 3
---

You will build **Contoso Admin**: a dashboard, and a customer list with search, sorting, row selection, batch delete and an add/edit drawer that validates its fields. It takes about 20 minutes. Every file is shown in full, and the finished project is in [examples/blazor/ContosoAdmin](https://github.com/modulus-framework/modulus-ui/tree/main/docs/examples/blazor/ContosoAdmin).

:::note[Before you start]

You need the .NET 10 SDK and the Modulus packages (see [Getting started](./getting-started.md)). The steps use the packages as `2.0.0`.

:::

## What you will use

| Piece | Where it comes from | Used for |
| --- | --- | --- |
| `AddModulusBlazor` | Synvro.Modulus.Blazor | Registers the theme, the shell and the menu pipeline |
| `AppShell` | Modulus.Blazor.Shell | The header, sidebar and page frame around your pages |
| `IMenuContributor` | Modulus.Ui.Abstractions.Navigation | Adds items to the menu |
| `PageHeading` | Modulus.Blazor.Shell | Pretitle, title, subtitle and action buttons |
| `StatisticCard` | Modulus.Blazor.Components.Pro | The numbers on the dashboard |
| `ProTable` | Modulus.Blazor.Components.Pro | Paged, sortable table with selection, batch actions and a toolbar |
| `DrawerForm` | Modulus.Blazor.Components.Pro | A side drawer with a validated form and async save |
| `Button`, `Input`, `FormItem`, `Switch`, `InputNumber` | Ant Design Blazor | Standard controls, with the Modulus theme applied |

## Step by step

### Step 1: Create the project

Create a Blazor Web App with interactive server rendering. The `--empty` flag leaves out the template's demo pages, and `--all-interactive` makes every page interactive.

````bash
dotnet new blazor -n ContosoAdmin -o ContosoAdmin --interactivity Server --empty --all-interactive
cd ContosoAdmin
````

### Step 2: Add the Modulus packages

Add the three packages every Blazor Modulus app uses. `Synvro.Modulus.Ui.Abstractions` comes with them as a dependency.

````bash
dotnet add package Synvro.Modulus.Blazor --version 2.0.0
dotnet add package Synvro.Modulus.Ui.Assets --version 2.0.0
dotnet add package Synvro.Modulus.AspNetCore.Hosting --version 2.0.0
````

:::tip[Packages not on nuget.org yet?]

Add the local feed to the command: `--source "path/to/modulus-ui/nupkg"`. Pack your own copy with `dotnet pack Modulus.slnx -c Release -o artifacts/packages`.

:::

The project file should now list the three packages:

````xml title="ContosoAdmin.csproj"
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Synvro.Modulus.AspNetCore.Hosting" Version="2.0.0" />
    <PackageReference Include="Synvro.Modulus.Blazor" Version="2.0.0" />
    <PackageReference Include="Synvro.Modulus.Ui.Assets" Version="2.0.0" />
  </ItemGroup>

</Project>
````

### Step 3: Register Modulus in `Program.cs`

Replace the template's `Program.cs`. Four things happen here:

- `AddModulusBlazor` registers Ant Design Blazor, the theme (the `Title` and `PrimaryColor` you set) and the shell. The theme values can also come from `appsettings.json`; see [Theming](./theming.md).
- `AddTransient<IMenuContributor, ...>` adds your menu (step 8).
- `AddSingleton<CustomerStore>` registers the data the pages read (step 9).
- `AddModulusHardening` turns on compression, security headers, a Content-Security-Policy and `/health`. See [Hosting and production](./production.md).

````csharp title="Program.cs"
using ContosoAdmin.Components;
using ContosoAdmin.Data;
using ContosoAdmin.Menu;
using Modulus.AspNetCore.Hosting;
using Modulus.Blazor;
using Modulus.Ui.Abstractions.Navigation;

var builder = WebApplication.CreateBuilder(args);

// Blazor Web App with interactive server rendering.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Modulus UI: Ant Design Blazor, the shell, the theme and the menu pipeline.
builder.Services.AddModulusBlazor(theme =>
{
    theme.Title = "Contoso Admin";
    theme.PrimaryColor = "#1677ff";
});

// Your menu: every IMenuContributor adds items to the sidebar and header.
builder.Services.AddTransient<IMenuContributor, AppMenuContributor>();

// Your data. A singleton in-memory store is enough for this tutorial.
builder.Services.AddSingleton<CustomerStore>();

// Brotli/gzip, security headers, a Content-Security-Policy and the /health endpoint.
builder.Services.AddModulusHardening();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseModulusHardening();
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapModulusHealth().AllowAnonymous();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
````

### Step 4: The page shell: `Components/App.razor`

This is the HTML document. Three Modulus pieces go in the `<head>` and one before the closing `</body>`:

- `<ModulusHead>` links the Modulus and Ant Design styles. Passing `Cookie` lets the server render the saved light or dark theme on the first paint, with no flash.
- `<ImportMap />` and `<ResourcePreloader />` are the Blazor template's own tags, kept as they are.
- `ant-design-blazor.js` must load before `blazor.web.js`.

````markup title="Components/App.razor"
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <ResourcePreloader />
    <ModulusHead Cookie="@(name => HttpContext.Request.Cookies[name])" />
    <ImportMap />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>

<body>
    <Routes @rendermode="InteractiveServer" />
    <ReconnectModal />
    <script src="_content/AntDesign/js/ant-design-blazor.js"></script>
    <script src="@Assets["_framework/blazor.web.js"]"></script>
</body>

</html>

@code {
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
}
````

### Step 5: The router: `Components/Routes.razor`

The template's router, plus `<AntContainer />`. It hosts Ant Design's modal, message and notification services, which the `DrawerForm` and `Message` service in step 11 use.

````markup title="Components/Routes.razor"
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>

@* Host for Ant Design's modal, message and notification services. *@
<AntContainer />
````

### Step 6: The layout: `Components/Layout/MainLayout.razor`

Wrap the body in `<AppShell>`. Every page then gets the Modulus header, the sidebar built from your menu, the theme settings and the footer. You write the page content and nothing else.

````markup title="Components/Layout/MainLayout.razor"
@inherits LayoutComponentBase

<AppShell>@Body</AppShell>

<div id="blazor-error-ui" data-nosnippet>
    An unhandled error has occurred.
    <a href="." class="reload">Reload</a>
    <span class="dismiss">🗙</span>
</div>
````

### Step 7: Shared usings: `Components/_Imports.razor`

Every component below `Components` gets these namespaces. `AntDesign` is Ant Design Blazor, and `Modulus.Blazor.Components.Pro` holds `ProTable`, `DrawerForm` and `StatisticCard`.

````markup title="Components/_Imports.razor"
@using System.ComponentModel.DataAnnotations
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using AntDesign
@using AntDesign.ProLayout
@using Modulus.Blazor
@using Modulus.Blazor.Shell
@using Modulus.Blazor.Theming
@using Modulus.Blazor.Components.Pro
@using Modulus.Ui.Abstractions
@using ContosoAdmin.Data
@using ContosoAdmin.Components.Layout
````

:::warning[Watch out for `Microsoft.AspNetCore.Components.Forms`]

Do not add it here. Its `InputNumber` clashes with Ant Design's `InputNumber`, and the build fails with an ambiguous-tag error.

:::

### Step 8: Add a menu: `Menu/AppMenuContributor.cs`

A menu contributor adds `MenuItemDefinition` entries. The `Href` is relative to the site root, with no leading slash, so `""` is the home page. `Icon` is an Ant Design icon name. `Order` sorts the items; `RequiredPolicy` hides an item from users who lack that authorization policy.

````csharp title="Menu/AppMenuContributor.cs"
using Modulus.Ui.Abstractions.Navigation;

namespace ContosoAdmin.Menu;

/// <summary>Adds the app's pages to the menu. Order decides the position; RequiredPolicy hides an item from users who lack it.</summary>
public sealed class AppMenuContributor : IMenuContributor
{
    public Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        // Href is relative to the site root, without a leading slash. "" is the home page.
        context.Add(new MenuItemDefinition("dashboard", "Dashboard", "", "dashboard") { Order = 0, ExactMatch = true })
               .Add(new MenuItemDefinition("customers", "Customers", "customers", "team") { Order = 10 });

        return Task.CompletedTask;
    }
}
````

To group pages, add children with `AddChild`; the shell folds nested groups for you.

### Step 9: Data: the model, the form input and a store

`Customer` is a row. `CustomerInput` is what the drawer edits, and its `DataAnnotations` attributes are the validation rules: the drawer shows the messages and blocks the save. `CustomerStore` holds the rows in memory. Its `Query` method is the one that serves `ProTable`: it filters, sorts the columns the table asks for, and returns one page with the total.

````csharp title="Data/Customers.cs"
using System.ComponentModel.DataAnnotations;
using Modulus.Ui.Abstractions;

namespace ContosoAdmin.Data;

/// <summary>A row in the customer table.</summary>
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Company { get; set; } = "";
    public string Email { get; set; } = "";
    public string City { get; set; } = "";
    public decimal Revenue { get; set; }
    public bool Active { get; set; }
}

/// <summary>The form's fields, with the validation rules the drawer enforces.</summary>
public sealed class CustomerInput
{
    [Required(ErrorMessage = "Enter the contact's name")]
    [StringLength(80)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Enter a company")]
    [StringLength(80)]
    public string Company { get; set; } = "";

    [Required(ErrorMessage = "Enter an email address")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter a city")]
    public string City { get; set; } = "";

    [Range(0, 1_000_000_000, ErrorMessage = "Revenue cannot be negative")]
    public decimal Revenue { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>In-memory data. Replace it with your database context; keep the method shapes.</summary>
public sealed class CustomerStore
{
    private readonly List<Customer> _customers =
    [
        new() { Id = 1, Name = "Olivia Bennett", Company = "Northwind", Email = "olivia@northwind.com", City = "London", Revenue = 48200, Active = true },
        new() { Id = 2, Name = "Liam Okafor", Company = "Fabrikam", Email = "liam@fabrikam.com", City = "Lagos", Revenue = 12750, Active = true },
        new() { Id = 3, Name = "Mei Tanaka", Company = "Contoso", Email = "mei@contoso.jp", City = "Tokyo", Revenue = 91300, Active = false },
        new() { Id = 4, Name = "Sofia Rossi", Company = "Litware", Email = "sofia@litware.it", City = "Milan", Revenue = 33040, Active = true },
    ];

    private int _nextId = 5;

    public IReadOnlyList<Customer> All => _customers;

    /// <summary>Filters, sorts and pages for a ProTable request. Sorting keys are the column names the table sends.</summary>
    public ProTableResponse<Customer> Query(ProTableRequest request, string? keyword)
    {
        IEnumerable<Customer> rows = _customers;

        if (!string.IsNullOrWhiteSpace(keyword))
            rows = rows.Where(c => c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                || c.Company.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        return rows.SortBy(request, new Dictionary<string, Func<Customer, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = c => c.Name,
                ["company"] = c => c.Company,
                ["revenue"] = c => c.Revenue,
            })
            .ToPage(request);
    }

    public Customer? Find(int id) => _customers.FirstOrDefault(c => c.Id == id);

    public void Add(CustomerInput input) =>
        _customers.Add(new Customer { Id = _nextId++, Name = input.Name, Company = input.Company, Email = input.Email,
                                      City = input.City, Revenue = input.Revenue, Active = input.Active });

    public void Update(int id, CustomerInput input)
    {
        if (Find(id) is not { } c) return;
        c.Name = input.Name;
        c.Company = input.Company;
        c.Email = input.Email;
        c.City = input.City;
        c.Revenue = input.Revenue;
        c.Active = input.Active;
    }

    public void Delete(IEnumerable<int> ids) => _customers.RemoveAll(c => ids.Contains(c.Id));
}
````

:::tip[Going to a database?]

Keep the method shapes and replace the bodies. `Query` can return `TableQuery<T>` results over an `IQueryable`, so the database does the paging. See [Large data](./pro.md#large-data).

:::

### Step 10: The dashboard: `Components/Pages/Home.razor`

This file replaces the template's `Home.razor` and takes the `/` route. Three `StatisticCard`s sit in a 3-column Ant Design `Row`, and each column stacks on small screens.

````markup title="Components/Pages/Home.razor"
@page "/"
@inject CustomerStore Store

<PageTitle>Dashboard · Contoso Admin</PageTitle>

<PageHeading Pretitle="Overview" Title="Dashboard" Subtitle="Customers and revenue at a glance." />

<Row Gutter="16">
    <AntDesign.Col Xs="24" Md="8">
        <StatisticCard Title="Customers" Value="@Store.All.Count.ToString("N0")" />
    </AntDesign.Col>
    <AntDesign.Col Xs="24" Md="8">
        <StatisticCard Title="Active" Value="@Store.All.Count(c => c.Active).ToString("N0")" />
    </AntDesign.Col>
    <AntDesign.Col Xs="24" Md="8">
        <StatisticCard Title="Revenue" Value="@Store.All.Sum(c => c.Revenue).ToString("C0")" />
    </AntDesign.Col>
</Row>
````

### Step 11: The customer list: `Components/Pages/Customers.razor`

This is the main page. Read it in four parts:

- **Header.** `PageHeading` with an *Add customer* button in its `Actions`.
- **Table.** `ProTable` calls `LoadAsync` on load, on every sort, page change and search. It returns one page from the store. `Selectable` adds row checkboxes, `BatchActions` appears when rows are selected, and `Columns` defines each column. Set `Sortable` on a column and the header sorts it.
- **Search.** The `Input` in `ToolBar` reloads the table on Enter or clear. `FiltersActive` and `OnClearFilters` wire up the table's own Clear button.
- **Drawer.** `DrawerForm` binds to `_drawerOpen`. Its `OnFinish` runs when the form validates. Returning `true` closes the drawer, and the table reloads.

````markup title="Components/Pages/Customers.razor"
@page "/customers"
@inject CustomerStore Store
@inject IMessageService Message

<PageTitle>Customers · Contoso Admin</PageTitle>

<PageHeading Pretitle="Sales" Title="Customers" Subtitle="Everyone who buys from us.">
    <Actions>
        <Button Type="@ButtonType.Primary" Icon="plus" OnClick="OpenCreate">Add customer</Button>
    </Actions>
</PageHeading>

<ProTable TItem="Customer" @ref="_table" HeaderTitle="All customers" RowKey="c => c.Id" Request="LoadAsync"
          Selectable FiltersActive="!string.IsNullOrWhiteSpace(_keyword)" OnClearFilters="ClearSearch">
    <ToolBar>
        <Input @bind-Value="_keyword" Placeholder="Search name or company" AllowClear Style="width: 260px"
               OnPressEnter="SearchAsync" OnClear="SearchAsync" />
    </ToolBar>

    <BatchActions Context="rows">
        <Popconfirm Title="@($"Delete {rows.Count} customers?")" OkText="Delete" OnConfirm="() => DeleteAsync(rows.Select(r => r.Id))">
            <Button Danger Size="@ButtonSize.Small">Delete selected</Button>
        </Popconfirm>
    </BatchActions>

    <Columns Context="row">
        <PropertyColumn Property="c => c.Name" Title="Name" Sortable />
        <PropertyColumn Property="c => c.Company" Title="Company" Sortable />
        <PropertyColumn Property="c => c.City" Title="City" />
        <PropertyColumn Property="c => c.Revenue" Title="Revenue" Sortable Align="ColumnAlign.Right">
            <span>@row.Revenue.ToString("C0")</span>
        </PropertyColumn>
        <ActionColumn Title="Actions" Width="140">
            <Button Type="@ButtonType.Link" OnClick="() => OpenEdit(row)">Edit</Button>
            <Popconfirm Title="@($"Delete {row.Name}?")" OkText="Delete" OnConfirm="() => DeleteAsync([row.Id])">
                <Button Type="@ButtonType.Link" Danger>Delete</Button>
            </Popconfirm>
        </ActionColumn>
    </Columns>
</ProTable>

<DrawerForm TModel="CustomerInput" Title="@(_editingId is null ? "Add customer" : "Edit customer")"
            @bind-Visible="_drawerOpen" Model="_input" OnFinish="SaveAsync"
            SubmitText="@(_editingId is null ? "Add customer" : "Save changes")">
    <FormItem Label="Contact name"><Input @bind-Value="context.Name" Placeholder="Olivia Bennett" /></FormItem>
    <FormItem Label="Company"><Input @bind-Value="context.Company" Placeholder="Northwind" /></FormItem>
    <FormItem Label="Email"><Input @bind-Value="context.Email" Placeholder="olivia@northwind.com" /></FormItem>
    <FormItem Label="City"><Input @bind-Value="context.City" Placeholder="London" /></FormItem>
    <FormItem Label="Revenue"><InputNumber @bind-Value="context.Revenue" Min="0" Style="width: 100%" /></FormItem>
    <FormItem Label="Active"><Switch @bind-Value="context.Active" /></FormItem>
</DrawerForm>

@code {
    private ProTable<Customer>? _table;
    private string? _keyword;
    private bool _drawerOpen;
    private int? _editingId;
    private CustomerInput _input = new();

    // ProTable calls this for every page, sort and search. Return the rows for the requested page.
    private Task<ProTableResponse<Customer>> LoadAsync(ProTableRequest request) =>
        Task.FromResult(Store.Query(request, _keyword));

    private Task SearchAsync() => _table!.ReloadAsync(resetPage: true);

    private Task ClearSearch()
    {
        _keyword = null;
        return _table!.ReloadAsync(resetPage: true);
    }

    private void OpenCreate()
    {
        _editingId = null;
        _input = new CustomerInput();
        _drawerOpen = true;
    }

    private void OpenEdit(Customer customer)
    {
        _editingId = customer.Id;
        _input = new CustomerInput
        {
            Name = customer.Name, Company = customer.Company, Email = customer.Email,
            City = customer.City, Revenue = customer.Revenue, Active = customer.Active,
        };
        _drawerOpen = true;
    }

    // Returning true closes the drawer; the submit button shows a spinner while this runs.
    private async Task<bool> SaveAsync(CustomerInput input)
    {
        if (_editingId is int id) Store.Update(id, input);
        else Store.Add(input);

        await Message.SuccessAsync(_editingId is null ? $"Added {input.Name}" : $"Saved {input.Name}");
        await _table!.ReloadAsync();
        return true;
    }

    private async Task DeleteAsync(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        Store.Delete(list);
        await Message.SuccessAsync(list.Count == 1 ? "Customer deleted" : $"{list.Count} customers deleted");
        await _table!.ReloadAsync();
    }
}
````

Two details are worth knowing. `PageHeading` takes its page title from `Title`, and the sidebar item stays highlighted because its `Href` matches the route. The `Message` service shows the toast at the top of the screen.

### Step 12: Run it

````bash
dotnet run
````

Open the address the terminal prints (for example `https://localhost:7xxx`) and check:

- The sidebar shows *Dashboard* and *Customers*; the current page is highlighted.
- The dashboard shows three cards: 4 customers, 3 active, and the revenue total.
- On *Customers*, click a column header to sort, type *northwind* and press Enter, and select rows to see *Delete selected*.
- *Add customer* opens the drawer. Submit it empty to see the validation messages; fill it in to save and see the toast.

:::note[No sign-in yet.]

This tutorial leaves out authentication. Before you deploy, add it with `AddAuthentication` and an authorization policy, then set `RequiredPolicy` on menu items. See [Hosting and production](./production.md).

:::

## What to change next

- **Replace the store.** Keep the `Query`, `Add`, `Update` and `Delete` shapes and back them with EF Core or your API. For large tables, return a `TableQuery` (see [Pro components](./pro.md)).
- **Add a page.** Create `Components/Pages/Invoices.razor` with `@page "/invoices"`, add a menu item in `AppMenuContributor`, and you are done. The shell picks it up.
- **Add an editable grid.** When users need to type into many rows at once, use `EditableGrid` instead of a drawer. See [Pro components](./pro.md).
- **Brand the app.** Change `Title`, `PrimaryColor` and `BorderRadius`, or bind them from configuration. See [Theming](./theming.md).
- **Start from the admin pages.** `AddModulusAdminUi` and the CLI's `ui add-admin` give you users, roles, tenants and settings pages to edit. See [CLI and the admin starter](./cli.md).

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| Page is unstyled, or the shell is missing | Check `<ModulusHead />` in `App.razor`, and that `Routes` uses `MainLayout` with `<AppShell>`. |
| Clicking a button does nothing | The page is not interactive. Check `--all-interactive` or add `@rendermode InteractiveServer` to the page, and that `ant-design-blazor.js` loads. |
| Drawer or toast never appears | Add `<AntContainer />` to `Routes.razor`. |
| Build error: ambiguous `InputNumber` | Remove `Microsoft.AspNetCore.Components.Forms` from `_Imports.razor`. |
| Menu item missing | The contributor must be registered with `AddTransient<IMenuContributor, ...>`. A `RequiredPolicy` hides items from users who lack it. |
