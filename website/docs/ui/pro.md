---
title: "Pro components"
description: "The data-heavy Modulus components: a table with server paging, editable and nested grids, a pivot grid, forms in drawers and dialogs, dashboards and large data."
sidebar_position: 7
---

The Pro components are for business screens: lists you can search, sort, filter and export; grids you can edit in place; pivot tables for reports; and forms that open in a drawer or a dialog. They follow the conventions of Ant Design Pro, and they work the same in Blazor and in MVC.

## The data table

`ProTable` in Blazor and `app-table` with the htmx helpers in MVC share one request model. The table sends the page, the page size, the sort and the column searches, and your code returns one page of rows. Paging, sorting and filtering happen on the server, so a table of a few rows and a table of millions behave the same way.

It has:

- server paging with page sizes of 10, 20, 50 and 100, a "1–10 of 42" range and jumps with •••;
- sorting by one column, or by several with Shift+click (`MultiSort`);
- a filter row, a search box, and a Clear button that shows while a search or filter is set;
- row selection, batch actions, and expandable row details (`RowDetail`);
- resizable columns, density, a column picker, reload and Export CSV;
- a Group by menu (`GroupFields`), with a header and subtotal per group;
- multi-level headers (`HeaderGroups`), with titled bands over neighbouring columns;
- row drag and drop (`OnRowMove`), with the keyboard too: focus the handle and press Up, Down, Home or End;
- a checkbox filter menu per column, like Excel's, with a search box, Select all and "(Blanks)".

The tutorials build a table with search, sorting and batch delete. See [Build an app with Blazor](./tutorial-blazor.md) and [Build an app with MVC](./tutorial-mvc.md).

:::tip[Keep the URL in step]

A Blazor `ProTable` with `UrlState` keeps its page, sort and column searches in the URL. Use it when people should be able to bookmark or share a filtered list.

:::

## Forms in a drawer or a dialog

`DrawerForm` and `ModalForm` (Blazor), or `app-drawer` and `app-modal` with a form inside (MVC), open a form beside the page.

- The form is validated before it submits.
- `OnFinish` is async and returns whether the save succeeded. The form closes only when it did.
- In MVC, the save answers with `Response.HxCloseDrawer()` and `Response.HxTrigger(...)`, so the table refreshes. See [How the pieces fit together](./concepts.md#mvc-and-razor-pages-server-pages-with-htmx).

## Statistic cards

`StatisticCard` (Blazor) and `app-statistic-card` (MVC) show one number with its label and an optional `Trend`, a small arrow and percentage that says whether the number went up or down. `FooterToolbar` pins a bar of actions to the bottom of the page, for a save button on a long form.

## Editable grid

`EditableGrid` (Blazor) and `app-editable-grid` (MVC) look and work like the data table, but each cell can be edited in place.

Columns are data: a `GridColumn` describes a text, number, currency, percent, select, multi-select, tag, date, switch, progress or rating column. Each cell edits with the matching control and is validated.

- Rows can expand into a detail template.
- Columns can be pinned left or right, hidden, reordered, resized (drag the edge of the header), and added or removed at run time.
- Changed rows and cells are coloured until you save, and new rows are marked.
- Row states: a row can be `ReadOnly` (cells fixed) or `Locked` (no edit, delete or move), with the reason shown on the row.
- Batch actions work on the selected rows: set a value, export them or delete them.

Blazor and MVC share one schema, formatter, search and validator (`GridSchema`), so a rule you write once applies in both.

## Nested grid

`NestedGrid` (Blazor) and `app-nested-grid` (MVC) put a table inside each row of another table, to any depth.

`NestedGridSettings` lists the levels. Each level has its own columns, search, filters, sort, page size, editing and expand rules. A child table loads the first time its parent row expands, and each level saves on its own. The number of levels you configure is the nesting limit.

## Pivot grid

`AppPivotGrid` (Blazor) and `app-pivot-grid` (MVC) give an Odoo-style pivot view: rows and columns are grouped by fields, and each cell holds a measure.

- `PivotQuery<T>` registers the fields you can group by: text, enum, and dates by year, quarter, month, week or day.
- The measures are sum, average, minimum and maximum; count is built in.
- Only the headers that are open are read from the database, so a collapsed header costs one grouped query, and every total comes from the database.
- The whole layout is in the URL. Expand, collapse, flip the axes, pick the measures and sort are links, so a pivot can be bookmarked.
- A value cell can drill down (`CellHref` in Blazor, `cell-href` in MVC). `PivotXlsx` writes the Excel download with merged headers and bold totals, with no extra library.

## Large data

A table over millions of rows must read one page from the database, not all the rows and then page them in memory. `TableQuery<T>` (in `Modulus.Ui.Abstractions.Data`) turns a `ProTableRequest` into an `IQueryable` that your database runs.

```csharp title="Orders.cs"
static readonly TableQuery<Order> Orders = TableQuery<Order>.By(o => o.Id)
    .Sort("date", o => o.Date).Sort("amount", o => o.Amount)
    .SearchText("customer", o => o.Customer)
    .DefaultSort("date", descending: true);

var page = await Orders.PageAsync(db.Orders, request, CountMode.Exact, (q, ct) => q.CountAsync(ct), ct);
```

- Only the sorts and column searches you register are applied, so a request cannot sort or filter on a column you did not expose.
- Numbered pages use `Skip` and `Take`. Pages after the first use a keyset cursor, which stays fast on deep pages.
- `CountMode.None` skips `COUNT(*)` when you do not need the total.
- In MVC, `app-load-more` gives infinite scroll, and `app-virtual-table` renders only the rows in view.
- In Blazor, `Virtualize` reads the same query.
- The sample at `/tables/large` shows all three over 100,000 rows, and `/tables/pivot` pivots the same shipments.

## Saved views

`AppSavedViews` (Blazor) and `app-saved-views` (MVC) add a Views menu to a page. A signed-in user names the current layout (its query string), stars one view as the default, and deletes views. Views are kept per user in `IUiStateStore`. The default store is in memory, so register your own to keep them across restarts.

In MVC and Razor Pages, map the endpoint with `app.MapModulusSavedViews()`.

## Dashboards

`app-dashboard` (MVC) and its Blazor twin place tiles on a 12-column grid. People move and resize the tiles with buttons, and the layout is saved per user. `MapModulusDashboard` maps the endpoints that store it.

## Ant Design components the library adds

These are in both flavours, and they are not in Ant Design Blazor 1.6: [`AppQRCode`](./components.md#data-display), `AppColorPicker`, the floating buttons and `AppTour`. They follow the Ant Design 5 markup, so they may differ slightly in detail from the Ant Design 5 versions.
