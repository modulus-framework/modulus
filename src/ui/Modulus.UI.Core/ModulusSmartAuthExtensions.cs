using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>
/// Single-host auth: one default scheme that routes API callers to the bearer
/// handler and browser page navigations to the cookie handler. Pages keep
/// cookie auth (with <c>UseModulusHtmxRedirects</c> on the cookie options so
/// htmx fragment requests get <c>HX-Redirect</c> instead of a swapped 302),
/// while <c>/api/*</c> and <c>Bearer</c>-header callers keep bearer tokens.
/// <para>
/// Scheme names are strings so <c>Modulus.UI.Core</c> never references the
/// Identity package: the defaults match a host using <c>AddModulusIdentity</c>
/// (Identity application cookie) + OpenIddict local validation. Pass explicit
/// names for any other auth setup.
/// </para>
/// </summary>
public static class ModulusSmartAuthExtensions
{
    /// <summary>Default bearer scheme for OpenIddict local validation.</summary>
    public const string DefaultBearerScheme = "OpenIddict.Validation.AspNetCore";

    /// <summary>Default cookie scheme registered by ASP.NET Core Identity.</summary>
    public const string DefaultCookieScheme = "Identity.Application";

    /// <summary>Default name of the smart policy scheme this registers.</summary>
    public const string DefaultSmartScheme = "Modulus.Smart";

    /// <summary>
    /// Registers the <c>Modulus.Smart</c> policy scheme and makes it the
    /// default (authenticate + challenge). Idempotent per smart-scheme name.
    /// </summary>
    public static AuthenticationBuilder AddModulusSmartAuth(
        this IServiceCollection services,
        string bearerScheme = DefaultBearerScheme,
        string cookieScheme = DefaultCookieScheme,
        string smartScheme = DefaultSmartScheme)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(cookieScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(smartScheme);

        // AddIdentity sets DefaultAuthenticateScheme / DefaultChallengeScheme / DefaultForbidScheme to its cookie, and
        // an explicit DefaultAuthenticateScheme beats DefaultScheme, so setting only the default + challenge left
        // bearer callers unauthenticated (a 403 instead of the API answering). PostConfigure runs after every
        // Configure, so the smart scheme wins whether it is registered before or after Identity.
        services.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultScheme = smartScheme;
            o.DefaultAuthenticateScheme = smartScheme;
            o.DefaultChallengeScheme = smartScheme;
            o.DefaultForbidScheme = smartScheme;
        });

        return services.AddAuthentication(o =>
            {
                o.DefaultScheme = smartScheme;
                o.DefaultChallengeScheme = smartScheme;
            })
            .AddPolicyScheme(smartScheme, "Cookie or Bearer", o =>
                o.ForwardDefaultSelector = ctx => SelectScheme(ctx, bearerScheme, cookieScheme));
    }

    /// <summary>
    /// Makes every Razor Page require a signed-in user, except the folders listed in <paramref name="anonymousFolders"/>
    /// (default <c>/Account</c>: the sign-in, registration, sign-out and access-denied pages). Prebuilt feature UIs
    /// (Users, Tenancy, Settings, ...) apply their own permission check only when the app configures one, so without
    /// this an admin page is open to anyone who can reach the site. A feature UI's own <c>RequirePermission</c> still
    /// applies on top. API endpoints, controllers and the framework error pages are unaffected.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="anonymousFolders">Razor Pages folders reachable without signing in, e.g. <c>/Account</c>, <c>/Public</c>.</param>
    public static IServiceCollection AddModulusPageAuthorization(this IServiceCollection services, params string[] anonymousFolders)
    {
        ArgumentNullException.ThrowIfNull(services);

        var open = anonymousFolders is { Length: > 0 } ? anonymousFolders : [DefaultAnonymousFolder];
        // PostConfigure, not Configure: AddRazorPages() registers a setup that replaces the conventions collection, so a
        // Configure registered before it (this runs from Program.cs before the UI wiring adds AddRazorPages) is lost.
        return services.PostConfigure<RazorPagesOptions>(o =>
        {
            o.Conventions.AuthorizeFolder("/");
            foreach (var folder in open)
            {
                o.Conventions.AllowAnonymousToFolder(folder);
            }
        });
    }

    /// <summary>The Razor Pages folder of the Identity UI's sign-in pages, left open by <see cref="AddModulusPageAuthorization"/>.</summary>
    public const string DefaultAnonymousFolder = "/Account";

    internal static string SelectScheme(HttpContext ctx, string bearerScheme, string cookieScheme)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || ctx.Request.Path.StartsWithSegments("/api"))
            return bearerScheme;
        return cookieScheme;
    }
}
