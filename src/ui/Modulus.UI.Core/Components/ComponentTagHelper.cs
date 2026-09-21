using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>
/// Renders a component's markup through the overridable view chain (app
/// <c>/Views/Shared/Modulus/{Component}/{View}.cshtml</c> → active theme →
/// framework <c>_Default</c>, see <see cref="IModulusViewResolver"/>). Tag helpers
/// hold no markup: they build a view model and hand it to <see cref="RenderAsync"/>.
/// </summary>
public static class ComponentRenderer
{
    /// <summary>Renders <paramref name="component"/>'s <paramref name="view"/> with <paramref name="model"/>.</summary>
    public static async Task<IHtmlContent> RenderAsync(
        ViewContext viewContext,
        string component,
        object model,
        string view = "Default")
    {
        ArgumentNullException.ThrowIfNull(viewContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentNullException.ThrowIfNull(model);

        var services = viewContext.HttpContext.RequestServices;
        var path = services.GetRequiredService<IModulusViewResolver>().Resolve(component, view);

        var html = services.GetRequiredService<IHtmlHelper>();
        (html as IViewContextAware)?.Contextualize(viewContext);
        return await html.PartialAsync(path, model);
    }
}

/// <summary>Base class for <c>m-*</c> tag helpers that render a component partial.</summary>
public abstract class ComponentTagHelper : TagHelper
{
    /// <summary>The executing view's context (set by the framework).</summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary>Component folder name used by view resolution (e.g. <c>DataTable</c>).</summary>
    protected abstract string Component { get; }

    /// <summary>Replaces the tag with the component's rendered partial (no wrapper element).</summary>
    protected async Task RenderAsync(TagHelperOutput output, object model, string view = "Default")
    {
        ArgumentNullException.ThrowIfNull(output);
        var html = await ComponentRenderer.RenderAsync(ViewContext, Component, model, view);
        output.TagName = null;
        output.Content.SetHtmlContent(html);
    }
}
