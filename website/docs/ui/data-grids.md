---
sidebar_position: 4
---

# Data Tables and Grids

Most admin screens are a list of records. Modulus UI has four components for lists, plus the query types that feed them.

| Component | Use it for | Blazor | MVC / Razor Pages |
|---|---|---|---|
| Data table | Reading and searching a list, with optional row actions | `ProTable` | `app-table`, with the list endpoints |
| Editable grid | Editing many rows in place, with validation | `EditableGrid` | `app-editable-grid` |
| Nested grid | A parent row that expands into child tables, to any depth | `NestedGrid` | `app-nested-grid` |
| Pivot grid | Grouping and totalling records, as Odoo's pivot view does | `AppPivotGrid` | `app-pivot-grid` |

The data table and the editable grid look and work alike: a search box, a filter row, a Columns button, Export CSV, row checkboxes with a batch bar, and sorting. Blazor and MVC share one schema, formatter, search and validator (`GridSchema`), so a column defined once works in both.

## Data table (`ProTable`)

A data table asks your code for one page of rows at a time. The request carries the page, page size, sort, column searches and group-by; your code returns the rows and the total.

```razor
<ProTable TItem="Customer" HeaderTitle="Customers" RowKey="c => c.Id" Request="LoadAsync" MultiSort="true">
    <Columns>
        <PropertyColumn Property="c => c.Name" Sortable />
        <PropertyColumn Property="c => c.Company" />
    </Columns>
</ProTable>
```

Built-in behaviour:

- Server paging, sorting, and Shift+click multi-column sort (`ProTable MultiSort`). Sort orders are kept in the URL (`thenBy`).
- Column searches, with a checkbox filter menu that lists the column's values with counts, a search box, Select all and "(Blanks)", as in Excel. The filter runs as an SQL `IN`.
- Row selection and batch actions, expandable row details (`RowDetail`), resizable columns, density, reload, and a Clear button while a search or filter is set.
- Group by: `GroupFields` adds a Group by menu (`?groupBy=` in the URL). Each group gets a collapsible header row and a subtotal row, with totals under the table.
- Multi-level headers (`HeaderGroups`): titled bands over neighbouring columns.
- Row drag and drop (`OnRowMove`): drag the handle, or focus it and press Up, Down, Home or End.

### Large data

`TableQuery<T>` (in `Modulus.Ui.Abstractions.Data`) turns a `ProTableRequest` into an `IQueryable` that your database runs, so a table over millions of rows reads only one page.

```csharp
static readonly TableQuery<Order> Orders = TableQuery<Order>.By(o => o.Id)
    .Sort("date", o => o.Date).Sort("amount", o => o.Amount)
    .SearchText("customer", o => o.Customer)
    .DefaultSort("date", descending: true);

var page = await Orders.PageAsync(db.Orders, request, CountMode.Exact, (q, ct) => q.CountAsync(ct), ct);
```

Only sorts and column searches you register are applied. Numbered pages use `Skip` and `Take`, and the rows after a page use a keyset cursor. `CountMode.None` skips `COUNT(*)`.

For group by and totals, `TableQuery.Group(…)` and `Total(…)` sort by the group first and read each group's count, sums, averages, minima or maxima in one SQL `GROUP BY`.

MVC adds `app-load-more` (infinite scroll) and `app-virtual-table` (only the rows in view are in the page). Blazor uses `Virtualize` over the same query. The sample page `/tables/large` shows all three over 100,000 rows.

## Editable grid

An editable grid's columns are data. Each `GridColumn` has a kind: text, number, currency, percent, select, multi-select, tag, date, switch, progress or rating. Cells edit in place with the matching control and are validated.

- Rows expand into a detail template.
- Columns pin left or right, hide, reorder, resize (drag the header edge), and can be added or removed at run time.
- `GridColumn.Filterable` adds a column to the filter row.
- An initials avatar (`Avatar`), a second line (`SubtitleKey`) or option icons (`GridOption.Icon`) can go in a cell.
- Changed rows and cells are coloured, and so are added rows and the row being edited, until you save.

Row states (`GridRecord.State`) make a row `ReadOnly` (cells fixed) or `Locked` (no edit, delete or move), with a reason shown as a lock or eye mark. `GridError.ForRow` flags a whole row, with `ValidateRow` in Blazor or the `rowRule` of `GridSchema.Validate`.

In MVC, the form reads the posted grid with `EditableGridForm.Read` and writes it back with `Apply`. `GridSchema.Protect` holds a post to the store with the grid's rules.

## Nested grid

A nested grid is one component for any depth. `NestedGridSettings` lists the levels, each with its own columns (or a column set picked by a field of the parent row), search, filters, sort, page size, editing and expand rules. The number of levels is the nesting limit.

Every level has the editable grid's toolbar, selection and export. Searches and filters run on the server (`NestedGridRequest.Filters`). A child table loads the first time its parent row expands (Blazor `Load(NestedGridRequest)`, MVC an htmx GET). Each level saves on its own (`NestedGridChanges`: the page as edited and the deleted ids).

## Pivot grid

`AppPivotGrid` (`app-pivot-grid`) is a pivot in the manner of Odoo's pivot view. You register the dimensions and measures in a `PivotQuery<T>`:

- Dimensions: text, enum, and dates by year, quarter, month, week or day.
- Measures: sum, average, minimum and maximum. Count is built in.

Only the open headers are read. The pivot runs one grouped query per row depth and column depth that has open headers, filtered to the records under those headers. A collapsed header costs one group, and every total comes from the database (averages are sum divided by count). `MaxGroups` caps the work.

The whole layout lives in the URL (`PivotState`), so a pivot can be bookmarked and shared. Every control is a link: expand a header on the next group-by (a date field offers an interval submenu), collapse it, flip the axes, expand all, pick the measures, or sort by one.

Value cells drill down with `CellHref` (`cell-href` in MVC). `PivotXlsx` writes an xlsx download with merged headers and bold totals, with no library. The sample `/tables/pivot` pivots the 100,000 shipments in all three samples.

## Saved views

`AppSavedViews` (`app-saved-views`) is a Views dropdown on a table. A signed-in user names the current layout (its query string), stars one view as the default and deletes views. Views are stored per user in `IUiStateStore`. The default store is in memory, so register your own store to keep views between restarts. The samples use `EfUiStateStore` on SQLite.

MVC and Razor Pages map the endpoint with `app.MapModulusSavedViews()`.

## Field access

`FieldAccess` hides fields by role. Apply it with `TableQuery.Restrict`, `PivotQuery.Restrict` or `GridSchema.Allowed` before the query runs, so the hidden data never leaves the database.

## Keeping state in the URL

A Blazor `ProTable` with `UrlState` keeps its page, sort and column searches in the URL, using the same keys as MVC. A link to a filtered list therefore opens the same view in either engine.

## Where to see them

The three samples show the same pages at the same URLs:

- `/tables/data-grid`: the data table with search, filters, sorting, paging, a column picker, density, selection and CSV export
- `/tables/advanced`: the advanced table, with saved views
- `/tables/editable`: the editable grid
- `/tables/nested`: the nested grid
- `/tables/large`: 100,000 rows with infinite scroll, virtual scrolling and the query
- `/tables/pivot`: the pivot grid with drill-down and xlsx download

Run a sample with `dotnet run --project samples/Modulus.AdminTemplate.Blazor` (http://localhost:5100) or the MVC sample on port 5101.
