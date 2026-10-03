namespace Modulus.Bff;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Bff.Middleware;
using Modulus.Bff.Proxy;
using Modulus.Bff.Tokens;
using Yarp.ReverseProxy.Configuration;
using Modulus.AspNetCore.Security.Policy;
using Modulus.Core.Abstractions.Security;

/// <summary>Maps BFF clients: their session endpoints, route groups for aggregators, and the YARP passthrough.</summary>
public static class BffEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Adds the per-client edge rules (CSRF for web, app-version gate and ETags for mobile,
    /// idempotency keys for partners). Place it after <c>UseAuthentication()</c> and before
    /// <c>UseAuthorization()</c>; call <c>UseRateLimiter()</c> after <c>UseAuthorization()</c>
    /// when a client has a <c>RateLimit</c>.
    /// </summary>
    public static IApplicationBuilder UseModulusBff(this IApplicationBuilder app)
        => app.UseMiddleware<BffMiddleware>();

    /// <summary>
    /// Maps every registered client's session endpoints (web: <c>/bff/login</c>, <c>/bff/user</c>,
    /// <c>/bff/logout</c>; mobile/partner: <c>/bff/me</c>) under its <c>PathPrefix</c>, then the YARP
    /// passthrough routes of all clients. Call once.
    /// </summary>
    public static IEndpointRouteBuilder MapModulusBff(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var registry = endpoints.ServiceProvider.GetRequiredService<BffClientRegistry>();
        var clients = endpoints.ServiceProvider.GetRequiredService<IOptionsMonitor<BffClientOptions>>();
        var clash = registry.Names
            .GroupBy(n => BffPaths.Normalize(clients.Get(n).PathPrefix), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"BFF clients {string.Join(", ", clash.Select(n => $"'{n}'"))} share the path prefix '{clash.Key}'. " +
                "Clients hosted together need distinct Bff:Clients:{name}:PathPrefix values (e.g. /web, /mobile).");
        }

        foreach (var name in registry.Names)
            MapSessionEndpoints(endpoints, name);

        endpoints.MapReverseProxy().ConfigureEndpoints((builder, route) =>
        {
            if (route.Metadata?.TryGetValue(BffProxyConfigProvider.ClientMetadataKey, out var client) == true && client is not null)
                builder.WithMetadata(Metadata(endpoints, client));
            if (route.Metadata?.ContainsKey(BffProxyConfigProvider.EventStreamMetadataKey) == true)
                builder.WithMetadata(BffEventStreamMetadata.Instance);
        });
        return endpoints;
    }

    /// <summary>
    /// A route group for client <paramref name="name"/>'s own endpoints (aggregators): under its
    /// <c>PathPrefix</c>, tagged with the client, behind its policy and rate limit. Endpoints that
    /// should stay public call <c>AllowAnonymous()</c>.
    /// </summary>
    public static RouteGroupBuilder MapBffClient(this IEndpointRouteBuilder endpoints, string name)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptionsMonitor<BffClientOptions>>().Get(name);
        if (!endpoints.ServiceProvider.GetRequiredService<BffClientRegistry>().Names.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"BFF client '{name}' is not registered; add it in AddModulusBff.");

        var group = endpoints.MapGroup(BffPaths.Normalize(options.PathPrefix))
            .WithMetadata(new BffClientMetadata(name, options.Kind))
            .RequireAuthorization(BffDefaults.Policy(name));
        if (BffPaths.HasRateLimit(options))
            group.RequireRateLimiting(BffDefaults.RateLimitPolicy(name));
        return group;
    }

    private static BffClientMetadata Metadata(IEndpointRouteBuilder endpoints, string name)
        => new(name, endpoints.ServiceProvider.GetRequiredService<IOptionsMonitor<BffClientOptions>>().Get(name).Kind);

    private static void MapSessionEndpoints(IEndpointRouteBuilder endpoints, string name)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptionsMonitor<BffClientOptions>>().Get(name);
        var bff = endpoints.MapBffClient(name).MapGroup("/bff").WithTags("BFF " + name);

        if (options.Kind != BffClientKind.Web)
        {
            bff.MapGet("/me", (HttpContext context) => Results.Ok(Claims(context.User)));
            return;
        }

        var scheme = BffDefaults.Scheme(name);
        bff.MapGet("/user", (HttpContext context) => Results.Ok(Claims(context.User)));

        if (options.LoginMode == BffLoginMode.Oidc)
        {
            // A top-level browser navigation, so no CSRF header can be sent.
            bff.MapGet("/login", (string? returnUrl) => Results.Challenge(
                    new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [BffDefaults.OidcScheme(name)]))
                .Loosen(new LoosenedAttribute("Starts the OIDC sign-in (a browser navigation before any session exists)") { Framework = true })
                .WithMetadata(BffSkipCsrfMetadata.Instance);
        }
        else
        {
            bff.MapPost("/login", (BffLoginRequest request, HttpContext context, CancellationToken ct) => PasswordLoginAsync(context, name, request, ct))
                .Loosen(new LoosenedAttribute("Password sign-in: creates the session, so there is none yet") { Framework = true });
        }

        bff.MapPost("/logout", (HttpContext context, string? returnUrl, CancellationToken ct) => LogoutAsync(context, name, returnUrl, ct))
            .Loosen(new LoosenedAttribute("Sign-out must succeed even when the session already expired") { Framework = true });
    }

    private static async Task<IResult> PasswordLoginAsync(HttpContext context, string name, BffLoginRequest request, CancellationToken ct)
    {
        var result = await context.RequestServices.GetRequiredService<IBffSessionService>()
            .SignInWithPasswordAsync(context, name, request.UserName, request.Password, request.RememberMe, ct).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Ok(Claims(result.Principal!))
            : Results.BadRequest(new { error = result.Error, error_description = result.ErrorDescription });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, string name, string? returnUrl, CancellationToken ct)
    {
        var endSessionUrl = await context.RequestServices.GetRequiredService<IBffSessionService>()
            .SignOutAsync(context, name, returnUrl, ct).ConfigureAwait(false);
        return endSessionUrl is null ? Results.NoContent() : Results.Ok(new { endSessionUrl });
    }

    /// <summary>Only local paths: <c>/x</c>, never <c>//host</c> or <c>/\host</c> (open-redirect guard).</summary>
    internal static string SafeReturnUrl(string? returnUrl)
        => returnUrl is { Length: > 0 } url && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
            ? url
            : "/";

    private static BffUserClaim[] Claims(ClaimsPrincipal user)
        => user.Claims.Where(c => c.Type != BffDefaults.SessionIdClaim).Select(c => new BffUserClaim(c.Type, c.Value)).ToArray();
}

/// <summary>The body of <c>POST /bff/login</c> (password login mode).</summary>
public sealed record BffLoginRequest(string UserName, string Password, bool RememberMe = false);

/// <summary>One claim as returned by <c>/bff/user</c> and <c>/bff/me</c>.</summary>
public sealed record BffUserClaim(string Type, string Value);
