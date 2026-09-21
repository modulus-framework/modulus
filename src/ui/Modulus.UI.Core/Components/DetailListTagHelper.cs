using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>One label/value pair of a <c>DetailList</c>.</summary>
/// <param name="Label">Caption.</param>
/// <param name="Value">Value markup.</param>
/// <param name="IsEmpty">True when there is no value (the view shows a muted dash).</param>
public sealed record DetailItem(string? Label, IHtmlContent Value, bool IsEmpty);

/// <summary>View model for the <c>DetailList</c> component.</summary>
/// <param name="Items">The pairs, in order.</param>
public sealed record DetailListModel(IReadOnlyList<DetailItem> Items);

/// <summary>Collects <c>m-detail</c> children for their <c>m-detail-list</c> parent.</summary>
internal sealed class DetailListContext
{
    public List<DetailItem> Items { get; } = [];
}

/// <summary>
/// <c>&lt;m-detail-list&gt;&lt;m-detail label="Email" value="@Model.Email" /&gt;&lt;/m-detail-list&gt;</c> — a
/// read-only label/value list for detail pages. A value is either the <c>value</c> attribute (encoded)
/// or the element's content (markup). Markup lives in the overridable <c>DetailList/Default</c> view.
/// </summary>
[HtmlTargetElement("m-detail-list")]
public sealed class DetailListTagHelper : ComponentTagHelper
{
    /// <inheritdoc />
    protected override string Component => "DetailList";

    /// <inheritdoc />
    public override void Init(TagHelperContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[typeof(DetailListContext)] = new DetailListContext();
    }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        await output.GetChildContentAsync();
        var items = ((DetailListContext)context.Items[typeof(DetailListContext)]).Items;
        await RenderAsync(output, new DetailListModel(items));
    }
}

/// <summary>A pair of an <c>m-detail-list</c>.</summary>
[HtmlTargetElement("m-detail", ParentTag = "m-detail-list")]
public sealed class DetailTagHelper : TagHelper
{
    /// <summary>Caption.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Plain-text value (HTML-encoded); when absent the element's content is the value.</summary>
    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var child = await output.GetChildContentAsync();
        if (context.Items.TryGetValue(typeof(DetailListContext), out var parent) && parent is DetailListContext list)
        {
            list.Items.Add(!string.IsNullOrWhiteSpace(Value)
                ? new DetailItem(Label, new HtmlContentBuilder().Append(Value), false)
                : new DetailItem(Label, child, child.IsEmptyOrWhiteSpace));
        }

        output.SuppressOutput();
    }
}
