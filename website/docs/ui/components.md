---
sidebar_position: 3
---

# Components

Modulus UI gives you the same components in every engine. In Blazor they are Ant Design components with `App*` names. In MVC and Razor Pages they are tag helpers (`app-*`) that write the same markup.

The rule for both: a component's parameters are the Ant Design parameters, so the Ant Design documentation applies. Modulus adds only what Ant Design Blazor 1.6 does not have, and the business fields.

## Blazor and MVC names

| Ant Design | Blazor | MVC / Razor Pages |
|---|---|---|
| Button | `AppButton` | `<app-button>` |
| Form item | `AppFormItem` | `<app-form-item asp-for="...">` |
| Table | `AppTable` | `<app-table>` |
| Modal | `AppModal` | `<app-modal>` |

Blazor's `App*` names are thin subclasses of the Ant Design component (`AppButton : AntDesign.Button`), so every upstream parameter works and new ones arrive on upgrade.

The MVC tag helpers bind to a model with `asp-for` (the name, the id, the value and the validation state). Any other attribute, such as `placeholder`, `hx-*` or `x-*`, goes to the real element. Forms are plain HTML and post normally, so they still work without JavaScript.

## Components by category

### General

`app-button` (with `loading`), `app-icon`, `app-title`, `app-text`, `app-paragraph`, `app-link`, `app-float-button`, `app-float-button-group` (click or hover), `app-float-back-top`.

### Layout

`app-divider`, `app-space`, `app-row` and `app-col` (a 24-column grid with gutters and xs to xxl breakpoints), `app-flex`, `app-layout` with `app-layout-header`, `app-layout-sider`, `app-layout-content` and `app-layout-footer`, `app-splitter` and `app-splitter-panel` (drag and arrow keys).

### Navigation

`app-breadcrumb`, `app-tabs` and `app-tab`, `app-steps` and `app-step`, `app-dropdown` and `app-dropdown-item`, `app-dropdown-button`, `app-menu` with `app-sub-menu`, `app-menu-item` and `app-menu-divider` (horizontal, vertical or inline), `app-pagination` (htmx or plain links), `app-anchor` and `app-anchor-link` (scroll spy), `app-affix`, `app-back-top`.

### Data entry

`app-form-item`, `app-input` (affixes, addons, clear, password, a datalist for autocomplete), `app-input-search`, `app-textarea` (with a count), `app-input-number`, `app-select`, `app-date-picker` (date, week, month, time, quarter, year), `app-range-picker` (with `presets`, from `DatePeriods`), `app-checkbox`, `app-checkbox-group`, `app-radio-group`, `app-switch`, `app-slider`, `app-rate`, `app-segmented`, `app-upload` (with `action`, `max-size`, `max-count` and `list-type="picture"`), `app-cascader`, `app-tree-select`, `app-transfer`, `app-auto-complete`, `app-mentions`, `app-input-group` (compact), `app-color-picker` (hex and RGB, alpha, presets, clear), `app-creatable-select` (an "Add item" row with tag colours), `app-schema-form` (a whole form from a `FormSchema`).

### Data display

`app-card` (with `app-card-cover`, `app-card-meta` and `app-card-action`), `app-comment`, `app-countdown`, `app-image-group`, `app-avatar` and `app-avatar-group`, `app-badge` and `app-badge-ribbon`, `app-tag`, `app-statistic`, `app-descriptions`, `app-table` (`hidden-columns`, `density`, `scroll-x`, `table-footer`), `app-table-columns` (a column picker), `app-table-settings` (reload and density), `app-list`, `app-timeline`, `app-collapse` and `app-panel`, `app-image`, `app-tooltip`, `app-popover`, `app-empty`, `app-tree` (checkable), `app-calendar`, `app-carousel` with `carousel-slide`, `app-qr-code` (with an icon and a status overlay), `app-tour` with `tour-step`.

### Feedback

`app-alert`, `app-progress` (line, circle or dashboard), `app-result`, `app-skeleton`, `app-spin`, `app-modal`, `app-drawer` (right, left, top or bottom), `app-watermark`. From the server, `Response.HxMessage`, `HxNotify`, `HxCloseModal` and the `hx-confirm` attribute drive these.

### Pro components

These follow the ProComponents conventions.

- `app-dashboard`: tiles on a 12-column grid, moved and resized with buttons and saved per user. Its model is `DashboardModel`, mapped with `MapModulusDashboard`.
- `app-file-manager`: folders, a breadcrumb, search, create, rename and delete. Its model is `FileManagerModel`.
- `app-page-container`, `app-page-header`, `app-statistic-card`, `app-trend`.
- `app-editable-grid`: see [Data tables and grids](./data-grids).
- `app-nested-grid`: see [Data tables and grids](./data-grids).
- `app-pivot-grid`: an Odoo-style pivot. See [Data tables and grids](./data-grids).

### Large data

`app-load-more` (infinite scroll) and `app-virtual-table` (virtual scrolling, only the rows in view are in the page).

### Charts

`app-line-chart`, `app-column-chart`, `app-bar-chart`, `app-donut-chart`, `app-heatmap`, `app-sparkline`, `app-mini-bar`, and the business charts (treemap, pareto, waterfall, variance, bullet, gauge, funnel, combo and more). The chart geometry is shared by Blazor and MVC, tooltips are CSS-only, and each chart carries a screen-reader data table. `legend-toggle` lets the legend hide series, and `links` makes columns and axis labels drill-down links.

### Plugins

Each plugin is a Blazor component and a tag helper over one renderer. The library loads only on pages that use it.

| Tag | Library | What it does |
|---|---|---|
| `app-editor` | Tiptap | WYSIWYG editor. The HTML is cleaned by `RichText.Sanitize(images: true)`. |
| `app-map` | MapLibre GL JS | Tile map with markers and popups |
| `app-vector-map` | jsVectorMap | Choropleth with hover values and a legend |
| `app-pdf` | pdf.js | PDF viewer, one page at a time, with zoom and download. Same-site or http(s) addresses only. |
| `app-spreadsheet` | Univer | Excel-style multi-sheet grid with formulas. It posts its sheets as JSON and follows the theme. |
| `app-media-player` | (native) | Video and audio with custom controls, poster and captions |
| `app-uploader` | Uppy | Drop area: each file uploads when dropped, with previews, progress and type, size and count limits |
| `app-image-cropper` | Cropper.js | Aspect-ratio buttons. The crop goes to a form field as JSON. |
| `app-code-editor` | CodeMirror 6 | Highlighting, folding and search for JavaScript, TypeScript, JSON, CSS and HTML |
| `app-sortable` and `app-sortable-item` | (native) | Reorder a list, drag between lists, handles and keyboard moves. It posts the order. |
| `app-lightbox` | (native) | Image lightbox |
| `app-barcode` | (native) | Code 128 and EAN-13 as SVG |

### Ant Design 5 components Blazor 1.6 does not have

`app-qr-code`, `app-color-picker`, `app-float-button` and `app-float-button-group`, `app-float-back-top`, and `app-tour`. They draw the Ant Design 5 markup in both engines.

### Business fields

These are for ERP-style forms. Each one posts a plain, invariant value, so the server reads the same value whatever the user typed.

| Component | Input | Posted value |
|---|---|---|
| `AppMoneyInput` (`app-money-input`) | Grouped amount, currency picker, the currency's decimals | The amount |
| `AppQuantityInput` (`app-quantity-input`) | A quantity with a unit of measure and its precision | The quantity and the unit |
| `AppDurationInput` (`app-duration-input`) | `1:30`, `1.5` or `90m` | Minutes |
| `AppNumberRange` (`app-number-range`) | A from and to range | Both numbers |
| `AppPhoneInput` (`app-phone-input`) | A country picker and a national number | `+1 202 555-0111` |
| `AppMaskedInput` (`app-masked-input`) | A mask such as `99-9999999` | The unmasked value |
| `AppOtpInput` (`app-otp-input`) | One box per character. Paste and SMS autofill spread over the boxes. | The code |
| `AppLookup` (`app-lookup`) | A many-to-one field with type-ahead and a "Search more" dialog | The selected value |
| `AppStatusBar` (`app-status-bar`) | Workflow stages, with folded ones | The current stage |
| `AppSignaturePad` (`app-signature-pad`) | A drawn signature | A PNG data URL |
| `AppRichText` (`app-rich-text`) | A small editor: formatting, lists, links and tables | Sanitized HTML |

`AppSchemaForm` (`app-schema-form`) draws a whole form from a `FormSchema`. Read a schema from JSON with `FormSchema.Parse`, or build it in code. The field types are text, multiline, number, money, date, choice and switch. The same rules run on the server: `Validate` first, then `Normalize` for what is stored. The parsing and checks live in `Modulus.Ui.Abstractions.Forms` (`NumberText`, `DurationText`, `Currency`, `UnitOfMeasure`, `InputMask`, `PhoneNumber`, `LookupSearch`, `RichText`, `SignatureImage`, `FormSchema`), so the server accepts exactly what the browser accepts.

`AppLookup` and `AppCreatableSelect` search the server with `GET {search-href}?q=text&max=n`. The endpoint answers `[{ value, label, description, fields }]`.

### Select with "Add item"

`AppCreatableSelect` (`app-creatable-select`) ends its dropdown with a name, a tag colour and an Add button. The new option joins the list and is selected. `GridSchema.AddOption` checks it (unique, trimmed, at most 50). For real records, `OnAddNew` (Blazor) or `add-modal` (MVC) open a create form instead.

### Icons

`app-icon` draws any Ant Design icon. `app-ti` draws an Ant Design outline icon by name, inline, and the `tb:` names are the Tabler fallbacks where Ant Design has no icon.

## Interactive widgets in MVC

Cascader, TreeSelect, Transfer, Tree, Calendar, Carousel, Anchor, Mentions, Splitter, ColorPicker and Tour run on small Alpine components in `modulus-widgets.js`. Their form values post through hidden inputs, so the form still submits without script.

## Localization of the components

Ant Design's built-in strings, dates and numbers follow the request culture. The sample pages stay in English. See [Theming and localization](./theming).
