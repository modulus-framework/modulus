namespace Modulus.Bff.Tokens;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Modulus.Bff.Authentication;

/// <summary>The outcome of <see cref="IBffSessionService.SignInWithPasswordAsync"/>.</summary>
public sealed record BffSignInResult(ClaimsPrincipal? Principal, string? Error = null, string? ErrorDescription = null)
{
    public bool Succeeded => Principal is not null;
}

/// <summary>
/// Opens and ends a web client's session. Backs the <c>/bff/login</c> and <c>/bff/logout</c> endpoints and is
/// public so a server-rendered host's own sign-in and sign-out pages share the same token handling.
/// </summary>
public interface IBffSessionService
{
    /// <summary>
    /// Signs <paramref name="userName"/> in with the password grant: the tokens go to the client's token store,
    /// the session cookie carries the user's claims (userinfo, else the id or access token) and a <c>bff_sid</c>.
    /// </summary>
    Task<BffSignInResult> SignInWithPasswordAsync(HttpContext context, string client, string userName, string password, bool rememberMe = false, CancellationToken ct = default);

    /// <summary>
    /// Revokes the session's tokens (when the auth server has a revocation endpoint), clears the store and removes
    /// the cookie. In OIDC login mode returns the auth server's end-session URL (RP-initiated logout) the browser
    /// should visit, with <paramref name="returnUrl"/> (a local path) as the post-logout target; otherwise <c>null</c>.
    /// </summary>
    Task<string?> SignOutAsync(HttpContext context, string client, string? returnUrl = null, CancellationToken ct = default);
}

internal sealed class BffSessionService(
    IBffTokenClient tokenClient,
    IUserTokenStore serverStore,
    CookieUserTokenStore cookieStore,
    IBffDiscovery discovery,
    IOptionsMonitor<BffClientOptions> clients,
    IOptions<BffOptions> bff) : IBffSessionService
{
    public async Task<BffSignInResult> SignInWithPasswordAsync(
        HttpContext context, string client, string userName, string password, bool rememberMe = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(client);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return new BffSignInResult(null, "invalid_request");

        var result = await tokenClient.PasswordAsync(client, userName, password, ct).ConfigureAwait(false);
        if (!result.Succeeded)
            return new BffSignInResult(null, result.Error, result.ErrorDescription);

        var tokens = result.Tokens!;
        var claims = (await tokenClient.GetUserInfoAsync(tokens.AccessToken, ct).ConfigureAwait(false)).ToList();
        if (claims.Count == 0 && tokens.IdToken is { } idToken)
            claims.AddRange(new JsonWebToken(idToken).Claims);
        if (claims.Count == 0 && TryReadJwt(tokens.AccessToken) is { } accessClaims)
            claims.AddRange(accessClaims);
        claims.Add(new Claim(BffDefaults.SessionIdClaim, Guid.NewGuid().ToString("N")));

        var scheme = BffDefaults.Scheme(client);
        var roleTypes = BffClaims.DefaultRoleClaimTypes(bff.Value.AuthServer).Concat(bff.Value.RoleClaimTypes);
        var principal = BffClaims.Normalize(new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)), roleTypes, scheme);

        var properties = new AuthenticationProperties { IsPersistent = rememberMe };
        await StoreFor(client).StoreAsync(client, principal, properties, tokens, ct).ConfigureAwait(false);
        await context.SignInAsync(scheme, principal, properties).ConfigureAwait(false);
        return new BffSignInResult(principal);
    }

    public async Task<string?> SignOutAsync(HttpContext context, string client, string? returnUrl = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(client);
        var scheme = BffDefaults.Scheme(client);
        var auth = await context.AuthenticateAsync(scheme).ConfigureAwait(false);
        if (!auth.Succeeded || auth.Principal is not { } user)
            return null;

        var store = StoreFor(client);
        var tokens = await store.GetAsync(client, user, auth.Properties, ct).ConfigureAwait(false);
        if (tokens?.RefreshToken is { } refresh)
            await tokenClient.RevokeAsync(client, refresh, "refresh_token", ct).ConfigureAwait(false);
        if (tokens is not null)
            await tokenClient.RevokeAsync(client, tokens.AccessToken, "access_token", ct).ConfigureAwait(false);

        await store.RemoveAsync(client, user, auth.Properties, ct).ConfigureAwait(false);
        await context.SignOutAsync(scheme).ConfigureAwait(false);

        // RP-initiated logout at the auth server (when it has one): the browser navigates there.
        var options = clients.Get(client);
        var endpoints = await discovery.GetEndpointsAsync(ct).ConfigureAwait(false);
        if (options.LoginMode != BffLoginMode.Oidc || endpoints.EndSessionEndpoint is null)
            return null;

        var request = context.Request;
        var postLogout = $"{request.Scheme}://{request.Host}{request.PathBase}{BffEndpointRouteBuilderExtensions.SafeReturnUrl(returnUrl)}";
        var query = new Dictionary<string, string?> { ["post_logout_redirect_uri"] = postLogout, ["client_id"] = options.ClientId };
        if (tokens?.IdToken is { } idToken)
            query["id_token_hint"] = idToken;
        return QueryHelpers.AddQueryString(endpoints.EndSessionEndpoint, query);
    }

    private IUserTokenStore StoreFor(string client)
        => clients.Get(client).TokenStorage == BffTokenStorage.Cookie ? cookieStore : serverStore;

    private static IEnumerable<Claim>? TryReadJwt(string token)
    {
        try
        {
            return new JsonWebToken(token).Claims.ToList();
        }
        catch (ArgumentException)
        {
            return null; // encrypted or opaque access token
        }
    }
}
