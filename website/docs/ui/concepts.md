---
title: "How the pieces fit together"
description: "How the shell, the menu, the services, the theme cookies and htmx work together in Blazor, MVC and Razor Pages, and what is known to be limited."
sidebar_position: 5
---

Every Modulus app, whichever flavour you choose, is made of the same few pieces. Knowing how they connect makes the tutorials easier to follow and helps when you change something.

## The four pieces

| Piece | Blazor | MVC or Razor Pages |
| --- | --- | --- |
| Services | `AddModulusBlazor(...)` | `AddModulusMvc(...)` |
| Menu | An `IMenuContributor`, registered with `AddTransient` | The same class |
| Layout | `<AppShell>` in `MainLayout.razor` | `_ModulusLayout`, set in `_ViewStart.cshtml` |
| Pages | Razor components with `@page` | Views or Razor Pages, using `app-*` tag helpers |

## The shell

The shell is the frame around your pages: the header, the sidebar or navbar, the theme settings and the footer. Blazor's `<AppShell>` and MVC's `_ModulusLayout` both call the same renderer (`ShellRenderer` in `Modulus.Ui.Abstractions`), so the markup, the classes and the behaviour are the same.

Header content comes from an `IShellContentProvider`. It supplies the signed-in user, notifications, the app switcher (`Apps`), the tenant switcher (`Tenants`), and extra links. You implement it when your app has those things; without it the shell still renders.

:::tip[Tenants]

A tenant without an `Href` is switched by writing the `modulus-tenant` cookie and reloading the page. Cancel the `modulus:tenant-change` event to handle the switch yourself. Set `ShellContent.CurrentTenantId` when your app resolves the tenant itself.

:::

## The menu

Each `IMenuContributor` adds items through `ConfigureMenuAsync`. Items are `MenuItemDefinition` values with a key, a label, an `Href`, an icon, an `Order`, an optional `RequiredPolicy`, and optional children.

- `Href` is relative to the app, with no leading slash: `customers`, not `/customers`.
- `RequiredPolicy` hides an item from users who do not satisfy the policy.
- Nested menus fold as a tree to any depth. In the navbar, a group directly under the top level is a bold section; in the sidebar, groups are folding branches. The branches on the path to the current page start open.
- `MenuScope.App` (set in the theme options) shows only the current app's menu, with the other apps in the launcher, as in Odoo.

One menu definition serves Blazor, MVC and Razor Pages, so the menu does not have to be written twice.

## Services and the theme

`AddModulusBlazor` and `AddModulusMvc` take a theme action. The values you set there (title, primary colour, layout) are defaults. Users can change most of them in the settings panel, and their choices are kept in cookies:

- `modulus-theme` holds light, dark or system mode, and the layout choices.
- `modulus-tenant` holds the current tenant.
- The culture cookie holds the language when you list `Cultures`.

Because the choices are in cookies, the server renders the right theme on the first byte, with no flash of the wrong colours. The full list of options is in [Theming](./theming.md).

## Blazor: components and state

In Blazor, the pages are interactive server components. A button click or a table sort runs C# on the server and sends the change back over the connection, so you write no JavaScript. The Pro components (`ProTable`, `DrawerForm`, `ModalForm`) take callbacks: `Request` loads a page of rows, `OnFinish` saves a form and returns whether it succeeded.

## MVC and Razor Pages: server pages with htmx

MVC and Razor Pages render full HTML on the server, like any ASP.NET Core site. Modulus adds a few small patterns on top, so common interactions do not need a page reload:

- **htmx** (`hx-get`, `hx-post`, `hx-trigger`) loads a fragment into a place on the page. A table that refreshes when something changes is a `div` with `hx-get` and `hx-trigger="customers:refresh from:body"`.
- **The drawer** is a side panel. A button calls `$store.drawer.load(url, title)` (Alpine.js), which loads the form into `#modulus-drawer-body`.
- **The response helpers** tell the page what to do after a save: `Response.HxMessage(...)` shows a toast, `Response.HxCloseDrawer()` closes the drawer, and `Response.HxTrigger("customers:refresh")` fires an event that the table listens for.

```csharp title="After a successful save"
Response.HxMessage(MessageType.Success, $"Created {customer.Name}");
Response.HxCloseDrawer();
Response.HxTrigger("customers:refresh");
return NoContent();
```

The layout sends the antiforgery token with every htmx request, so form posts are protected without extra code.

:::note[Razor Pages and SPA navigation]

With `SpaNavigation` turned on in the theme options, links and forms in the shell load through htmx `hx-boost`. The server returns only the page body, the sidebar and the header, so scripts and Alpine state stay loaded between pages. Turn it on after reading the [upgrading notes](https://github.com/modulus-framework/modulus-ui/blob/main/docs/UPGRADING.md#spa-navigation-and-ishtmx).

:::

## Known limits

These are the limits to plan around. The full list is in the README's known limitations section.

- **Ant Design Blazor 1.6 ships antd v4 styling.** Modulus adds a refresh layer that looks like Ant Design 5, but it is not pixel-identical to antd v5.
- **Interactive MVC widgets need Alpine.** Cascader, tree select, transfer, tree, calendar, carousel, anchor, mentions, splitter, colour picker and tour run on small Alpine components. Form widgets post through hidden inputs, so forms still submit without script.
- **Native date pickers in MVC.** `app-date-picker` and `app-range-picker` use the browser's own calendar inside Ant Design's styling.
- **Icons in MVC** reuse Ant Design Blazor's icon store through reflection, because the API is internal. A unit test catches it if an upgrade moves it.
- **Localization covers the framework, not the sample text.** Ant Design's strings, dates and numbers follow the request culture; the sample pages stay in English.
- **Authorization** is wired for menus (`RequiredPolicy`). The samples sign in against in-memory users; there is no user store, registration or password reset behind the auth pages.
