using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>One contributed cell.</summary>
/// <param name="Text">Display text; empty renders a dash.</param>
/// <param name="Class">Extra classes for the <c>td</c>.</param>
public sealed record EntityCell(string? Text, string? Class);

/// <summary>View model for the <c>EntityCells</c> component.</summary>
/// <param name="Cells">One cell per visible contributed column, in header order.</param>
public sealed record EntityCellsModel(IReadOnlyList<EntityCell> Cells);

/// <summary>
/// <c>&lt;m-entity-cells entity="Catalog.Product" row-id="@p.Id" values="Model.Extra" /&gt;</c> — inside a
/// <c>&lt;tr&gt;</c>, the cells for the columns other modules contributed, matching the headers
/// <c>m-datatable entity="..."</c> added (same position: where <c>&lt;m-entity-columns /&gt;</c> sits). <c>values</c> is what
/// <see cref="EntityColumnLoader.LoadEntityColumnsAsync"/> returned for the page's rows; without it (or without an entry
/// for the row) the cells render a dash, so the table stays aligned. Markup lives in the overridable
/// <c>EntityCells/Default</c> view.
/// </summary>
[HtmlTargetElement("m-entity-cells")]
public sealed class EntityCellsTagHelper : ComponentTagHelper
{
    /// <summary>Registry key of the entity.</summary>
    [HtmlAttributeName("entity")]
    public string? Entity { get; set; }

    /// <summary>The row's id, as passed to the loader.</summary>
    [HtmlAttributeName("row-id")]
    public string? RowId { get; set; }

    /// <summary>The loaded values for the page.</summary>
    [HtmlAttributeName("values")]
    public EntityColumnValues? Values { get; set; }

    /// <inheritdoc />
    protected override string Component => "EntityCells";

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var entity = !string.IsNullOrWhiteSpace(Entity)
            ? Entity
            : throw new InvalidOperationException("<m-entity-cells> requires an entity=\"...\" attribute.");
        var rowId = RowId ?? throw new InvalidOperationException("<m-entity-cells> requires a row-id=\"...\" attribute.");

        var services = ViewContext.HttpContext.RequestServices;
        var cells = services.GetRequiredService<IEntityUiRegistry>()
            .GetVisibleColumns(entity, services.GetRequiredService<ICurrentUser>())
            .Select(c => new EntityCell(Values?.Get(c.Name, rowId), c.CssClass))
            .ToList();

        return RenderAsync(output, new EntityCellsModel(cells));
    }
}

/// <summary>One rendered action button or link.</summary>
/// <param name="Label">Visible text.</param>
/// <param name="Css">Button colour classes.</param>
/// <param name="Url">Link target (a link), or null.</param>
/// <param name="HxGet">htmx GET url (a button), or null.</param>
/// <param name="HxPost">htmx POST url (a button), or null.</param>
/// <param name="HxTarget">Where an htmx response lands (a CSS selector, <c>closest tr</c> for a row).</param>
/// <param name="HxSwap">The htmx swap style.</param>
public sealed record EntityActionLink(string Label, string Css, string? Url, string? HxGet, string? HxPost, string HxTarget, string HxSwap);

/// <summary>View model for the <c>EntityActions</c> component.</summary>
/// <param name="Actions">Permission-filtered, ordered actions for the row, with <c>{id}</c> already expanded.</param>
public sealed record EntityActionsModel(IReadOnlyList<EntityActionLink> Actions);

/// <summary>
/// <c>&lt;m-entity-actions entity="Catalog.Product" row-id="@p.Id" /&gt;</c> — in a row, the buttons other modules
/// contributed to that entity (<c>ConfigureEntityUi</c>), already filtered by the current user's permissions. <c>{id}</c> in
/// an action's URL becomes the row id. Renders nothing when there are none. Markup lives in the overridable
/// <c>EntityActions/Default</c> view.
/// </summary>
[HtmlTargetElement("m-entity-actions")]
public sealed class EntityActionsTagHelper : ComponentTagHelper
{
    /// <summary>Registry key of the entity.</summary>
    [HtmlAttributeName("entity")]
    public string? Entity { get; set; }

    /// <summary>The row's id.</summary>
    [HtmlAttributeName("row-id")]
    public string? RowId { get; set; }

    /// <inheritdoc />
    protected override string Component => "EntityActions";

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var entity = !string.IsNullOrWhiteSpace(Entity)
            ? Entity
            : throw new InvalidOperationException("<m-entity-actions> requires an entity=\"...\" attribute.");
        var rowId = RowId ?? throw new InvalidOperationException("<m-entity-actions> requires a row-id=\"...\" attribute.");

        var services = ViewContext.HttpContext.RequestServices;
        var actions = services.GetRequiredService<IEntityUiRegistry>()
            .GetVisibleActions(entity, services.GetRequiredService<ICurrentUser>())
            .Select(a => new EntityActionLink(
                a.Label,
                a.CssClass ?? "btn-outline-secondary",
                a.Url is null ? null : EntityAction.Expand(a.Url, rowId),
                a.HxGet is null ? null : EntityAction.Expand(a.HxGet, rowId),
                a.HxPost is null ? null : EntityAction.Expand(a.HxPost, rowId),
                a.Target == EntityActionTarget.Row ? "closest tr" : "#m-modal-container",
                a.Target == EntityActionTarget.Row ? "outerHTML" : "innerHTML"))
            .ToList();

        if (actions.Count == 0)
        {
            output.SuppressOutput();
            return Task.CompletedTask;
        }

        return RenderAsync(output, new EntityActionsModel(actions));
    }
}
