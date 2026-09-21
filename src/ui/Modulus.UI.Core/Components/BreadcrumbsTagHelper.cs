using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Modulus.UI;

/// <summary>View model for the <c>Breadcrumbs</c> component.</summary>
/// <param name="Items">The trail, root first; the last item is the current page.</param>
public sealed record BreadcrumbsModel(IReadOnlyList<BreadcrumbItem> Items);

/// <summary>
/// <c>&lt;m-breadcrumbs /&gt;</c> — renders the trail from, in order: the <c>items</c> attribute, a trail
/// the page set with <c>ViewData.SetBreadcrumbs(...)</c>, or the registered
/// <see cref="IBreadcrumbContributor"/>s for the page id (<c>page-id</c> or
/// <c>ViewData.SetPageId(...)</c>). Renders nothing when there is no trail or
/// <c>Modulus:Ui:Features:Breadcrumbs</c> is off, so layouts can include it unconditionally.
/// </summary>
[HtmlTargetElement("m-breadcrumbs")]
public sealed class BreadcrumbsTagHelper : ComponentTagHelper
{
    /// <summary>Explicit trail (wins over contributors).</summary>
    [HtmlAttributeName("items")]
    public IEnumerable<BreadcrumbItem>? Items { get; set; }

    /// <summary>Page id to resolve contributors for (default: the page's <c>SetPageId</c>).</summary>
    [HtmlAttributeName("page-id")]
    public string? PageId { get; set; }

    /// <inheritdoc />
    protected override string Component => "Breadcrumbs";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var services = ViewContext.HttpContext.RequestServices;
        if (!services.GetRequiredService<IOptions<UiOptions>>().Value.Features.Breadcrumbs)
        {
            output.SuppressOutput();
            return;
        }

        IReadOnlyList<BreadcrumbItem> trail;
        if (Items is not null)
        {
            trail = Items.ToList();
        }
        else if (ViewContext.ViewData.GetBreadcrumbs() is { } explicitTrail)
        {
            trail = explicitTrail;
        }
        else if ((PageId ?? ViewContext.ViewData.GetPageId()) is { } pageId)
        {
            trail = services.GetRequiredService<IBreadcrumbProvider>().GetItems(pageId);
        }
        else
        {
            trail = [];
        }

        if (trail.Count == 0)
        {
            output.SuppressOutput();
            return;
        }

        await RenderAsync(output, new BreadcrumbsModel(trail));
    }
}
