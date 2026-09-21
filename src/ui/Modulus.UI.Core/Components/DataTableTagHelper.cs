using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>A column header collected from an <c>&lt;m-column&gt;</c> child.</summary>
/// <param name="Header">Header markup (may be empty for an actions column).</param>
/// <param name="Class">Extra classes for the <c>th</c> (e.g. <c>text-end</c>).</param>
public sealed record DataTableColumn(IHtmlContent Header, string? Class);

/// <summary>View model for the <c>DataTable</c> component.</summary>
/// <param name="Id">Optional element id (a stable htmx swap target).</param>
/// <param name="Columns">Header cells.</param>
/// <param name="Rows">Row markup (<c>&lt;tr&gt;</c> elements written by the page).</param>
/// <param name="IsEmpty">Render <paramref name="EmptyMessage"/> instead of the table.</param>
/// <param name="EmptyMessage">Text for the empty state.</param>
/// <param name="Pagination">Pager model, or null when the table is not paged.</param>
/// <param name="Source">Endpoint returning rendered <c>&lt;tr&gt;</c> fragments that replace the rows, or null.</param>
/// <param name="RefreshTrigger">The <c>hx-trigger</c> value that re-queries <paramref name="Source"/>.</param>
public sealed record DataTableModel(
    string? Id,
    IReadOnlyList<DataTableColumn> Columns,
    IHtmlContent Rows,
    bool IsEmpty,
    string? EmptyMessage,
    PaginationModel? Pagination,
    string? Source = null,
    string? RefreshTrigger = null);

/// <summary>Collects <c>m-column</c> children for their <c>m-datatable</c> parent.</summary>
internal sealed class DataTableContext
{
    public List<DataTableColumn> Columns { get; } = [];

    /// <summary>Where <c>&lt;m-entity-columns /&gt;</c> sat among the declared columns; null = after the last one.</summary>
    public int? EntityColumnsIndex { get; set; }
}

/// <summary>
/// <c>&lt;m-datatable&gt;</c> — the card + responsive table shell every list page repeats. The page
/// declares headers with <c>&lt;m-column&gt;</c> children and writes its own <c>&lt;tr&gt;</c> rows, so
/// cell markup (links, badges, row actions) stays fully in the page's hands:
/// <code>
/// &lt;m-datatable empty="@(Model.Rows.Count == 0)" empty-message="No users" page="@Model.Page" has-next="@Model.HasNext"&gt;
///     &lt;m-column&gt;Name&lt;/m-column&gt;&lt;m-column class="text-end" /&gt;
///     @foreach (var row in Model.Rows) { &lt;tr&gt;…&lt;/tr&gt; }
/// &lt;/m-datatable&gt;
/// </code>
/// Set <c>page</c> to add the pager; <c>route-Name="value"</c> layers extra values onto its links.
/// <para>
/// Set <c>source</c> to a handler that returns rendered <c>&lt;tr&gt;</c> fragments and the rows refresh
/// themselves: <c>refresh-on="product:changed"</c> re-queries whenever a response announces that entity with
/// <c>HtmxResponse.NotifyChanged</c> (comma-separate several). Without <c>refresh-on</c> the rows load once
/// on page load. The table shell renders even when empty so a later refresh has somewhere to land; the
/// pager is not refreshed, so keep source grids to unpaged or single-page lists.
/// </para>
/// <para>
/// Set <c>entity="Catalog.Product"</c> to add the columns other modules contributed to that entity
/// (<c>ConfigureEntityUi</c>): their headers go where <c>&lt;m-entity-columns /&gt;</c> sits among the
/// <c>m-column</c>s (default: after the last), and each row writes the matching cells with
/// <c>&lt;m-entity-cells&gt;</c> at the same position.
/// </para>
/// </summary>
[HtmlTargetElement("m-datatable")]
public sealed partial class DataTableTagHelper : ComponentTagHelper
{
    /// <summary>Optional element id.</summary>
    [HtmlAttributeName("id")]
    public string? Id { get; set; }

    /// <summary>When true, render <see cref="EmptyMessage"/> instead of the table.</summary>
    [HtmlAttributeName("empty")]
    public bool IsEmpty { get; set; }

    /// <summary>Empty-state text.</summary>
    [HtmlAttributeName("empty-message")]
    public string? EmptyMessage { get; set; }

    /// <summary>Current page. Setting it enables the pager footer.</summary>
    [HtmlAttributeName("page")]
    public int? Page { get; set; }

    /// <summary>Show a previous-page link.</summary>
    [HtmlAttributeName("has-previous")]
    public bool HasPrevious { get; set; }

    /// <summary>Show a next-page link.</summary>
    [HtmlAttributeName("has-next")]
    public bool HasNext { get; set; }

    /// <summary>Query key carrying the page number. Default <c>PageNumber</c>.</summary>
    [HtmlAttributeName("page-param")]
    public string PageParam { get; set; } = "PageNumber";

    /// <summary>Text before the page number in the pager. Default <c>Page</c>.</summary>
    [HtmlAttributeName("page-label")]
    public string PageLabel { get; set; } = "Page";

    /// <summary>Endpoint returning <c>&lt;tr&gt;</c> fragments that replace the rows (see the class remarks).</summary>
    [HtmlAttributeName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Entity events (<c>product:changed</c>, comma-separated) that re-query <see cref="Source"/>.
    /// Each must end with <c>:changed</c>, the suffix <c>HtmxResponse.NotifyChanged</c> enforces.
    /// </summary>
    [HtmlAttributeName("refresh-on")]
    public string? RefreshOn { get; set; }

    /// <summary>Registry key of the entity whose contributed columns get headers (<c>Catalog.Product</c>).</summary>
    [HtmlAttributeName("entity")]
    public string? Entity { get; set; }

    /// <summary>Extra query values for pager links (<c>route-UnreadOnly="true"</c>).</summary>
    [HtmlAttributeName("route", DictionaryAttributePrefix = "route-")]
    public IDictionary<string, string?> RouteValues { get; set; } = new Dictionary<string, string?>();

    /// <inheritdoc />
    protected override string Component => "DataTable";

    /// <inheritdoc />
    public override void Init(TagHelperContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[typeof(DataTableContext)] = new DataTableContext();
    }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var rows = await output.GetChildContentAsync();
        var table = (DataTableContext)context.Items[typeof(DataTableContext)];
        var columns = table.Columns.ToList();
        if (!string.IsNullOrWhiteSpace(Entity))
        {
            var services = ViewContext.HttpContext.RequestServices;
            var contributed = services.GetRequiredService<IEntityUiRegistry>()
                .GetVisibleColumns(Entity, services.GetRequiredService<ICurrentUser>())
                .Select(c => new DataTableColumn(new HtmlContentBuilder().Append(c.Label), c.CssClass));
            columns.InsertRange(Math.Min(table.EntityColumnsIndex ?? columns.Count, columns.Count), contributed);
        }

        var pagination = Page is { } page
            ? PaginationTagHelper.Create(
                ViewContext.HttpContext.Request, PageLabel, page, HasPrevious, HasNext, PageParam, RouteValues)
            : null;

        await RenderAsync(
            output,
            new DataTableModel(Id, columns, rows, IsEmpty, EmptyMessage, pagination, Source, BuildRefreshTrigger()));
    }

    private string? BuildRefreshTrigger()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            if (!string.IsNullOrWhiteSpace(RefreshOn))
            {
                throw new InvalidOperationException("<m-datatable refresh-on> needs a source to re-query.");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(RefreshOn))
        {
            return "load";
        }

        var events = RefreshOn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var name in events)
        {
            if (!EntityEvent().IsMatch(name))
            {
                throw new InvalidOperationException(
                    $"<m-datatable refresh-on> entries must look like 'product:changed', got '{name}'.");
            }
        }

        return string.Join(", ", events.Select(e => $"{e} from:body"));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+:changed$")]
    private static partial Regex EntityEvent();
}

/// <summary>A header cell of an <c>m-datatable</c>; the element's content is the header text.</summary>
[HtmlTargetElement("m-column", ParentTag = "m-datatable")]
public sealed class DataTableColumnTagHelper : TagHelper
{
    /// <summary>Extra classes for the header cell (e.g. <c>text-end</c>).</summary>
    [HtmlAttributeName("class")]
    public string? Class { get; set; }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var header = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(typeof(DataTableContext), out var parent) && parent is DataTableContext table)
        {
            table.Columns.Add(new DataTableColumn(header, Class));
        }

        output.SuppressOutput();
    }
}

/// <summary>
/// <c>&lt;m-entity-columns /&gt;</c> — marks where an <c>m-datatable entity="..."</c> puts the columns other modules
/// contributed (among the <c>m-column</c>s). Write the row's <c>&lt;m-entity-cells&gt;</c> at the same position.
/// </summary>
[HtmlTargetElement("m-entity-columns", ParentTag = "m-datatable", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DataTableEntityColumnsTagHelper : TagHelper
{
    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(DataTableContext), out var parent) && parent is DataTableContext table)
        {
            table.EntityColumnsIndex = table.Columns.Count;
        }

        output.SuppressOutput();
    }
}
