---
sidebar_position: 6
---

# Theming and Localization

The look of a Modulus app comes from one options object, `ModulusThemeOptions`. Blazor, MVC and Razor Pages read the same options, so one configuration section sets the brand in every engine.

## Set the theme

Bind the options from configuration:

```json
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

Or set them in code:

```csharp
builder.Services.AddModulusMvc(theme =>
{
    theme.Title = "Contoso ERP";
    theme.PrimaryColor = "#1677ff";
});
```

Blazor uses `AddModulusBlazor` with the same lambda. The generated web project already binds the `Modulus:Theme` section.

## Options

| Option | Default | What it does |
|---|---|---|
| `Title`, `LogoUrl` | "Modulus Admin", the built-in mark | Header brand and the suffix of the document title |
| `PrimaryColor`, `SuccessColor`, `WarningColor`, `ErrorColor`, `InfoColor` | Tabler palette (`#066fd1`, `#2fb344`, `#f59f00`, `#d63939`; info follows primary) | Each colour gets a generated 10-step palette |
| `BorderRadius` | 6 | Controls. Cards and overlays use the radius plus 2. |
| `DefaultMode` | `Light` | `Light`, `Dark` or `System`. The user's choice is stored in the `modulus-theme` cookie. |
| `NavTheme`, `HeaderTheme` | `Light`, `Light` | Colour of the navigation and the header row |
| `Layout` | `Horizontal` | `Horizontal`, `Vertical` or `Condensed` |
| `ContentWidth` | `Boxed` | `Boxed`, `Fluid` or `Framed` |
| `Header` | `Fixed` | `Fixed`, `Sticky`, `Static` or `Floating` |
| `SidebarPosition`, `SidebarStyle`, `SidebarFold` | `Left`, `Default`, `Full` | The vertical layout only. Position is start or end; the style is default, transparent or floating; the fold is full, folded with flyouts, or folded until hovered. |
| `Direction` | `Ltr` | `Rtl` sets `dir="rtl"` on `<html>` |
| `MenuScope` | `All` | `App` shows only the current app's menu, with the other apps in the launcher (Odoo style) |
| `Density`, `Font`, `Base` | `Compact`, `Sans`, `Gray` | Spacing and type size, the font family, and the neutral palette |
| `ColorPresets`, `ShowCustomizer` | eight colours, `true` | The theme settings panel |
| `Cultures` | none | Two or more cultures add a language picker |
| `SpaNavigation`, `SpaPrefetch` | `false`, `true` | MVC and Razor Pages only: navigation without full page loads, and prefetch on hover |
| `SiderWidth`, `HeaderHeight` | 240, 56 | Comfortable density. Compact uses 224 and 48. |
| `Copyright`, `Version` | `{year} Modulus`, none | Footer |

Users change most of these in the theme settings panel of the shell. Their choices are stored in cookies, so the server renders them correctly on the first byte, with no flash of the wrong theme.

## Presets

The settings panel has fifteen one-click presets for common layouts: Boxed, Condensed, Fluid, Fluid vertical, Horizontal, Navbar dark, Navbar floating, Navbar sticky, Right vertical, RTL, Vertical, Vertical transparent, Vertical floating, Folded sidebar and Folded hover.

## Dark mode and the brand colour

Light and dark mode both follow the palette. Ant Design's dark stylesheet hard-codes its default blue, so the framework repeats those declarations with your brand's dark palette (`antd-dark-brand.css`). The primary button keeps your brand colour in dark mode too.

## The `ui theme` commands

The CLI manages named themes stored in the app's `Themes` folder:

```bash
modulus ui theme create corporate --colors blue
modulus ui theme create mono --base minimal --colors "#0b7285"
modulus ui theme list
modulus ui theme set corporate
modulus ui theme export corporate --format tailwind
```

`set` writes the theme's values into `Modulus:Theme` in `appsettings.json`. `export` writes the theme as CSS variables, SCSS variables, a Tailwind config or JSON. See [The ui commands](./cli) for the options.

## Localization

List the languages in `Cultures`. The settings panel then shows a language picker that stores the culture in a cookie.

- MVC and Razor Pages: `app.UseModulusLocalization()` sets the request culture from `Cultures`.
- Blazor: `UseRequestLocalization` with the same cultures does the same job.

The request culture drives Ant Design's `LocaleProvider` (built-in strings, dates and numbers) and the tag helpers. `modulus ui add-i18n` sets the languages and wires localization for you:

```bash
modulus ui add-i18n --languages en,es,fr
```

The first language is the default.

The framework's own strings follow the culture. The sample pages stay in English.

## The `<html lang>` attribute

The layout sets `lang` from the current UI culture, so screen readers and browsers pick the right language.
