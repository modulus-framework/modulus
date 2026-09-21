using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Modulus.UI;

/// <summary>
/// Cookie-auth redirect handling for htmx. A 302 to the login page swapped
/// into a table cell is the classic broken-HTMX-app symptom: for htmx
/// requests this sends <c>HX-Redirect</c> instead so the browser navigates
/// top-level. Wire from the host's cookie options:
/// <code>services.AddCookie().AddCookie(o =&gt; o.UseModulusHtmxRedirects());</code>
/// Access-denied uses the same path (retarget to a toast is a page-level choice).
/// </summary>
public static class HtmxCookieAuthExtensions
{
    /// <summary>
    /// Replaces the login/access-denied redirects with htmx-aware variants.
    /// Non-htmx requests keep the default redirect behavior.
    /// </summary>
    public static CookieAuthenticationOptions UseModulusHtmxRedirects(
        this CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var priorLogin = options.Events.OnRedirectToLogin;
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.IsHtmx())
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.Headers["HX-Redirect"] = ctx.RedirectUri;
                return Task.CompletedTask;
            }

            return priorLogin(ctx);
        };

        var priorDenied = options.Events.OnRedirectToAccessDenied;
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.IsHtmx())
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.Headers["HX-Redirect"] = ctx.RedirectUri;
                return Task.CompletedTask;
            }

            return priorDenied(ctx);
        };

        return options;
    }
}
