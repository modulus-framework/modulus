namespace Modulus.Bff.Tokens;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Modulus.Bff.Authentication;

/// <summary>The outcome of <c>IBffSessionService.SignInWithPasswordAsync</c>.</summary>
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
    /// The password sign-in with the second factor. <paramref name="mfaCode"/> is an authenticator or recovery code; when the account
    /// needs one and it is missing, the result's error is <c>mfa_required</c> and the caller asks for the code and tries again.
    /// </summary>
    Task<BffSignInResult> SignInWithPasswordAsync(HttpContext context, string client, string userName, string password, string? mfaCode, bool rememberMe = false, CancellationToken ct = default);

    /// <summary>
    /// Signs the person in with a passkey: the auth server's passkey grant turns <paramref name="assertion"/> (the browser's WebAuthn
    /// answer to the options of <paramref name="challengeId"/>) into tokens, then the session is set up as for a password sign-in.
    /// </summary>
    Task<BffSignInResult> SignInWithPasskeyAsync(HttpContext context, string client, string challengeId, string assertion, bool rememberMe = false, CancellationToken ct = default);

    /// <summary>
    /// Starts impersonating the person the one-time <paramref name="code"/> was issued for (the auth server's impersonation grant). The
    /// caller's own session is parked: its tokens stay in the token store and come back when the impersonation ends. The cookie then
    /// carries the impersonated person's session, with <c>bff_impersonator</c> set. Needs server-side token storage (the default).
    /// </summary>
    Task<BffSignInResult> StartImpersonationAsync(HttpContext context, string client, string code, CancellationToken ct = default);

    /// <summary>
    /// Ends the impersonation: revokes the impersonated person's tokens and restores the parked session. Returns <c>null</c> when the
    /// session is not impersonating (the caller decides what that means); a failed restore signs the session out.
    /// </summary>
    Task<BffSignInResult?> EndImpersonationAsync(HttpContext context, string client, CancellationToken ct = default);

    /// <summary>
    /// Revokes the session's tokens (when the auth server has a revocation endpoint), clears the store and removes
    /// the cookie. In OIDC login mode returns the auth server's end-session URL (RP-initiated logout) the browser
    /// should visit, with <paramref name="returnUrl"/> (a local path) as the post-logout target; otherwise <c>null</c>.
    /// An impersonating session also revokes the parked session's tokens.
    /// </summary>
    Task<string?> SignOutAsync(HttpContext context, string client, string? returnUrl = null, CancellationToken ct = default);
}

internal sealed class BffSessionService(
    IBffTokenClient tokenClient,
    IUserTokenStore serverStore,
    CookieUserTokenStore cookieStore,
    IBffDiscovery discovery,
    IOptionsMonitor<BffClientOptions> clients,
    IOptions<BffOptions> bff,
    TimeProvider clock) : IBffSessionService
{
    /// <summary>The <see cref="BffSignInResult.Error"/> when the auth server could not be reached (RFC 6749 §4.1.2.1 code).</summary>
    internal const string TemporarilyUnavailable = "temporarily_unavailable";

    public Task<BffSignInResult> SignInWithPasswordAsync(
        HttpContext context, string client, string userName, string password, bool rememberMe = false, CancellationToken ct = default)
        => SignInWithPasswordAsync(context, client, userName, password, mfaCode: null, rememberMe, ct);

    public async Task<BffSignInResult> SignInWithPasswordAsync(
        HttpContext context, string client, string userName, string password, string? mfaCode, bool rememberMe = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(client);
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return new BffSignInResult(null, "invalid_request");

        BffTokenResult result;
        try
        {
            result = await tokenClient.PasswordAsync(client, userName, password, mfaCode, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // The auth server (or its discovery document) could not be reached: say so instead of failing the request.
            return new BffSignInResult(null, TemporarilyUnavailable, "The sign-in service is not reachable.");
        }

        return await FinishSignInAsync(context, client, result, rememberMe, ct).ConfigureAwait(false);
    }

    public async Task<BffSignInResult> SignInWithPasskeyAsync(
        HttpContext context, string client, string challengeId, string assertion, bool rememberMe = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(client);
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(assertion))
            return new BffSignInResult(null, "invalid_request");

        BffTokenResult result;
        try
        {
            result = await tokenClient.PasskeyAsync(client, challengeId, assertion, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new BffSignInResult(null, TemporarilyUnavailable, "The sign-in service is not reachable.");
        }

        return await FinishSignInAsync(context, client, result, rememberMe, ct).ConfigureAwait(false);
    }

    public async Task<BffSignInResult> StartImpersonationAsync(HttpContext context, string client, string code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(client);
        if (string.IsNullOrWhiteSpace(code))
            return new BffSignInResult(null, "invalid_request");

        var scheme = BffDefaults.Scheme(client);
        var auth = await context.AuthenticateAsync(scheme).ConfigureAwait(false);
        if (!auth.Succeeded || auth.Principal is not { } actor || auth.Properties is not { } actorProperties)
            return new BffSignInResult(null, "login_required", "Sign in first.");
        if (actor.FindFirst(BffDefaults.ActorSessionClaim) is not null)
            return new BffSignInResult(null, "already_impersonating", "End the current impersonation first.");
        if (clients.Get(client).TokenStorage == BffTokenStorage.Cookie)
            return new BffSignInResult(null, "impersonation_unsupported", "Impersonation needs server-side token storage.");
        if (await serverStore.GetAsync(client, actor, actorProperties, ct).ConfigureAwait(false) is null)
            return new BffSignInResult(null, "login_required", "Sign in first.");

        BffTokenResult result;
        try
        {
            result = await tokenClient.ImpersonateAsync(client, code, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return new BffSignInResult(null, TemporarilyUnavailable, "The sign-in service is not reachable.");
        }

        // The impersonated session gets its own id; the parked session keeps the caller's id, and the cookie points at it.
        var parked = new Claim[]
        {
            new(BffDefaults.ActorSessionClaim, actor.FindFirst(BffDefaults.SessionIdClaim)?.Value ?? string.Empty),
            new(BffDefaults.ImpersonatorClaim, actor.Identity?.Name ?? string.Empty),
        };
        return await FinishSignInAsync(context, client, result, rememberMe: false, ct, parked).ConfigureAwait(false);
    }

    public async Task<BffSignInResult?> EndImpersonationAsync(HttpContext context, string client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(client);
        var scheme = BffDefaults.Scheme(client);
        var auth = await context.AuthenticateAsync(scheme).ConfigureAwait(false);
        if (!auth.Succeeded || auth.Principal is not { } impersonated || auth.Properties is not { } properties)
            return null;
        if (impersonated.FindFirst(BffDefaults.ActorSessionClaim)?.Value is not { Length: > 0 } actorSessionId)
            return null;

        // The impersonated person's tokens end with the impersonation, before the caller's session comes back.
        var store = serverStore;
        if (await store.GetAsync(client, impersonated, properties, ct).ConfigureAwait(false) is { } ended)
            await tokenClient.RevokeAsync(client, ended.AccessToken, "access_token", ct).ConfigureAwait(false);
        await store.RemoveAsync(client, impersonated, properties, ct).ConfigureAwait(false);

        var parkedUser = SessionPrincipal(actorSessionId);
        var parked = await store.GetAsync(client, parkedUser, null, ct).ConfigureAwait(false);
        if (parked is null)
        {
            await context.SignOutAsync(scheme).ConfigureAwait(false);
            return new BffSignInResult(null, "login_required", "The session before impersonation has ended. Sign in again.");
        }

        if (parked.RefreshToken is { } refresh && parked.ExpiresAt - clients.Get(client).RefreshBeforeExpiry <= clock.GetUtcNow())
        {
            var refreshed = await tokenClient.RefreshAsync(client, refresh, ct).ConfigureAwait(false);
            if (!refreshed.Succeeded)
            {
                await store.RemoveAsync(client, parkedUser, null, ct).ConfigureAwait(false);
                await context.SignOutAsync(scheme).ConfigureAwait(false);
                return new BffSignInResult(null, "login_required", "The session before impersonation has ended. Sign in again.");
            }
            parked = refreshed.Tokens! with { IdToken = refreshed.Tokens!.IdToken ?? parked.IdToken };
            await store.StoreAsync(client, parkedUser, new AuthenticationProperties(), parked, ct).ConfigureAwait(false);
        }

        // The caller's own session id: its tokens are already stored under it.
        var claims = await ClaimsForAsync(parked, ct).ConfigureAwait(false);
        return await SignInSessionAsync(context, client, claims, actorSessionId, rememberMe: false, ct).ConfigureAwait(false);
    }

    // The token answer becomes the session: the claims, the token store and the session cookie. Shared by every sign-in method.
    private async Task<BffSignInResult> FinishSignInAsync(
        HttpContext context, string client, BffTokenResult result, bool rememberMe, CancellationToken ct, IEnumerable<Claim>? extraClaims = null)
    {
        if (!result.Succeeded)
        {
            // "server_error": the token endpoint answered without a usable OAuth body (a 5xx, a proxy error page).
            var unavailable = result.Error == "server_error";
            return new BffSignInResult(null, unavailable ? TemporarilyUnavailable : result.Error, unavailable ? "The sign-in service is not available." : result.ErrorDescription);
        }

        var claims = (await ClaimsForAsync(result.Tokens!, ct).ConfigureAwait(false)).ToList();
        if (extraClaims is not null)
            claims.AddRange(extraClaims);
        return await SignInSessionAsync(context, client, claims, sessionId: null, rememberMe, ct, result.Tokens!).ConfigureAwait(false);
    }

    // Stores the tokens under the session and signs the cookie in with the normalized claims. A new session gets a new id.
    private async Task<BffSignInResult> SignInSessionAsync(
        HttpContext context, string client, List<Claim> claims, string? sessionId, bool rememberMe, CancellationToken ct, BffUserTokens? tokens = null)
    {
        claims.RemoveAll(c => c.Type == BffDefaults.SessionIdClaim);
        claims.Add(new Claim(BffDefaults.SessionIdClaim, sessionId ?? Guid.NewGuid().ToString("N")));

        var scheme = BffDefaults.Scheme(client);
        var roleTypes = BffClaims.DefaultRoleClaimTypes(bff.Value.AuthServer).Concat(bff.Value.RoleClaimTypes);
        var principal = BffClaims.Normalize(new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)), roleTypes, scheme);

        var properties = new AuthenticationProperties { IsPersistent = rememberMe };
        if (tokens is not null)
            await StoreFor(client).StoreAsync(client, principal, properties, tokens, ct).ConfigureAwait(false);
        await context.SignInAsync(scheme, principal, properties).ConfigureAwait(false);
        return new BffSignInResult(principal);
    }

    private async Task<List<Claim>> ClaimsForAsync(BffUserTokens tokens, CancellationToken ct)
    {
        var claims = (await tokenClient.GetUserInfoAsync(tokens.AccessToken, ct).ConfigureAwait(false)).ToList();
        if (claims.Count == 0 && tokens.IdToken is { } idToken)
            claims.AddRange(new JsonWebToken(idToken).Claims);
        if (claims.Count == 0 && TryReadJwt(tokens.AccessToken) is { } accessClaims)
            claims.AddRange(accessClaims);
        return claims;
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
        await RevokeAndForgetAsync(client, user, auth.Properties, tokens, store, ct).ConfigureAwait(false);

        // An impersonating session also drops the caller's parked session, so nothing stays signed in behind it.
        if (user.FindFirst(BffDefaults.ActorSessionClaim)?.Value is { Length: > 0 } actorSessionId)
        {
            var parkedUser = SessionPrincipal(actorSessionId);
            var parked = await store.GetAsync(client, parkedUser, null, ct).ConfigureAwait(false);
            await RevokeAndForgetAsync(client, parkedUser, null, parked, store, ct).ConfigureAwait(false);
        }

        await context.SignOutAsync(scheme).ConfigureAwait(false);

        // RP-initiated logout at the auth server (when it has one): the browser navigates there.
        var options = clients.Get(client);
        var endpoints = await discovery.GetEndpointsAsync(ct).ConfigureAwait(false);
        if (options.LoginMode != BffLoginMode.Oidc || endpoints.EndSessionEndpoint is null)
            return null;

        var request = context.Request;
        var target = BffEndpointRouteBuilderExtensions.SafeReturnUrl(returnUrl, bff.Value.AllowedReturnOrigins);
        var postLogout = Uri.TryCreate(target, UriKind.Absolute, out _)
            ? target
            : $"{request.Scheme}://{request.Host}{request.PathBase}{target}";
        var query = new Dictionary<string, string?> { ["post_logout_redirect_uri"] = postLogout, ["client_id"] = options.ClientId };
        if (tokens?.IdToken is { } idToken)
            query["id_token_hint"] = idToken;
        return QueryHelpers.AddQueryString(endpoints.EndSessionEndpoint, query);
    }

    private async Task RevokeAndForgetAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, BffUserTokens? tokens, IUserTokenStore store, CancellationToken ct)
    {
        if (tokens?.RefreshToken is { } refresh)
            await tokenClient.RevokeAsync(client, refresh, "refresh_token", ct).ConfigureAwait(false);
        if (tokens is not null)
            await tokenClient.RevokeAsync(client, tokens.AccessToken, "access_token", ct).ConfigureAwait(false);
        await store.RemoveAsync(client, user, properties, ct).ConfigureAwait(false);
    }

    // The principal that keys the token store for one session id (the store reads only the session claim).
    private static ClaimsPrincipal SessionPrincipal(string sessionId)
        => new(new ClaimsIdentity([new Claim(BffDefaults.SessionIdClaim, sessionId)]));

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
