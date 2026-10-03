namespace Modulus.Bff.Authentication;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Modulus.Caching;

/// <summary>Options of <see cref="BffIntrospectionHandler"/>: the BFF client it validates for.</summary>
public sealed class BffIntrospectionOptions : AuthenticationSchemeOptions
{
    public string Client { get; set; } = string.Empty;
}

/// <summary>
/// Validates bearer tokens with RFC 7662 introspection at the auth server's discovered
/// <c>introspection_endpoint</c> (OpenIddict, Keycloak, Okta, Duende, Authentik, Auth0 via a
/// custom domain...). Works with encrypted and opaque tokens. Active results are cached by
/// token hash for <see cref="BffClientOptions.IntrospectionCacheDuration"/>, capped at <c>exp</c>.
/// </summary>
public sealed class BffIntrospectionHandler(
    IOptionsMonitor<BffIntrospectionOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IHttpClientFactory httpClientFactory,
    IBffDiscovery discovery,
    IOptionsMonitor<BffClientOptions> clients,
    IOptions<BffOptions> bff,
    ICacheService cache,
    TimeProvider clock)
    : AuthenticationHandler<BffIntrospectionOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
            return AuthenticateResult.NoResult();

        var key = "bff:introspect:" + Options.Client + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var claims = await cache.GetAsync<Dictionary<string, string[]>>(key, Context.RequestAborted).ConfigureAwait(false);
        if (claims is null)
        {
            var (active, introspected, expiresAt) = await IntrospectAsync(token).ConfigureAwait(false);
            if (!active)
                return AuthenticateResult.Fail("The token is not active.");
            claims = introspected;
            var lifetime = clients.Get(Options.Client).IntrospectionCacheDuration;
            if (expiresAt is { } exp && exp - clock.GetUtcNow() < lifetime)
                lifetime = exp - clock.GetUtcNow();
            if (lifetime > TimeSpan.Zero)
                await cache.SetAsync(key, claims, lifetime, Context.RequestAborted).ConfigureAwait(false);
        }

        var audiences = clients.Get(Options.Client).Audiences;
        if (audiences.Count > 0)
        {
            var tokenAudiences = claims.TryGetValue("aud", out var aud)
                ? aud.SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                : [];
            if (!tokenAudiences.Any(a => audiences.Contains(a, StringComparer.Ordinal)))
                return AuthenticateResult.Fail("The token was not issued for this audience.");
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims.SelectMany(c => c.Value.Select(v => new Claim(c.Key, v))), Scheme.Name));
        var roleTypes = BffClaims.DefaultRoleClaimTypes(bff.Value.AuthServer).Concat(bff.Value.RoleClaimTypes);
        principal = BffClaims.Normalize(principal, roleTypes, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private async Task<(bool Active, Dictionary<string, string[]> Claims, DateTimeOffset? ExpiresAt)> IntrospectAsync(string token)
    {
        var endpoints = await discovery.GetEndpointsAsync(Context.RequestAborted).ConfigureAwait(false);
        if (endpoints.IntrospectionEndpoint is null)
            throw new InvalidOperationException("The auth server publishes no introspection endpoint; use TokenValidation=Jwt.");

        var client = clients.Get(Options.Client);
        var id = client.IntrospectionClientId ?? client.ClientId;
        var secret = client.IntrospectionClientSecret ?? client.ClientSecret;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.IntrospectionEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token, ["token_type_hint"] = "access_token" }),
        };
        if (id is not null && secret is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(id) + ":" + Uri.EscapeDataString(secret))));
        }

        using var response = await httpClientFactory.CreateClient(BffDefaults.AuthorityHttpClient)
            .SendAsync(request, Context.RequestAborted).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("Introspection for BFF client {Client} answered {Status}", Options.Client, (int)response.StatusCode);
            return (false, [], null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(Context.RequestAborted).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: Context.RequestAborted).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("active", out var active) || active.ValueKind != JsonValueKind.True)
            return (false, [], null);

        DateTimeOffset? expiresAt = root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var claims = Tokens.BffTokenClient.ToClaims(root)
            .Where(c => c.Type != "active")
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray(), StringComparer.Ordinal);
        return (true, claims, expiresAt);
    }
}
