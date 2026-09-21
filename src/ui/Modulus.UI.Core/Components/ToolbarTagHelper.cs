using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>View model for the <c>Toolbar</c> component.</summary>
/// <param name="Items">Permission-filtered, ordered toolbar items.</param>
public sealed record ToolbarModel(IReadOnlyList<ToolbarItem> Items);

/// <summary>
/// <c>&lt;m-toolbar page-id="Catalog.Products.Index" /&gt;</c> — the buttons modules contributed to a
/// page through <see cref="IToolbarContributor"/>, already filtered by the current user's permissions.
/// <c>page-id</c> defaults to the page's <c>ViewData.SetPageId(...)</c>. Renders nothing when there
/// are no items.
/// </summary>
[HtmlTargetElement("m-toolbar")]
public sealed class ToolbarTagHelper : ComponentTagHelper
{
    /// <summary>Page id to resolve contributors for (default: the page's <c>SetPageId</c>).</summary>
    [HtmlAttributeName("page-id")]
    public string? PageId { get; set; }

    /// <inheritdoc />
    protected override string Component => "Toolbar";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var pageId = PageId ?? ViewContext.ViewData.GetPageId();
        var items = pageId is null
            ? []
            : ViewContext.HttpContext.RequestServices.GetRequiredService<IToolbarProvider>().GetItems(pageId);

        if (items.Count == 0)
        {
            output.SuppressOutput();
            return;
        }

        await RenderAsync(output, new ToolbarModel(items));
    }
}
