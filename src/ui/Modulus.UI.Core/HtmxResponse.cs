using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Modulus.UI;

/// <summary>
/// Scoped per-request builder for htmx response headers. Register via
/// <c>AddModulusUi()</c> and inject into pages (or use
/// <c>HtmxPageModel.Htmx</c>). All <c>Trigger</c> calls in one request merge
/// into a single <c>HX-Trigger</c> header, so toasts and list-refresh events
/// compose instead of overwriting each other.
/// <para>
/// Toast contract: <c>Trigger("modulusToast", new { message, type })</c>.
/// <c>modulus-ui.js</c> also accepts the <c>modulus:toast</c> alias.
/// </para>
/// </summary>
public sealed class HtmxResponse
{
    /// <summary>Canonical toast event consumed by <c>modulus-ui.js</c>.</summary>
    public const string ToastEvent = "modulusToast";

    /// <summary>Alias accepted by <c>modulus-ui.js</c> (design-guide naming).</summary>
    public const string ToastEventAlias = "modulus:toast";

    /// <summary>
    /// Event instructing <c>modulus-ui.js</c> to close the shared modal after
    /// the swap completes (forms rendered inside modals).
    /// </summary>
    public const string ModalCloseEvent = "modulus:modal:close";

    /// <summary>Suffix every entity-change event must carry (see <see cref="NotifyChanged"/>).</summary>
    private const string ChangedSuffix = ":changed";

    private readonly HttpResponse _response;
    private readonly Dictionary<string, object?> _triggers = new(StringComparer.Ordinal);

    public HtmxResponse(IHttpContextAccessor accessor)
        : this(accessor?.HttpContext?.Response)
    {
    }

    internal HtmxResponse(HttpResponse? response)
    {
        _response = response ?? new DefaultHttpContext().Response;
    }

    /// <summary>Merges a client-side event into the <c>HX-Trigger</c> header.</summary>
    public HtmxResponse Trigger(string name, object? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _triggers[name] = detail;
        FlushTriggers();
        return this;
    }

    /// <summary>Queues a Tabler toast rendered by <c>modulus-ui.js</c>.</summary>
    /// <param name="message">Toast body.</param>
    /// <param name="type">Tabler alert color: success, info, warning, danger.</param>
    public HtmxResponse Toast(string message, string type = "success")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return Trigger(ToastEvent, new { message, type });
    }

    /// <summary>Closes the shared modal after the swap completes (forms rendered inside modals).</summary>
    public HtmxResponse CloseModal() => Trigger(ModalCloseEvent);

    /// <summary>
    /// Announces that an entity changed so grids listening on it (from body)
    /// re-query. <paramref name="entityEvent"/> must end with
    /// <c>:changed</c> — e.g. <c>product:changed</c>.
    /// </summary>
    public HtmxResponse NotifyChanged(string entityEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityEvent);
        if (!entityEvent.EndsWith(ChangedSuffix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Entity events must end with '{ChangedSuffix}' (e.g. 'product:changed').",
                nameof(entityEvent));
        return Trigger(entityEvent);
    }

    /// <summary>Client-side navigation (a plain redirect would only swap content).</summary>
    public HtmxResponse Redirect(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _response.Headers["HX-Redirect"] = url;
        return this;
    }

    /// <summary>Full client-side reload after the swap.</summary>
    public HtmxResponse Refresh()
    {
        _response.Headers["HX-Refresh"] = "true";
        return this;
    }

    /// <summary>Overrides the <c>hx-target</c> for this response.</summary>
    public HtmxResponse Retarget(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        _response.Headers["HX-Retarget"] = selector;
        return this;
    }

    /// <summary>Overrides the <c>hx-swap</c> strategy for this response.</summary>
    public HtmxResponse Reswap(string swap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(swap);
        _response.Headers["HX-Reswap"] = swap;
        return this;
    }

    /// <summary>Updates the browser URL without a reload (pairs with fragment swaps).</summary>
    public HtmxResponse PushUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _response.Headers["HX-Push-Url"] = url;
        return this;
    }

    private void FlushTriggers()
    {
        if (_triggers.Count == 0)
            return;

        if (_triggers.Count == 1)
        {
            var (name, detail) = _triggers.First();
            _response.Headers["HX-Trigger"] = detail is null
                ? name
                : JsonSerializer.Serialize(_triggers);
            return;
        }

        _response.Headers["HX-Trigger"] = JsonSerializer.Serialize(_triggers);
    }
}
