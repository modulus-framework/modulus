using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.UI;

/// <summary>
/// Base class for UI pages with HTMX progressive enhancement. Every mutation
/// keeps its classic full-page behavior (redirect) for non-JS callers and adds
/// an HTMX branch — gated on <see cref="IsHtmxRequest"/> — that returns a
/// fragment (<see cref="HtmxPartial"/>) or removes the targeted element
/// (<see cref="HtmxEmpty"/>). The shared <c>modulus-ui.js</c> wires the
/// antiforgery token into HTMX requests and renders <c>modulusToast</c>
/// triggers (see <see cref="HtmxToast"/>).
/// <para>
/// New helpers: <see cref="PageOrPartial"/> (full page for normal/boosted
/// navigation, partial for fragment requests) and <c>HandleAsync</c>
/// (runs a command; maps <see cref="ValidationException"/> to ModelState and
/// returns the form partial with 422 so htmx swaps the re-rendered form).
/// Layout switching in <c>_ViewStart.cshtml</c>:
/// <code>Layout = Context.Request.IsHtmxFragment() ? null : "_UiLayout";</code>
/// </para>
/// </summary>
public abstract class HtmxPageModel : PageModel
{
    /// <summary>Request header HTMX sends on every enhanced request.</summary>
    public const string HxRequestHeader = "HX-Request";

    /// <summary>Response header carrying client-side event triggers.</summary>
    public const string HxTriggerHeader = "HX-Trigger";

    /// <summary>Response header instructing HTMX to navigate client-side.</summary>
    public const string HxRedirectHeader = "HX-Redirect";

    private HtmxResponse? _htmx;

    /// <summary>
    /// Scoped per-request htmx response builder. Resolved from DI when the host
    /// called <c>AddModulusUi()</c>; falls back to a page-bound instance so
    /// handlers stay unit-testable without a service provider.
    /// </summary>
    protected HtmxResponse Htmx => _htmx ??= ResolveHtmx();

    /// <summary>
    /// Whether the current request came through HTMX. Null-safe: without an
    /// HTTP context (e.g. unit tests calling handlers directly) this is
    /// <c>false</c>, preserving the classic full-page behavior.
    /// </summary>
    protected bool IsHtmxRequest
        => PageContext?.HttpContext?.Request.Headers.ContainsKey(HxRequestHeader) is true;

    /// <summary>
    /// Public fragment probe for Razor views: full shell for normal/boosted
    /// navigations, layout-less content for htmx fragment swaps.
    /// Intended use at the top of a list page:
    /// <c>Layout = Model.IsHtmxFragment ? null : "_UiLayout";</c>
    /// Null-safe: without an HTTP context this is <c>false</c>.
    /// </summary>
    public bool IsHtmxFragment
        => PageContext?.HttpContext?.Request.IsHtmxFragment() ?? false;

    /// <summary>
    /// Full <c>Page()</c> for normal/boosted navigation, partial for fragment
    /// requests. Keeps list pages to a single handler for both shapes.
    /// </summary>
    protected IActionResult PageOrPartial(string partialName, object? model = null)
        => IsHtmxRequest && (PageContext?.HttpContext.Request.IsHtmxFragment() ?? true)
            ? HtmxPartial(partialName, model ?? this)
            : Page();

    /// <summary>
    /// Runs a command; maps <see cref="ValidationException"/> failures to
    /// ModelState and returns the form partial with 422 (htmx swaps it via the
    /// <c>422 → swap:true</c> rule in <c>modulus-ui.js</c>). The mediator's
    /// <c>ValidationBehavior</c> formats failures as <c>"Property: message"</c>
    /// strings, so each entry is added as a model-level error.
    /// </summary>
    protected async Task<IActionResult> HandleAsync(
        Func<Task> action,
        string formPartial,
        Func<IActionResult> onSuccess)
        => await HandleAsync(action, formPartial, (object)this, onSuccess);

    /// <summary>
    /// Overload for partials bound to a dedicated form model instead of the page.
    /// </summary>
    protected async Task<IActionResult> HandleAsync(
        Func<Task> action,
        string formPartial,
        object? model,
        Func<IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(formPartial);
        ArgumentNullException.ThrowIfNull(onSuccess);

        try
        {
            await action();
            return onSuccess();
        }
        catch (ValidationException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return HtmxPartial(formPartial, model ?? this);
        }
    }

    /// <summary>
    /// Renders a Razor partial as an HTMX swap fragment. Built explicitly
    /// (fresh <c>ViewData</c> plus a copy of the ambient <c>ModelState</c>) so
    /// fragments render validation summaries, never inherit page ViewData, and
    /// handlers stay unit-testable without a full view context.
    /// </summary>
    protected PartialViewResult HtmxPartial(string viewName, object? model)
    {
        var viewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model,
        };
        // ModelState is readable with or without page activation; without it
        // there is simply nothing to carry (unit-test path).
        viewData.ModelState.Merge(ModelState);
        return new PartialViewResult { ViewName = viewName, ViewData = viewData };
    }

    /// <summary>
    /// Returns an empty body so the <c>hx-target</c> element is removed when
    /// swapped with <c>hx-swap="outerHTML"</c> (e.g. deleted rows).
    /// </summary>
    protected ContentResult HtmxEmpty() => Content(string.Empty);

    /// <summary>
    /// Fires a client-side trigger. <c>modulus-ui.js</c> renders the
    /// <c>modulusToast</c> trigger as a dismissible Tabler toast; anything else
    /// is left for page-level <c>htmx:afterRequest</c> listeners. Multiple
    /// calls merge into one <c>HX-Trigger</c> header via <see cref="Htmx"/>.
    /// </summary>
    protected void HtmxTrigger(string name, object? detail = null)
        => Htmx.Trigger(name, detail);

    /// <summary>
    /// Shows a toast after the swap completes (no page reload).
    /// <paramref name="type"/> is a Tabler alert color: success, info,
    /// warning, danger.
    /// </summary>
    protected void HtmxToast(string message, string type = "success")
        => Htmx.Toast(message, type);

    /// <summary>
    /// Navigates the browser client-side (for HTMX posts that change location,
    /// where a plain redirect would only swap content).
    /// </summary>
    protected ContentResult HtmxRedirect(string url)
    {
        Htmx.Redirect(url);
        return Content(string.Empty);
    }

    private HtmxResponse ResolveHtmx()
    {
        var scoped = HttpContext?.RequestServices?.GetService<HtmxResponse>();
        if (scoped is not null)
            return scoped;

        try
        {
            return new HtmxResponse(Response);
        }
        catch (InvalidOperationException)
        {
            // No HttpContext (direct unit-test instantiation): bind nowhere.
            return new HtmxResponse((HttpResponse?)null);
        }
    }
}
