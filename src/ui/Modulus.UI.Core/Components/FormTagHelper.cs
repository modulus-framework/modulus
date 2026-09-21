using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>View model for the <c>Form</c> component.</summary>
/// <param name="Action">POST target (also the no-JavaScript fallback).</param>
/// <param name="HxTarget">htmx target selector, or null for a plain post.</param>
/// <param name="HxSwap">htmx swap strategy (used with <paramref name="HxTarget"/>).</param>
/// <param name="ShowSummary">Render the validation summary when the model state is invalid.</param>
/// <param name="Class">Extra classes for the <c>form</c>.</param>
/// <param name="Body">Form body markup.</param>
public sealed record FormModel(
    string? Action,
    string? HxTarget,
    string HxSwap,
    bool ShowSummary,
    string? Class,
    IHtmlContent Body)
{
    /// <summary>Post as <c>multipart/form-data</c> (plain and htmx) so the form can carry files.</summary>
    public bool Multipart { get; init; }
}

/// <summary>
/// <c>&lt;m-form handler="Save" target="#form-region"&gt;…&lt;/m-form&gt;</c> — a POST form that works as a
/// plain form without JavaScript and is enhanced with htmx when a <c>target</c> (or <c>modal</c>) is
/// set. Adds the antiforgery token and, on a 422 re-render, the validation summary, so pages never
/// repeat that plumbing. The action is the current page's handler URL unless <c>action</c> overrides it.
/// </summary>
[HtmlTargetElement("m-form")]
public sealed class ModulusFormTagHelper : ComponentTagHelper
{
    private const string ModalTarget = "#m-modal-container";

    /// <summary>Razor Pages handler name (<c>?handler=Save</c>) on the current page.</summary>
    [HtmlAttributeName("handler")]
    public string? Handler { get; set; }

    /// <summary>Another Razor Page to post to (default: the current page).</summary>
    [HtmlAttributeName("page")]
    public string? Page { get; set; }

    /// <summary>Explicit action URL; overrides <c>page</c>/<c>handler</c>.</summary>
    [HtmlAttributeName("action")]
    public string? Action { get; set; }

    /// <summary>Route values for the generated URL (<c>route-id="@Model.Id"</c>).</summary>
    [HtmlAttributeName("route", DictionaryAttributePrefix = "route-")]
    public IDictionary<string, string?> RouteValues { get; set; } = new Dictionary<string, string?>();

    /// <summary>htmx target selector for the response (e.g. <c>#settings-form</c>).</summary>
    [HtmlAttributeName("target")]
    public string? Target { get; set; }

    /// <summary>Post into the shared modal container (<c>#m-modal-container</c>).</summary>
    [HtmlAttributeName("modal")]
    public bool Modal { get; set; }

    /// <summary>htmx swap strategy. Default <c>innerHTML</c>.</summary>
    [HtmlAttributeName("swap")]
    public string Swap { get; set; } = "innerHTML";

    /// <summary>Show the validation summary when the model state is invalid. Default true.</summary>
    [HtmlAttributeName("summary")]
    public bool Summary { get; set; } = true;

    /// <summary>Post as <c>multipart/form-data</c>, plain and htmx (needed for <c>m-file</c>).</summary>
    [HtmlAttributeName("multipart")]
    public bool Multipart { get; set; }

    /// <summary>Extra classes for the <c>form</c> element.</summary>
    [HtmlAttributeName("class")]
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override string Component => "Form";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = await output.GetChildContentAsync();
        var target = Target ?? (Modal ? ModalTarget : null);
        await RenderAsync(output, new FormModel(ResolveAction(), target, Swap, Summary, Class, body) { Multipart = Multipart });
    }

    private string? ResolveAction()
    {
        if (!string.IsNullOrWhiteSpace(Action))
        {
            return Action;
        }

        var url = ViewContext.HttpContext.RequestServices
            .GetRequiredService<IUrlHelperFactory>()
            .GetUrlHelper(ViewContext);
        var values = RouteValues.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => (object?)v.Value);
        return url.Page(Page, Handler, values);
    }
}
