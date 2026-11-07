---
title: "Build an app with MVC"
description: "Step-by-step: the same customer admin app on ASP.NET Core MVC, with tag helpers and htmx for live updates."
sidebar_position: 4
---

You will build **Contoso Admin** on ASP.NET Core MVC: a dashboard, and a customer list with search, a table that refreshes itself after every change, a drawer for adding and editing customers with server-side validation, and delete with a confirmation. It takes about 20 minutes. The finished project is in [examples/mvc/ContosoMvc](https://github.com/modulus-framework/modulus-ui/tree/main/docs/examples/mvc/ContosoMvc).

:::note[Before you start]

You need the .NET 10 SDK and the Modulus packages (see [Getting started](./getting-started.md)). The steps use the packages as `2.0.0`. The same steps work for Razor Pages with the changes listed at the end of this page.

:::

## How the pieces talk to each other

An MVC page is rendered on the server, like any MVC page. Modulus adds three things on top:

- **The shell.** `_ModulusLayout` draws the header, the sidebar and the drawer. `Views/_ViewStart.cshtml` makes it the layout for every page.
- **Tag helpers.** `<app-table>`, `<app-button>`, `<app-form-item>` and the others draw the same Ant Design markup as the Blazor components.
- **htmx.** A button or form can load part of a page from the server. The drawer is loaded this way, and a save returns `204 No Content` with a header that tells the page to close the drawer and reload the table.

## Step by step

### Step 1: Create the project and remove the template's demo files

Create an MVC project, then delete the files this tutorial replaces. The Modulus layout ships its own styles and scripts, so the template's Bootstrap and jQuery are not needed.

````bash
dotnet new mvc -n ContosoMvc -o ContosoMvc
cd ContosoMvc
rm -rf wwwroot/lib wwwroot/css wwwroot/js
rm -f Views/Home/Privacy.cshtml Views/Home/Index.cshtml Views/Shared/Error.cshtml \
      Views/Shared/_Layout.cshtml Views/Shared/_Layout.cshtml.css Views/Shared/_ValidationScriptsPartial.cshtml \
      Models/ErrorViewModel.cs
````

In PowerShell, use `Remove-Item -Recurse -Force wwwroot/lib, wwwroot/css, wwwroot/js` and `Remove-Item` for the files. The `Program.cs`, `HomeController.cs` and `_ViewImports.cshtml` files are replaced in the next steps.

### Step 2: Add the Modulus packages

An MVC app needs the MVC package, the assets (styles and scripts) and the hosting defaults. The Abstractions package comes with the MVC package.

````bash
dotnet add package Synvro.Modulus.AspNetCore.Mvc --version 2.0.0
dotnet add package Synvro.Modulus.Ui.Assets --version 2.0.0
dotnet add package Synvro.Modulus.AspNetCore.Hosting --version 2.0.0
````

If the packages are not on nuget.org yet, add `--source "path/to/modulus-ui/nupkg"` to each command.

### Step 3: Register Modulus in `Program.cs`

Replace the template's `Program.cs`:

- `AddControllersWithViews` with `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes` makes validation follow only the explicit `[Required]` attributes, as the Blazor version does.
- `AddModulusMvc` registers the shell, the theme, the htmx helpers and the menu pipeline.
- `MapModulusHealth` and `AddModulusHardening` add the `/health` endpoint, compression, security headers and a Content-Security-Policy. See [Hosting and production](./production.md).
- `MapStaticAssets` serves the Modulus scripts and styles.

````csharp title="Program.cs"
using ContosoMvc.Data;
using ContosoMvc.Menu;
using Modulus.AspNetCore.Hosting;
using Modulus.AspNetCore.Mvc;
using Modulus.Ui.Abstractions.Navigation;

var builder = WebApplication.CreateBuilder(args);

// Validate from explicit [Required] attributes only, so the same rules apply as in the Blazor version.
builder.Services.AddControllersWithViews(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);

// Modulus UI: the shell layout, tag helpers, theme, htmx helpers and the menu pipeline.
builder.Services.AddModulusMvc(theme =>
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
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseModulusHardening();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapModulusHealth().AllowAnonymous();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
````

### Step 4: Imports and the layout: `Views/_ViewImports.cshtml` and `Views/_ViewStart.cshtml`

The two `@addTagHelper` lines turn on the tag helpers. The `_ViewStart` picks the Modulus layout for every view, so each page only has its content.

````markup title="Views/_ViewImports.cshtml"
@using ContosoMvc
@using ContosoMvc.Models
@using ContosoMvc.Data
@using Modulus.Ui.Abstractions
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Modulus.AspNetCore.Mvc
````

````markup title="Views/_ViewStart.cshtml"
@{
    // Every page uses the Modulus shell: header, sidebar, drawer and toasts.
    Layout = "_ModulusLayout";
}
````

### Step 5: Add a menu: `Menu/AppMenuContributor.cs`

The same menu contributor as in the Blazor tutorial. The shell reads it for the sidebar and the header.

````csharp title="Menu/AppMenuContributor.cs"
using Modulus.Ui.Abstractions.Navigation;

namespace ContosoMvc.Menu;

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

### Step 6: Models: `Models/Customer.cs` and `Models/DashboardStats.cs`

`Customer` is a row. `CustomerInput` is what the drawer posts. Its `[Required]` and `[EmailAddress]` attributes are checked on the server, and the messages come back into the drawer. `CustomerInput.From` copies a row into the form when you edit it.

````csharp title="Models/Customer.cs"
using System.ComponentModel.DataAnnotations;

namespace ContosoMvc.Models;

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

    public static CustomerInput From(Customer c) => new()
    {
        Name = c.Name, Company = c.Company, Email = c.Email, City = c.City, Revenue = c.Revenue, Active = c.Active,
    };
}
````

````csharp title="Models/DashboardStats.cs"
namespace ContosoMvc.Models;

/// <summary>The numbers on the dashboard.</summary>
public sealed record DashboardStats(int Total, int Active, decimal Revenue);
````

### Step 7: Data: `Data/CustomerStore.cs`

An in-memory store with the same shape a database repository would have: `Search`, `Find`, `Add`, `Update` and `Delete`. Replace the bodies with your data access.

````csharp title="Data/CustomerStore.cs"
using ContosoMvc.Models;

namespace ContosoMvc.Data;

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

    public int Count => _customers.Count;
    public int ActiveCount => _customers.Count(c => c.Active);
    public decimal Revenue => _customers.Sum(c => c.Revenue);

    /// <summary>Customers whose name or company contains the keyword (all of them when it is empty).</summary>
    public IReadOnlyList<Customer> Search(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword)
            ? _customers.OrderBy(c => c.Name).ToList()
            : _customers.Where(c => c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                 || c.Company.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Name).ToList();

    public Customer? Find(int id) => _customers.FirstOrDefault(c => c.Id == id);

    public void Add(CustomerInput input) =>
        _customers.Add(new Customer
        {
            Id = _nextId++, Name = input.Name, Company = input.Company, Email = input.Email,
            City = input.City, Revenue = input.Revenue, Active = input.Active,
        });

    public bool Update(int id, CustomerInput input)
    {
        if (Find(id) is not { } c) return false;
        c.Name = input.Name;
        c.Company = input.Company;
        c.Email = input.Email;
        c.City = input.City;
        c.Revenue = input.Revenue;
        c.Active = input.Active;
        return true;
    }

    public bool Delete(int id) => _customers.RemoveAll(c => c.Id == id) > 0;
}
````

### Step 8: Controllers

`HomeController` renders the dashboard. `CustomersController` is the interesting one. It has five actions, and each one is a piece of the htmx flow:

| Action | Called by | Returns |
| --- | --- | --- |
| `Index` | The sidebar link, the search form | The full customer page |
| `Table` | The table container, when a save or delete sends `customers:refresh` | The table rows only |
| `Form` | The *Add customer* and *Edit* buttons, through `$store.drawer.load` | The drawer form, filled in when editing |
| `Save` | The drawer form's `hx-post` | The form again if the input is invalid; otherwise `204` with headers that show a toast, close the drawer and refresh the table |
| `Delete` | The *Delete* button's `hx-post`, after the `hx-confirm` dialog | `204` with a toast and a refresh |

`Response.HxMessage`, `HxCloseDrawer` and `HxTrigger` are extension methods in `Modulus.AspNetCore.Mvc.Htmx`. They set the response headers the page reads.

````csharp title="Controllers/HomeController.cs"
using ContosoMvc.Data;
using ContosoMvc.Models;
using Microsoft.AspNetCore.Mvc;

namespace ContosoMvc.Controllers;

public class HomeController(CustomerStore store) : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Dashboard";
        return View(new DashboardStats(store.Count, store.ActiveCount, store.Revenue));
    }

    public IActionResult Error()
    {
        ViewData["Title"] = "Error";
        return View();
    }
}
````

````csharp title="Controllers/CustomersController.cs"
using ContosoMvc.Data;
using ContosoMvc.Models;
using Microsoft.AspNetCore.Mvc;
using Modulus.AspNetCore.Mvc.Htmx;
using Modulus.Ui.Abstractions;

namespace ContosoMvc.Controllers;

/// <summary>The customer list, its table fragment, and the add/edit drawer. Saves answer 204 and send a refresh event.</summary>
[Route("customers")]
public class CustomersController(CustomerStore store) : Controller
{
    // Sent to the page after a save or delete. The table listens for it and reloads itself.
    public const string RefreshEvent = "customers:refresh";

    [HttpGet("")]
    public IActionResult Index(string? keyword)
    {
        ViewData["Title"] = "Customers";
        ViewData["Keyword"] = keyword;
        return View(store.Search(keyword));
    }

    // The table on its own, used for refreshes.
    [HttpGet("table")]
    public IActionResult Table(string? keyword) => PartialView("_Table", store.Search(keyword));

    [HttpGet("form/{id:int?}")]
    public IActionResult Form(int? id)
    {
        if (id is null) return Drawer(null, new CustomerInput());
        return store.Find(id.Value) is { } customer ? Drawer(id, CustomerInput.From(customer)) : NotFound();
    }

    [HttpPost("save/{id:int?}")]
    public IActionResult Save(int? id, CustomerInput input)
    {
        // Invalid input: send the drawer back with the error messages. htmx swaps it into the drawer.
        if (!ModelState.IsValid) return Drawer(id, input);

        if (id is { } existing)
        {
            if (!store.Update(existing, input)) return NotFound();
        }
        else store.Add(input);

        Response.HxMessage(MessageType.Success, id is null ? $"Added {input.Name}" : $"Saved {input.Name}")
                .HxCloseDrawer()
                .HxTrigger(RefreshEvent);
        return NoContent();
    }

    [HttpPost("delete/{id:int}")]
    public IActionResult Delete(int id)
    {
        if (!store.Delete(id)) return NotFound();

        Response.HxMessage(MessageType.Success, "Customer deleted").HxTrigger(RefreshEvent);
        return NoContent();
    }

    private PartialViewResult Drawer(int? id, CustomerInput input)
    {
        ViewData["Id"] = id;
        return PartialView("_Form", input);
    }
}
````

### Step 9: The dashboard and the error page

Three `app-statistic-card`s sit in an `app-row` with 24-column `app-col`s. Each column takes the full width on small screens (`xs="24"`) and a third of the row from medium screens up (`md="8"`).

````markup title="Views/Home/Index.cshtml"
@model DashboardStats

<app-page-header pretitle="Overview" title="Dashboard" subtitle="Customers and revenue at a glance."></app-page-header>

<app-row gutter="16">
    <app-col xs="24" md="8">
        <app-statistic-card title="Customers" value="@Model.Total.ToString("N0")"></app-statistic-card>
    </app-col>
    <app-col xs="24" md="8">
        <app-statistic-card title="Active" value="@Model.Active.ToString("N0")"></app-statistic-card>
    </app-col>
    <app-col xs="24" md="8">
        <app-statistic-card title="Revenue" value="@Model.Revenue.ToString("C0")"></app-statistic-card>
    </app-col>
</app-row>
````

````markup title="Views/Home/Error.cshtml"
@{
    ViewData["Title"] = "Error";
}

<app-empty description="Something went wrong. Try again, or return to the dashboard."></app-empty>
<p style="text-align: center"><a href="/">Back to the dashboard</a></p>
````

### Step 10: The customer list: three views

The list is split into three files so each part can be reloaded on its own.

- **`Index.cshtml`** is the page. It has the header with the *Add customer* button, a search form, and a container that loads the table. The button opens the drawer with `$store.drawer.load('/customers/form', 'Add customer')`, which is part of the shell.
- **`_Table.cshtml`** is the table. It is drawn by the server on the first load and again whenever the `customers:refresh` event arrives. The *Delete* button uses `hx-confirm`, which the shell turns into an Ant Design confirmation dialog.
- **`_Form.cshtml`** is the drawer's form. `app-form-item` with `asp-for` draws the label, the input and the validation message. Its `hx-post` sends the save to `CustomersController.Save`.

````markup title="Views/Customers/Index.cshtml"
@model IReadOnlyList<Customer>
@{
    var keyword = ViewData["Keyword"] as string;
}

<app-page-header pretitle="Sales" title="Customers" subtitle="Everyone who buys from us.">
    <app-button type="primary" icon="plus" @@click="$store.drawer.load('/customers/form', 'Add customer')">Add customer</app-button>
</app-page-header>

<app-card title="All customers">
    <form method="get" action="/customers" style="display: flex; gap: 8px; margin-bottom: 16px;">
        <app-input name="keyword" value="@keyword" placeholder="Search name or company" style="max-width: 320px;"></app-input>
        <app-button html-type="submit">Search</app-button>
    </form>

    @* The table reloads itself whenever a save or delete sends the customers:refresh event. *@
    <div id="customer-table"
         hx-get="/customers/table?keyword=@Uri.EscapeDataString(keyword ?? "")"
         hx-trigger="customers:refresh from:body"
         hx-swap="innerHTML">
        @await Html.PartialAsync("_Table", Model)
    </div>
</app-card>
````

````markup title="Views/Customers/_Table.cshtml"
@model IReadOnlyList<Customer>

<app-table size="middle">
    <thead>
        <tr>
            <th>Name</th>
            <th>Company</th>
            <th>City</th>
            <th>Status</th>
            <th style="text-align: right">Revenue</th>
            <th></th>
        </tr>
    </thead>
    <tbody>
        @if (Model.Count == 0)
        {
            <tr>
                <td colspan="6"><app-empty description="No customers match your search."></app-empty></td>
            </tr>
        }
        @foreach (var c in Model)
        {
            <tr>
                <td>@c.Name<br /><small>@c.Email</small></td>
                <td>@c.Company</td>
                <td>@c.City</td>
                <td>@(c.Active ? "Active" : "Inactive")</td>
                <td style="text-align: right">@c.Revenue.ToString("C0")</td>
                <td>
                    <app-button type="link" @@click="$store.drawer.load('/customers/form/@c.Id', 'Edit customer')">Edit</app-button>
                    <app-button type="link" danger hx-post="/customers/delete/@c.Id" hx-confirm="Delete @c.Name?" hx-swap="none">Delete</app-button>
                </td>
            </tr>
        }
    </tbody>
</app-table>
````

````markup title="Views/Customers/_Form.cshtml"
@model CustomerInput
@{
    var id = ViewData["Id"] as int?;
}

@* Add / edit drawer. The form posts with htmx; a valid save closes the drawer, an invalid one re-renders it. *@
<form class="m-drawer-form ant-form ant-form-vertical ant-form-hide-required-mark"
      hx-post="/customers/save/@id" hx-target="#modulus-drawer-body" hx-swap="innerHTML" novalidate>
    <div class="m-drawer-form-body">
        <app-form-item asp-for="Name" label="Contact name"><app-input asp-for="Name" placeholder="Olivia Bennett" /></app-form-item>
        <app-form-item asp-for="Company" label="Company"><app-input asp-for="Company" placeholder="Northwind" /></app-form-item>
        <app-form-item asp-for="Email" label="Email"><app-input asp-for="Email" type="email" placeholder="olivia@northwind.com" /></app-form-item>
        <app-form-item asp-for="City" label="City"><app-input asp-for="City" placeholder="London" /></app-form-item>
        <app-form-item asp-for="Revenue" label="Revenue"><app-input-number asp-for="Revenue" min="0" /></app-form-item>
        <app-form-item asp-for="Active" label="Active"><app-switch asp-for="Active" /></app-form-item>
    </div>
    <div class="m-drawer-form-footer">
        <app-button @@click="$store.drawer.hide()">Cancel</app-button>
        <app-button type="primary" html-type="submit">@(id is null ? "Add customer" : "Save changes")</app-button>
    </div>
</form>
````

:::tip[Why `hx-target` on the form?]

A failed save returns the form with its messages, and htmx swaps it into `#modulus-drawer-body`, the drawer's body. A successful save returns no content, so nothing is swapped, and the drawer closes because of the `HxCloseDrawer` header.

:::

### Step 11: Run it and check the flow

````bash
dotnet run
````

Open the address the terminal prints and check:

- The sidebar shows *Dashboard* and *Customers*, and the dashboard shows three cards.
- Type *northwind* in the search box and press *Search*. The page reloads with one row.
- Click *Add customer*. Save the form empty to see the validation messages, then fill it in and save. The drawer closes, a green toast appears, and the new row is in the table without a page reload.
- Click *Delete* on a row. The confirmation appears; confirm it, and the row disappears.

You can also check the endpoints from a terminal. The table fragment is `/customers/table` and the health check is `/health`.

:::note[No sign-in yet.]

This tutorial leaves out authentication. Before you deploy, add cookie authentication, an authorization policy, and `RequiredPolicy` on menu items. See [Hosting and production](./production.md).

:::

## Razor Pages instead of MVC

The layout, the menu, the tag helpers and the htmx responses are the same. Only the project shape changes:

| MVC | Razor Pages |
| --- | --- |
| `builder.Services.AddControllersWithViews()` | `builder.Services.AddRazorPages()` |
| `app.MapControllerRoute(...)` | `app.MapRazorPages()` |
| A controller with `[HttpGet]` actions and a view for each | A `.cshtml` page with `@page`, and a `PageModel` with `OnGet` and `OnPost` handlers |
| `Views/Customers/_Table.cshtml` returned by `PartialView` | A partial view, rendered from a handler the same way (see `samples/Modulus.AdminTemplate.RazorPages`) |

The `samples/Modulus.AdminTemplate.RazorPages` project in the repository shows the full version.

## What to change next

- **Use a database.** Replace `CustomerStore` with an EF Core repository and keep the method shapes; the controller does not change.
- **Paginate large tables.** Use `app-load-more` for infinite scroll, or `app-virtual-table` for very long lists. See [Large data](./pro.md#large-data).
- **Use an editable grid** when users need to change many rows at once. See [Pro components](./pro.md).
- **Add a page.** Add a controller action and a view, then a menu item. The shell adds the page to the layout for you.
- **Start from the admin pages.** `AddModulusAdminUi` and `ui add-admin` give you users, roles, tenants and settings pages. See [CLI and the admin starter](./cli.md).

## Troubleshooting

| Symptom | Likely cause and fix |
| --- | --- |
| Page has no styles or shell | Check `Views/_ViewStart.cshtml` sets `Layout = "_ModulusLayout"`, and that `MapStaticAssets()` runs. |
| Tags such as `<app-button>` show up as plain text or unstyled | Check `@addTagHelper *, Modulus.AspNetCore.Mvc` in `Views/_ViewImports.cshtml`. |
| Save returns `400` | The antiforgery token is missing. The layout sends it in `hx-headers` on `<body>`. Do not remove it or post the form without htmx. |
| Drawer opens empty or never opens | Check the `Form` action returns a view (not a 404) and the element `#modulus-drawer-body` exists, which the layout adds. |
| Table does not refresh after save | The save must send `HxTrigger("customers:refresh")` and the table container must listen with `hx-trigger="customers:refresh from:body"`. |
