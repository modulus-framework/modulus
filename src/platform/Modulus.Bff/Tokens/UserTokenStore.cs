namespace Modulus.Bff.Tokens;

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Modulus.Caching;

/// <summary>The tokens a web session holds for calling upstream APIs.</summary>
public sealed record BffUserTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string? IdToken = null);

/// <summary>Keeps a web session's tokens. The browser never sees them.</summary>
public interface IUserTokenStore
{
    /// <summary>True when tokens live in the authentication properties, so a change needs a cookie re-issue.</summary>
    bool StoresInCookie { get; }

    Task<BffUserTokens?> GetAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, CancellationToken ct = default);

    Task StoreAsync(string client, ClaimsPrincipal user, AuthenticationProperties properties, BffUserTokens tokens, CancellationToken ct = default);

    Task RemoveAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, CancellationToken ct = default);
}

/// <summary>
/// Default store: tokens in <see cref="ICacheService"/> (FusionCache: L1 memory + Redis L2 + backplane
/// when configured), encrypted with Data Protection, keyed by the session's <c>bff_sid</c> claim.
/// The cookie stays small and holds no token. With more than one BFF replica, register Redis
/// (<c>AddRedisFusionCache</c>) and a shared Data Protection key ring.
/// </summary>
internal sealed class ServerSideUserTokenStore(ICacheService cache, IDataProtectionProvider dataProtection, IOptionsMonitor<BffClientOptions> clients)
    : IUserTokenStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Modulus.Bff.UserTokens");

    public bool StoresInCookie => false;

    public async Task<BffUserTokens?> GetAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, CancellationToken ct = default)
    {
        if (Key(client, user) is not { } key)
            return null;
        var protectedValue = await cache.GetAsync<string>(key, ct).ConfigureAwait(false);
        if (protectedValue is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize(_protector.Unprotect(protectedValue), BffTokenJsonContext.Default.BffUserTokens);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public Task StoreAsync(string client, ClaimsPrincipal user, AuthenticationProperties properties, BffUserTokens tokens, CancellationToken ct = default)
    {
        var key = Key(client, user) ?? throw new InvalidOperationException($"The session has no '{BffDefaults.SessionIdClaim}' claim.");
        var value = _protector.Protect(JsonSerializer.Serialize(tokens, BffTokenJsonContext.Default.BffUserTokens));
        return cache.SetAsync(key, value, clients.Get(client).SessionLifetime, ct);
    }

    public Task RemoveAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, CancellationToken ct = default)
        => Key(client, user) is { } key ? cache.RemoveAsync(key, ct) : Task.CompletedTask;

    private static string? Key(string client, ClaimsPrincipal user)
        => user.FindFirst(BffDefaults.SessionIdClaim)?.Value is { Length: > 0 } sid ? $"bff:tokens:{client}:{sid}" : null;
}

/// <summary>
/// Stores tokens inside the (encrypted) session cookie's authentication properties. No shared
/// state across replicas, but the cookie is larger and is re-issued on every refresh.
/// </summary>
internal sealed class CookieUserTokenStore : IUserTokenStore
{
    private const string Access = "access_token";
    private const string Refresh = "refresh_token";
    private const string Id = "id_token";
    private const string Expires = "expires_at";

    public bool StoresInCookie => true;

    public Task<BffUserTokens?> GetAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, CancellationToken ct = default)
    {
        var access = properties?.GetTokenValue(Access);
        if (properties is null || string.IsNullOrEmpty(access))
            return Task.FromResult<BffUserTokens?>(null);

        var expires = DateTimeOffset.TryParse(properties.GetTokenValue(Expires), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.MinValue;
        return Task.FromResult<BffUserTokens?>(new BffUserTokens(access, properties.GetTokenValue(Refresh), expires, properties.GetTokenValue(Id)));
    }

    public Task StoreAsync(string client, ClaimsPrincipal user, AuthenticationProperties properties, BffUserTokens tokens, CancellationToken ct = default)
    {
        var list = new List<AuthenticationToken>
        {
            new() { Name = Access, Value = tokens.AccessToken },
            new() { Name = Expires, Value = tokens.ExpiresAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture) },
        };
        if (tokens.RefreshToken is not null)
            list.Add(new AuthenticationToken { Name = Refresh, Value = tokens.RefreshToken });
        if (tokens.IdToken is not null)
            list.Add(new AuthenticationToken { Name = Id, Value = tokens.IdToken });
        properties.StoreTokens(list);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string client, ClaimsPrincipal user, AuthenticationProperties? properties, CancellationToken ct = default)
    {
        properties?.StoreTokens([]);
        return Task.CompletedTask;
    }
}

/// <summary>Source-generated serialization for the session tokens.</summary>
[JsonSerializable(typeof(BffUserTokens))]
internal sealed partial class BffTokenJsonContext : JsonSerializerContext;
