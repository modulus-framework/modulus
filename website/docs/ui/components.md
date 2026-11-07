---
title: "Components"
description: "Every Modulus component by category, with the Blazor name and the MVC tag helper for each."
sidebar_position: 6
---

Modulus gives you every Ant Design Blazor component, in both flavours.

- **In Blazor** use the `App*` name, for example `AppButton`, which is an alias of `AntDesign.Button`. The alias keeps every upstream parameter, so new ones arrive with an upgrade. Components with no alias use the Ant Design name directly, such as `Table` or `Input`, and the tutorial's `Button` works the same way.
- **In MVC and Razor Pages** use the `app-*` tag helpers. Add `@addTagHelper *, Modulus.AspNetCore.Mvc` to `_ViewImports.cshtml` once. Controls bind with `asp-for` (name, id, value and validation state); any other attribute, such as `placeholder`, `hx-*` or `x-*`, goes to the real element.

The samples show every component at `/ui/components`, in the MVC sample and the Blazor sample alike.

## General

| Blazor | MVC tag helper | Notes |
| --- | --- | --- |
| `AppButton` | `app-button` | `type`, `danger`, `loading`, `icon`, `html-type` |
| `AppIcon` | `app-icon` | Ant Design icons by name |
| `AppTitle`, `AppText`, `AppParagraph`, `AppLink` | `app-title`, `app-text`, `app-paragraph`, `app-link` | |
| `AppFloatButton`, `AppFloatButtonGroup`, `AppFloatBackTop` | `app-float-button`, `app-float-button-group`, `app-float-back-top` | The group opens on click or hover |

## Layout

| Blazor | MVC tag helper | Notes |
| --- | --- | --- |
| `AppDivider` | `app-divider` | |
| `AppSpace` | `app-space` | |
| `AppRow`, `AppCol` | `app-row`, `app-col` | 24-column grid, `gutter`, `xs` to `xxl` |
| `AppFlex` | `app-flex` | |
| `AppLayout`, `AppHeader`, `AppSider`, `AppContent`, `AppFooter` | `app-layout`, `app-layout-header`, `app-layout-sider`, `app-layout-content`, `app-layout-footer` | |
| `AppSplitter`, `AppSplitterPanel` | `app-splitter`, `app-splitter-panel` | Drag, or use the arrow keys |

## Navigation

| Blazor | MVC tag helper | Notes |
| --- | --- | --- |
| `AppBreadcrumb` | `app-breadcrumb` | |
| `AppTabs`, `AppTabPane` | `app-tabs`, `app-tab` | |
| `AppSteps`, `AppStep` | `app-steps`, `app-step` | |
| `AppDropdown`, `AppDropdownButton` | `app-dropdown`, `app-dropdown-item`, `app-dropdown-button` | |
| `AppMenu`, `AppSubMenu`, `AppMenuItem` | `app-menu`, `app-sub-menu`, `app-menu-item`, `app-menu-divider` | Horizontal, vertical or inline |
| `AppPagination` | `app-pagination` | Plain links, or htmx |
| `AppAnchor`, `AppAnchorLink` | `app-anchor`, `app-anchor-link` | Highlights the section in view |
| `AppAffix`, `AppBackTop` | `app-affix`, `app-back-top` | |

## Data entry

| Blazor | MVC tag helper | Notes |
| --- | --- | --- |
| `AppFormItem` | `app-form-item` | `asp-for` gives the label and the validation message |
| `AppInput`, `AppInputPassword`, `AppSearch` | `app-input`, `app-input-search` | Affixes, addons, clear, password, datalist for autocomplete |
| `AppTextArea` | `app-textarea` | `show-count` |
| `AppInputNumber` | `app-input-number` | |
| `AppSelect` | `app-select` | |
| `DatePicker`, `RangePicker` | `app-date-picker`, `app-range-picker` | `picker` can be `date`, `week`, `month`, `time`, `quarter` or `year`; `presets` uses the built-in date periods |
| `AppCheckbox`, `CheckboxGroup` | `app-checkbox`, `app-checkbox-group` | |
| `RadioGroup` | `app-radio-group` | |
| `AppSwitch` | `app-switch` | |
| `Slider` | `app-slider` | |
| `AppRate` | `app-rate` | |
| `AppSegmented` | `app-segmented` | |
| `AppUpload` | `app-upload` | Also `action`, `max-size`, `max-count` and `list-type="picture"` |
| `AppCascader`, `TreeSelect`, `AppTransfer`, `AutoComplete`, `AppMentions` | `app-cascader`, `app-tree-select`, `app-transfer`, `app-auto-complete`, `app-mentions` | |
| `AppInputGroup` | `app-input-group` | Compact mode |
| `AppColorPicker` | `app-color-picker` | Hex or RGB, alpha, presets, clear |
| `AppCreatableSelect` | `app-creatable-select` | An "Add item" row with tag colours |
| `AppSchemaForm` | `app-schema-form` | A whole form drawn from a `FormSchema`; see [Forms](./forms.md) |

## Data display

| Blazor | MVC tag helper | Notes |
| --- | --- | --- |
| `AppCard` | `app-card` | `app-card-cover`, `app-card-meta`, `app-card-action` |
| `AppComment` | `app-comment` | |
| `AppCountDown` | `app-countdown` | |
| `AppImage`, `AppImageGroup` | `app-image`, `app-image-group` | Shared preview |
| `AppAvatar`, `AppAvatarGroup` | `app-avatar`, `app-avatar-group` | |
| `AppBadge`, `AppBadgeRibbon` | `app-badge`, `app-badge-ribbon` | |
| `AppTag` | `app-tag` | |
| `AppStatistic` | `app-statistic` | |
| `AppDescriptions` | `app-descriptions` | |
| `AppTable` | `app-table` | `hidden-columns`, `density`, `scroll-x`, `table-footer` |
| `AppList` | `app-list` | |
| `AppTimeline` | `app-timeline` | |
| `AppCollapse`, `AppPanel` | `app-collapse`, `app-panel` | |
| `AppTooltip`, `AppPopover` | `app-tooltip`, `app-popover` | |
| `AppPopconfirm` | `app-popconfirm` | Attributes such as `hx-delete` go on the OK button; OK also raises `modulus:popconfirm-ok` |
| `AppTimePicker` | `app-time-picker` | Same as `app-date-picker picker="time"` |
| `AppEmpty` | `app-empty` | |
| `AppTree` | `app-tree` | Checkable |
| `AppCalendar` | `app-calendar` | |
| `AppCarousel` | `app-carousel`, `carousel-slide` | |
| `AppQRCode` | `app-qr-code` | Icon and status overlays, drawn as SVG |
| `AppTour`, `TourStep` | `app-tour`, `tour-step` | Spotlight, placement with flipping, keyboard |

## Feedback

| Blazor | MVC tag helper | Notes |
| --- | --- | --- |
| `AppAlert` | `app-alert` | |
| `AppProgress` | `app-progress` | Line, circle or dashboard |
| `AppResult` | `app-result` | |
| `AppSkeleton` | `app-skeleton` | |
| `AppSpin` | `app-spin` | |
| `AppModal` | `app-modal` | |
| `AppDrawer` | `app-drawer` | Right, left, top or bottom |
| `AppWatermark` | `app-watermark` | |

For messages from a save, the MVC helpers are `Response.HxMessage`, `HxNotify` and `HxCloseModal` on the server. Add `hx-confirm` to a button to ask before it sends.

## Charts

Charts have no JavaScript dependency. The geometry is shared by Blazor and MVC, the tooltips are CSS only, and each chart carries a screen-reader data table.

| Blazor | MVC tag helper |
| --- | --- |
| `LineChart` (line or area) | `app-line-chart` |
| `ColumnChart` (grouped or stacked) | `app-column-chart` |
| `BarChart` | `app-bar-chart` |
| `DonutChart` | `app-donut-chart` |
| `HeatmapChart` | `app-heatmap` |
| `Sparkline` | `app-sparkline` |
| `MiniBar` | `app-mini-bar` |

Business charts (treemap, pareto, waterfall, variance, bullet, gauge, funnel, combo and more) are in the same family. `legend-toggle` lets the legend hide series, and `links` turns columns and axis labels into drill-down links.

## Icons

`app-icon` draws any Ant Design icon. `app-ti` draws an Ant Design outline icon by name, inline. Names starting with `tb:` are Tabler fallbacks, for the few icons Ant Design does not have.

## Pro components, plugins and forms

The data-heavy components are in [Pro components](./pro.md). The rich-text editor, maps, PDF viewer, spreadsheet, uploads, code editor and the others are in [Plugins](./plugins.md). Business fields such as money, phone and lookup are in [Forms](./forms.md).
