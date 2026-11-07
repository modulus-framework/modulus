---
title: "Theming and layout"
description: "The theme options (colours, layout, density, direction, languages), where to set them, and how users change them with the settings panel."
sidebar_position: 10
---

The look of a Modulus app comes from one options object, `ModulusThemeOptions`. Set it in code when you register Modulus, or bind it from configuration, and every page follows it.

## Set the options

In code, in `Program.cs`:

```csharp title="Program.cs"
builder.Services.AddModulusBlazor(theme =>
{
    theme.Title = "Contoso Admin";
    theme.PrimaryColor = "#1677ff";
});
```

Or from configuration, which is handy when the same app runs for several customers:

```json title="appsettings.json"
"Modulus": {
  "Theme": {
    "Title": "Modulus Admin",
    "PrimaryColor": "#066fd1",
    "BorderRadius": 6,
    "DefaultMode": "Light",
    "Layout": "Horizontal",
    "MenuScope": "App",
    "SpaNavigation": true,
    "Cultures": [ "en-US", "de-DE", "fr-FR" ]
  }
}
```

In MVC and Razor Pages, use `AddModulusMvc` in the same way.

## The options

| Option | Default | What it does |
| --- | --- | --- |
| `Title`, `LogoUrl` | "Modulus Admin", the built-in mark | The header brand, and the suffix of the page title |
| `PrimaryColor`, `SuccessColor`, `WarningColor`, `ErrorColor`, `InfoColor` | Tabler palette: `#066fd1`, `#2fb344`, `#f59f00`, `#d63939`; info follows the primary | Each colour gets a 10-step palette, generated on the server |
| `BorderRadius` | 6 | Controls. Cards and overlays use the radius plus 2. |
| `DefaultMode` | `Light` | `Light`, `Dark` or `System`. The user's choice is kept in the `modulus-theme` cookie. |
| `NavTheme`, `HeaderTheme` | `Light`, `Light` | The colour of the navigation and the header |
| `Layout` | `Horizontal` | `Horizontal`, `Vertical` or `Condensed` |
| `ContentWidth`, `Header` | `Boxed`, `Fixed` | Width: `Boxed`, `Fluid` or `Framed`. Header: `Fixed`, `Sticky`, `Static` or `Floating`. |
| `SidebarPosition`, `SidebarStyle`, `SidebarFold` | `Left`, `Default`, `Full` | The sidebar, in the vertical layout only |
| `Direction` | `Ltr` | `Rtl` sets `dir="rtl"` on the page |
| `MenuScope` | `All` | `App` shows only the current app's menu, with the other apps in the launcher |
| `Density`, `Font`, `Base` | `Compact`, `Sans`, `Gray` | Spacing and type size, the font family, and the neutral palette |
| `ColorPresets`, `ShowCustomizer` | eight colours, `true` | The colours in the settings panel, and whether the panel is shown |
| `Cultures` | none | Two or more add a language picker to the settings panel |
| `SpaNavigation`, `SpaPrefetch` | `false`, `true` | SPA navigation for MVC and Razor Pages, and prefetching on hover |
| `SiderWidth`, `HeaderHeight` | 240, 56 | The comfortable density. The compact density uses 224 and 48. |
| `Copyright`, `Version` `"{year} Modulus"`, none | The footer |

## Layout presets

The settings panel has fifteen one-click presets for the usual demo layouts: Boxed, Condensed, Fluid, Fluid vertical, Horizontal, Navbar dark, Navbar floating, Navbar sticky, Right vertical, RTL, Vertical, Vertical transparent, Vertical floating, Folded sidebar and Folded hover. Each one sets several of the options above.

## What users can change

Users change most of these options in the settings panel. Their choices are kept in cookies, so the server renders them on the first paint, with no flash of the default theme. Light and dark mode works the same way.

## Languages

List the languages in `Cultures`, and the settings panel gets a language picker that sets the culture cookie.

- In MVC and Razor Pages, call `app.UseModulusLocalization()` before `UseRouting`.
- In Blazor, call `UseRequestLocalization` with the same cultures.

Ant Design's own strings, dates and numbers then follow the language. The sample pages stay in English, so translate your own text with the usual resource files.

## Dark mode and the brand colour

The brand colour replaces Ant Design's blue in dark mode too, through the generated `antd-dark-brand.css`. You do not need to write dark-mode styles for the brand colour.

## Tenants

The header's tenant switcher writes the `modulus-tenant` cookie and reloads the page. Your app reads the cookie to choose the tenant's data. See [How the pieces fit together](./concepts.md#the-shell).
