using System.Collections;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>One rendered row of an <c>m-line-items</c> collection.</summary>
/// <param name="Index">Zero-based position; the row's fields are named <c>{collection}[Index].Member</c>.</param>
/// <param name="Content">The row partial rendered for this item.</param>
public sealed record LineItemRow(int Index, IHtmlContent Content);

/// <summary>View model for the <c>LineItems</c> component.</summary>
/// <param name="NamePrefix">Posted name of the collection (<c>Input.Lines</c>); rows are <c>NamePrefix[i]</c>.</param>
/// <param name="IdPrefix">The sanitised form of <paramref name="NamePrefix"/> used in element ids.</param>
/// <param name="Label">Visible label of the group.</param>
/// <param name="Rows">The existing rows, in order.</param>
/// <param name="Template">A blank row whose index is <see cref="ModulusLineItemsTagHelper.IndexPlaceholder"/>; the client clones it for "add".</param>
/// <param name="Errors">Validation messages for the collection itself (not for a row's field).</param>
public sealed record LineItemsModel(
    string NamePrefix,
    string IdPrefix,
    string Label,
    IReadOnlyList<LineItemRow> Rows,
    IHtmlContent Template,
    IReadOnlyList<string> Errors)
{
    /// <summary>Muted help text under the rows.</summary>
    public string? Hint { get; init; }

    /// <summary>Text of the add button.</summary>
    public string AddLabel { get; init; } = "Add line";

    /// <summary>Accessible name of each row's remove button.</summary>
    public string RemoveLabel { get; init; } = "Remove";

    /// <summary>True when the collection has validation errors.</summary>
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// <c>&lt;m-line-items for="Input.Lines" row-partial="_LineRow" add-label="Add line" /&gt;</c> — editable child
/// rows (invoice lines, a bill of materials). The row partial is an ordinary partial whose model is one element
/// and whose fields are ordinary <c>m-input</c>s bound to the element (<c>for="Sku"</c>); the tag helper renders it
/// once per item with the element's index in the field path (so the collection model-binds as
/// <c>Input.Lines[0].Sku</c>, and a 422 re-render puts each row's posted values and errors back) and once more,
/// blank, as a <c>&lt;template&gt;</c> that the <c>mLineItems</c> Alpine component clones for "add". Removing a row
/// renumbers the rest, so indexes stay contiguous and the default collection binder needs nothing special.
/// Markup lives in the overridable <c>LineItems/Default</c> view.
/// </summary>
[HtmlTargetElement("m-line-items")]
public sealed class ModulusLineItemsTagHelper : ComponentTagHelper
{
    /// <summary>Stands in for the row index inside the blank template; the client swaps in the real index.</summary>
    public const string IndexPlaceholder = "__index__";

    /// <summary>The collection member (<c>for="Input.Lines"</c>).</summary>
    [HtmlAttributeName("for")]
    public ModelExpression? For { get; set; }

    /// <summary>The partial that renders one row; its model is the collection's element type.</summary>
    [HtmlAttributeName("row-partial")]
    public string? RowPartial { get; set; }

    /// <summary>Label of the group; defaults to the member's display name.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Muted help text under the rows.</summary>
    [HtmlAttributeName("hint")]
    public string? Hint { get; set; }

    /// <summary>Text of the add button.</summary>
    [HtmlAttributeName("add-label")]
    public string AddLabel { get; set; } = "Add line";

    /// <summary>Accessible name of each row's remove button.</summary>
    [HtmlAttributeName("remove-label")]
    public string RemoveLabel { get; set; } = "Remove";

    /// <inheritdoc />
    protected override string Component => "LineItems";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var expression = For ?? throw new InvalidOperationException("<m-line-items> requires a for=\"...\" model expression.");
        var partial = !string.IsNullOrWhiteSpace(RowPartial)
            ? RowPartial
            : throw new InvalidOperationException("<m-line-items> requires a row-partial=\"...\" attribute.");
        var elementType = expression.Metadata.ElementType
            ?? throw new InvalidOperationException(
                $"<m-line-items for=\"{expression.Name}\"> must bind a collection, but {expression.Metadata.ModelType} is not one.");

        var name = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name);

        var rows = new List<LineItemRow>();
        if (expression.Model is IEnumerable items)
        {
            foreach (var item in items)
            {
                var index = rows.Count;
                rows.Add(new LineItemRow(index, await RenderRowAsync(partial, item, $"{name}[{index}]")));
            }
        }

        var template = await RenderRowAsync(partial, CreateBlank(elementType), $"{name}[{IndexPlaceholder}]");

        var errors = ViewContext.ModelState.TryGetValue(name, out var entry)
            ? entry.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage).ToList()
            : [];

        var model = new LineItemsModel(
            name,
            TagBuilder.CreateSanitizedId(name, "_"),
            Label ?? expression.Metadata.DisplayName ?? expression.Metadata.PropertyName ?? name,
            rows,
            template,
            errors)
        {
            Hint = Hint,
            AddLabel = AddLabel,
            RemoveLabel = RemoveLabel,
        };

        await RenderAsync(output, model);
    }

    /// <summary>Renders the row partial for <paramref name="item"/> with <paramref name="prefix"/> as its field path.</summary>
    private async Task<IHtmlContent> RenderRowAsync(string partial, object? item, string prefix)
    {
        var services = ViewContext.HttpContext.RequestServices;
        var html = services.GetRequiredService<IHtmlHelper>();
        (html as IViewContextAware)?.Contextualize(ViewContext);

        // Copying the page's ViewData would keep its model type (the page), which the row's model is not,
        // so start from an untyped dictionary that shares the page's model state (rows read their errors from it).
        var viewData = new ViewDataDictionary(services.GetRequiredService<IModelMetadataProvider>(), ViewContext.ViewData.ModelState)
        {
            Model = item,
        };
        foreach (var (key, value) in ViewContext.ViewData)
        {
            viewData[key] = value;
        }

        viewData.TemplateInfo.HtmlFieldPrefix = prefix;
        return await html.PartialAsync(partial, item, viewData);
    }

    /// <summary>A default element for the blank row, or null when the type has no parameterless constructor.</summary>
    private static object? CreateBlank(Type elementType)
        => elementType.IsValueType || elementType.GetConstructor(Type.EmptyTypes) is not null
            ? Activator.CreateInstance(elementType)
            : null;
}
