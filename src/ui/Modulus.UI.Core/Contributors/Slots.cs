using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// Contributes markup to a named layout slot (see <see cref="Theming.UiSlots"/>).
/// Registered via <c>AddSlotContributor&lt;T&gt;()</c> (scoped — resolve
/// request services inside <see cref="RenderAsync"/>). Themes render slots via
/// <c>Html.ModulusSlotAsync(slot)</c>; contributions are ordered by
/// <see cref="Order"/> and hidden when <see cref="RequiredPermission"/> is set
/// and the current user lacks it.
/// </summary>
public interface ISlotContributor
{
    /// <summary>Slot this contributor renders into (a <see cref="Theming.UiSlots"/> name).</summary>
    string Slot { get; }

    /// <summary>Sort key within the slot (ascending).</summary>
    int Order => 0;

    /// <summary>Hidden unless the current user has this permission.</summary>
    string? RequiredPermission => null;

    /// <summary>Renders the contribution. Return null to contribute nothing.</summary>
    ValueTask<IHtmlContent?> RenderAsync(SlotContext context, CancellationToken cancellationToken = default);
}

/// <summary>Context passed to <see cref="ISlotContributor.RenderAsync"/>.</summary>
/// <param name="viewContext">The executing view's context (page/view helpers, request).</param>
public sealed class SlotContext(ViewContext viewContext)
{
    /// <summary>The executing view's context.</summary>
    public ViewContext ViewContext { get; } = viewContext ?? throw new ArgumentNullException(nameof(viewContext));

    /// <summary>Request services.</summary>
    public IServiceProvider Services => ViewContext.HttpContext.RequestServices;

    /// <summary>Renders a view component (contextualized to the current view).</summary>
    public Task<IHtmlContent> ViewComponentAsync(string componentName, object? arguments = null)
    {
        var helper = Services.GetRequiredService<IViewComponentHelper>();
        (helper as IViewContextAware)?.Contextualize(ViewContext);
        return helper.InvokeAsync(componentName, arguments);
    }
}

/// <summary>Read-only, permission-filtered renderer for layout slots.</summary>
public interface ISlotRenderer
{
    /// <summary>
    /// Renders every contribution for <paramref name="slot"/> in order, or
    /// null when the slot has no visible content.
    /// </summary>
    ValueTask<IHtmlContent?> RenderAsync(string slot, ViewContext viewContext, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="ISlotRenderer"/>.</summary>
public sealed class SlotRenderer(
    IEnumerable<ISlotContributor> contributors,
    ICurrentUser currentUser) : ISlotRenderer
{
    private readonly IReadOnlyList<ISlotContributor> _contributors =
        (contributors ?? throw new ArgumentNullException(nameof(contributors))).ToList();

    private readonly ICurrentUser _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public async ValueTask<IHtmlContent?> RenderAsync(
        string slot,
        ViewContext viewContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(viewContext);

        var applicable = _contributors
            .Where(c => string.Equals(c.Slot, slot, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.RequiredPermission is null || _currentUser.HasPermission(c.RequiredPermission))
            .OrderBy(c => c.Order)
            .ToList();

        if (applicable.Count == 0)
            return null;

        var output = new HtmlContentBuilder();
        var context = new SlotContext(viewContext);
        foreach (var contributor in applicable)
        {
            var content = await contributor.RenderAsync(context, cancellationToken);
            if (content is not null)
                output.AppendHtml(content);
        }

        return output;
    }
}

/// <summary>Razor helper for rendering a named slot inside a theme layout.</summary>
public static class SlotRendererHtmlExtensions
{
    /// <summary>Renders every visible contribution for <paramref name="slot"/> (empty when none).</summary>
    public static async Task<IHtmlContent> ModulusSlotAsync(
        this IHtmlHelper htmlHelper,
        string slot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(htmlHelper);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);

        var renderer = htmlHelper.ViewContext.HttpContext.RequestServices.GetRequiredService<ISlotRenderer>();
        var content = await renderer.RenderAsync(slot, htmlHelper.ViewContext, cancellationToken);
        return content ?? HtmlString.Empty;
    }
}
