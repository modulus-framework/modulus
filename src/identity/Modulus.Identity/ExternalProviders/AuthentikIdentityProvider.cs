namespace Modulus.Identity.Authentik;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

/// <summary>
/// Adapter for Authentik as an external identity provider. Validates bearer
/// tokens via Authentik's per-application OIDC discovery document and fetches
/// user details from the REST admin API (<c>/api/v3/core/users/&lt;pk&gt;/</c>).
/// </summary>
public sealed class AuthentikIdentityProvider(
    HttpClient http, AuthentikOptions opts) : IExternalIdentityProvider
{
    private readonly OidcDiscoveryValidator _tokenValidator =
        new($"{opts.Authority}application/o/{opts.ClientId}/.well-known/openid-configuration");

    public string Name => "authentik";
    public string DisplayName => "Authentik";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        // Use a per-request HttpRequestMessage so the Authorization header is
        // never written to HttpClient.DefaultRequestHeaders, which is shared
        // across concurrent calls and would cause a race condition.
        var url = $"{opts.Authority}api/v3/core/users/{subject}/";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.ApiToken);

        using var response = await http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return MapUser(resp);
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
        => _tokenValidator.ValidateAsync(token, ct);

    /// <summary>Authentik exposes only a full <c>name</c> field; first/last are not split.</summary>
    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        if (!el.TryGetProperty("pk", out var pkEl) && !el.TryGetProperty("sub", out pkEl))
            return null;

        var claims = new Dictionary<string, string>();
        foreach (var p in el.EnumerateObject())
            claims[p.Name] = p.Value.GetRawText().Trim('"');

        return new ExternalUserInfo(
            Subject: pkEl.GetRawText().Trim('"'),
            Email: el.TryGetProperty("email", out var e) ? e.GetString() : null,
            UserName: el.TryGetProperty("username", out var u) ? u.GetString() : null,
            FirstName: el.TryGetProperty("name", out var n) ? n.GetString() : null,
            // Authentik exposes only "name"; split-first/split-last isn't available.
            LastName: null,
            AvatarUrl: el.TryGetProperty("avatar", out var av) ? av.GetString() : null,
            Claims: claims);
    }
}

public sealed class AuthentikOptions
{
    public string Authority { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string? ApiToken { get; set; }
    public string Scope { get; set; } = "openid profile email";
}

public static class AuthentikExtensions
{
    public static AuthenticationBuilder AddAuthentik(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new AuthentikOptions();
        configuration.GetSection("Identity:ExternalProviders:Authentik").Bind(opts);

        builder.Services.Configure<AuthentikOptions>(
            configuration.GetSection("Identity:ExternalProviders:Authentik"));
        builder.Services.AddHttpClient<AuthentikIdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, AuthentikIdentityProvider>();

        builder.AddOpenIdConnect("Authentik", options =>
        {
            options.Authority = opts.Authority;
            options.MetadataAddress = $"{opts.Authority}application/o/{opts.ClientId}/.well-known/openid-configuration";
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            options.Scope.Add(opts.Scope);
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.UseTokenLifetime = true;
            // Prevent the legacy JwtSecurityTokenHandler claim-rewriting that
            // drops/duplicates claims during inbound mapping.
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            // Authentik emits group memberships as a "groups" claim.
            options.TokenValidationParameters.RoleClaimType = "groups";
        });

        return builder;
    }
}
