using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>
/// Standard Tabler page header (<c>&lt;m-page-header title="..." subtitle="..."&gt;actions&lt;/m-page-header&gt;</c>).
/// Collapses the header block every list/details page repeats: title, optional
/// subtitle, and an actions slot (buttons, forms) pinned right via
/// <c>col-auto ms-auto</c>. The actions slot renders only when it has
/// non-whitespace child content, so pages without actions emit no empty column.
/// Set <c>page-id</c> to also render the buttons modules contributed to that page
/// (<see cref="IToolbarContributor"/>, permission-filtered) after the child actions.
/// Title/subtitle are HTML-encoded; actions child content passes through as-is.
/// </summary>
/// <example>
/// <code>
/// &lt;m-page-header title="Users" subtitle="Acme Corp"&gt;
///     &lt;a class="btn btn-primary" asp-page="./Create"&gt;New&lt;/a&gt;
/// &lt;/m-page-header&gt;
/// </code>
/// </example>
[HtmlTargetElement("m-page-header")]
public sealed class PageHeaderTagHelper : TagHelper
{
    /// <summary>Page title (required; usually <c>ViewData["Title"]</c>).</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Optional secondary line under the title.</summary>
    [HtmlAttributeName("subtitle")]
    public string? Subtitle { get; set; }

    /// <summary>Page id whose contributed toolbar items are appended to the actions.</summary>
    [HtmlAttributeName("page-id")]
    public string? PageId { get; set; }

    /// <summary>The executing view's context (set by the framework; only needed for <c>page-id</c>).</summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext? ViewContext { get; set; }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = "div";
        // Pages write <m-page-header title="..." />; content is dropped in self-closing mode,
        // which used to render an empty <div /> with no title at all.
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "page-header d-print-none");

        var child = await output.GetChildContentAsync();
        var toolbar = await RenderToolbarAsync();

        output.Content.AppendHtml("<div class=\"row align-items-center\"><div class=\"col\">");
        output.Content.AppendHtml("<h2 class=\"page-title\">");
        output.Content.Append(Title ?? string.Empty);
        output.Content.AppendHtml("</h2>");
        if (!string.IsNullOrWhiteSpace(Subtitle))
        {
            output.Content.AppendHtml("<div class=\"text-secondary mt-1\">");
            output.Content.Append(Subtitle);
            output.Content.AppendHtml("</div>");
        }

        output.Content.AppendHtml("</div>");
        if (!child.IsEmptyOrWhiteSpace || toolbar is not null)
        {
            output.Content.AppendHtml("<div class=\"col-auto ms-auto\">");
            output.Content.AppendHtml(child);
            if (toolbar is not null)
            {
                output.Content.AppendHtml(toolbar);
            }

            output.Content.AppendHtml("</div>");
        }

        output.Content.AppendHtml("</div>");
    }

    private async Task<IHtmlContent?> RenderToolbarAsync()
    {
        if (string.IsNullOrWhiteSpace(PageId) || ViewContext is null)
        {
            return null;
        }

        var items = ViewContext.HttpContext.RequestServices.GetRequiredService<IToolbarProvider>().GetItems(PageId);
        return items.Count == 0
            ? null
            : await ComponentRenderer.RenderAsync(ViewContext, "Toolbar", new ToolbarModel(items));
    }
}
