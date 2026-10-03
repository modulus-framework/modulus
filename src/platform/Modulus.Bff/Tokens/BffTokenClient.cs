namespace Modulus.Bff.Tokens;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Modulus.Bff.Authentication;

/// <summary>The outcome of a token request.</summary>
public sealed record BffTokenResult(BffUserTokens? Tokens, string? Error, string? ErrorDescription)
{
    public bool Succeeded => Tokens is not null;
}

/// <summary>Talks to the auth server's token, revocation and userinfo endpoints (any OIDC server).</summary>
public interface IBffTokenClient
{
    /// <summary>Resource-owner password grant (for web clients in <see cref="BffLoginMode.Password"/>).</summary>
    Task<BffTokenResult> PasswordAsync(string client, string userName, string password, CancellationToken ct = default);

    /// <summary>Refresh-token grant.</summary>
    Task<BffTokenResult> RefreshAsync(string client, string refreshToken, CancellationToken ct = default);

    /// <summary>RFC 7009 revocation; a no-op when the server has no revocation endpoint (Entra ID).</summary>
    Task RevokeAsync(string client, string token, string tokenTypeHint, CancellationToken ct = default);

    /// <summary>The userinfo claims for <paramref name="accessToken"/>; arrays become one claim per value.</summary>
    Task<IReadOnlyList<Claim>> GetUserInfoAsync(string accessToken, CancellationToken ct = default);
}

internal sealed class BffTokenClient(
    IHttpClientFactory httpClientFactory,
    IBffDiscovery discovery,
    IOptionsMonitor<BffClientOptions> clients,
    TimeProvider clock,
    ILogger<BffTokenClient> logger) : IBffTokenClient
{
    public Task<BffTokenResult> PasswordAsync(string client, string userName, string password, CancellationToken ct = default)
    {
        var options = clients.Get(client);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = userName,
            ["password"] = password,
            ["scope"] = string.Join(' ', options.Scopes),
        };
        return RequestAsync(client, options, form, previousRefreshToken: null, ct);
    }

    public Task<BffTokenResult> RefreshAsync(string client, string refreshToken, CancellationToken ct = default)
    {
        var options = clients.Get(client);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        return RequestAsync(client, options, form, refreshToken, ct);
    }

    public async Task RevokeAsync(string client, string token, string tokenTypeHint, CancellationToken ct = default)
    {
        var endpoints = await discovery.GetEndpointsAsync(ct).ConfigureAwait(false);
        if (endpoints.RevocationEndpoint is null)
            return;

        var options = clients.Get(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.RevocationEndpoint)
        {
            Content = Form(options, new Dictionary<string, string> { ["token"] = token, ["token_type_hint"] = tokenTypeHint }),
        };
        AuthenticateClient(request, options);
        try
        {
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Token revocation for BFF client {Client} answered {Status}", client, (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Token revocation for BFF client {Client} failed", client);
        }
    }

    public async Task<IReadOnlyList<Claim>> GetUserInfoAsync(string accessToken, CancellationToken ct = default)
    {
        var endpoints = await discovery.GetEndpointsAsync(ct).ConfigureAwait(false);
        if (endpoints.UserInfoEndpoint is null)
            return [];

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoints.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return [];

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return ToClaims(doc.RootElement);
    }

    /// <summary>Flattens a JSON claims object: arrays become repeated claims, objects stay JSON.</summary>
    internal static List<Claim> ToClaims(JsonElement root)
    {
        var claims = new List<Claim>();
        if (root.ValueKind != JsonValueKind.Object)
            return claims;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in property.Value.EnumerateArray())
                        claims.Add(new Claim(property.Name, item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText()));
                    break;
                case JsonValueKind.String:
                    claims.Add(new Claim(property.Name, property.Value.GetString()!));
                    break;
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    break;
                default:
                    claims.Add(new Claim(property.Name, property.Value.GetRawText()));
                    break;
            }
        }

        return claims;
    }

    private HttpClient Http => httpClientFactory.CreateClient(BffDefaults.AuthorityHttpClient);

    private async Task<BffTokenResult> RequestAsync(
        string client, BffClientOptions options, Dictionary<string, string> form, string? previousRefreshToken, CancellationToken ct)
    {
        var endpoints = await discovery.GetEndpointsAsync(ct).ConfigureAwait(false);
        if (endpoints.TokenEndpoint is null)
            return new BffTokenResult(null, "server_error", "The auth server publishes no token endpoint.");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.TokenEndpoint) { Content = Form(options, form) };
        AuthenticateClient(request, options);

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        JsonElement root;
        try
        {
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new BffTokenResult(null, "server_error", $"The token endpoint answered {(int)response.StatusCode} without JSON.");
        }

        if (!response.IsSuccessStatusCode || !root.TryGetProperty("access_token", out var access) || access.GetString() is not { Length: > 0 } accessToken)
            return new BffTokenResult(null, Str(root, "error") ?? "invalid_grant", Str(root, "error_description"));

        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        return new BffTokenResult(
            new BffUserTokens(
                accessToken,
                Str(root, "refresh_token") ?? previousRefreshToken, // servers without rotation return no new one
                clock.GetUtcNow().AddSeconds(expiresIn),
                Str(root, "id_token")),
            null,
            null);
    }

    /// <summary>
    /// Public clients send <c>client_id</c> in the form; confidential clients use HTTP Basic
    /// (<c>client_secret_basic</c>, the method every supported server accepts).
    /// </summary>
    private static FormUrlEncodedContent Form(BffClientOptions options, Dictionary<string, string> form)
    {
        if (options.ClientId is { Length: > 0 } clientId && string.IsNullOrEmpty(options.ClientSecret))
            form["client_id"] = clientId;
        foreach (var (key, value) in options.AuthorizationParameters)
            form.TryAdd(key, value);
        return new FormUrlEncodedContent(form);
    }

    private static void AuthenticateClient(HttpRequestMessage request, BffClientOptions options)
    {
        if (options.ClientId is not { Length: > 0 } id || options.ClientSecret is not { Length: > 0 } secret)
            return;
        var raw = Uri.EscapeDataString(id) + ":" + Uri.EscapeDataString(secret);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
    }

    private static string? Str(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
