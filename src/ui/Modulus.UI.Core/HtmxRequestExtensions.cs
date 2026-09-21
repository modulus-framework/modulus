using Microsoft.AspNetCore.Http;

namespace Modulus.UI;

/// <summary>
/// HTMX request detection helpers. Centralizes the header contracts so pages,
/// auth redirects, and tests agree on what counts as a fragment request:
/// <c>HX-Request</c> on every enhanced request, <c>HX-Boosted</c> only on
/// boosted full-page navigations.
/// </summary>
public static class HtmxRequestExtensions
{
    /// <summary>True when the request came through htmx (fragment or boosted).</summary>
    public static bool IsHtmx(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.ContainsKey("HX-Request");
    }

    /// <summary>True for boosted navigations (htmx drives a full-page swap).</summary>
    public static bool IsHtmxBoosted(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.ContainsKey("HX-Boosted");
    }

    /// <summary>
    /// True for fragment swaps: an htmx request that is NOT boosted. Use this to
    /// decide between a full <c>Page()</c> and a <c>Partial(...)</c> (see
    /// <c>HtmxPageModel.PageOrPartial</c>), and for layout switching in
    /// <c>_ViewStart.cshtml</c>:
    /// <code>Layout = Context.Request.IsHtmxFragment() ? null : "_UiLayout";</code>
    /// </summary>
    public static bool IsHtmxFragment(this HttpRequest request)
        => request.IsHtmx() && !request.IsHtmxBoosted();

    /// <summary>The <c>HX-Target</c> element id being swapped, if any.</summary>
    public static string? HtmxTarget(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.TryGetValue("HX-Target", out var v) ? v.ToString() : null;
    }
}
