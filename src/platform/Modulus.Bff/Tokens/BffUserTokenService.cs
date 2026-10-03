namespace Modulus.Bff.Tokens;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

/// <summary>Returns a usable access token for the current request's caller.</summary>
public interface IBffAccessTokenService
{
    /// <summary>
    /// Web: the session's stored access token, refreshed first when it expires within
    /// <see cref="BffClientOptions.RefreshBeforeExpiry"/> (or when <paramref name="forceRefresh"/>).
    /// A refused refresh ends the session. Mobile/partner: the inbound bearer token.
    /// Returns <c>null</c> when there is no signed-in caller.
    /// </summary>
    Task<string?> GetAccessTokenAsync(HttpContext context, string client, bool forceRefresh = false, CancellationToken ct = default);
}

internal sealed class BffAccessTokenService(
    IUserTokenStore serverStore,
    CookieUserTokenStore cookieStore,
    IBffTokenClient tokenClient,
    IOptionsMonitor<BffClientOptions> clients,
    TimeProvider clock,
    ILogger<BffAccessTokenService> logger) : IBffAccessTokenService
{
    // Striped per-session locks: one refresh per session at a time on this node, so rotating
    // refresh tokens are not redeemed twice. Fixed size, so nothing grows with the session count.
    private static readonly SemaphoreSlim[] s_locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<string?> GetAccessTokenAsync(HttpContext context, string client, bool forceRefresh = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = clients.Get(client);
        if (options.Kind != BffClientKind.Web)
            return ReadBearer(context);

        var scheme = BffDefaults.Scheme(client);
        var auth = await context.AuthenticateAsync(scheme).ConfigureAwait(false);
        if (!auth.Succeeded || auth.Principal is not { } user || auth.Properties is not { } properties)
            return null;

        var store = StoreFor(options);
        var tokens = await store.GetAsync(client, user, properties, ct).ConfigureAwait(false);
        if (tokens is null)
            return null;
        if (!forceRefresh && !NeedsRefresh(tokens, options))
            return tokens.AccessToken;

        var sid = user.FindFirst(BffDefaults.SessionIdClaim)?.Value ?? user.Identity?.Name ?? string.Empty;
        var gate = s_locks[(StringComparer.Ordinal.GetHashCode(sid) & int.MaxValue) % s_locks.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another request may have refreshed while this one waited.
            var current = await store.GetAsync(client, user, properties, ct).ConfigureAwait(false) ?? tokens;
            if (current.AccessToken != tokens.AccessToken && current.ExpiresAt > clock.GetUtcNow())
                return current.AccessToken;
            if (current.RefreshToken is null)
                return current.ExpiresAt > clock.GetUtcNow() ? current.AccessToken : null;

            var result = await tokenClient.RefreshAsync(client, current.RefreshToken, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                logger.LogInformation("Refresh for BFF client {Client} was refused ({Error}); ending the session", client, result.Error);
                await store.RemoveAsync(client, user, properties, ct).ConfigureAwait(false);
                if (!context.Response.HasStarted)
                    await context.SignOutAsync(scheme).ConfigureAwait(false);
                return null;
            }

            var refreshed = result.Tokens! with { IdToken = result.Tokens!.IdToken ?? current.IdToken };
            await store.StoreAsync(client, user, properties, refreshed, ct).ConfigureAwait(false);
            if (store.StoresInCookie && !context.Response.HasStarted)
                await context.SignInAsync(scheme, user, properties).ConfigureAwait(false);
            return refreshed.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    internal IUserTokenStore StoreFor(BffClientOptions options)
        => options.TokenStorage == BffTokenStorage.Cookie ? cookieStore : serverStore;

    private bool NeedsRefresh(BffUserTokens tokens, BffClientOptions options)
        => tokens.ExpiresAt - options.RefreshBeforeExpiry <= clock.GetUtcNow();

    private static string? ReadBearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }
}
