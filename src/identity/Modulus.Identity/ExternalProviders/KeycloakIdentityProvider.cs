namespace Modulus.Identity.Keycloak;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

/// <summary>
/// Adapter for Keycloak as an external identity provider.
/// </summary>
public sealed class KeycloakIdentityProvider(
    HttpClient http, KeycloakOptions opts) : IExternalIdentityProviderWithProfileFetch
{
    public string Name => "keycloak";
    public string DisplayName => "Keycloak";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        var adminToken = await GetAdminTokenAsync(ct);
        if (adminToken is null) return null;

        // Use a per-request HttpRequestMessage so the Authorization header is
        // never written to HttpClient.DefaultRequestHeaders, which is shared
        // across concurrent calls and would cause a race condition.
        var url = $"{opts.Authority}/admin/realms/{opts.Realm}/users/{subject}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        using var response = await http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return MapUser(resp);
    }

    public async Task<bool> ValidateTokenAsync(
        string token, CancellationToken ct = default)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = token,
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret,
            });

            var resp = await http.PostAsync(
                $"{opts.Authority}/realms/{opts.Realm}/protocol/openid-connect/token/introspect",
                content, ct);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            return body.TryGetProperty("active", out var active) && active.GetBoolean();
        }
        catch { return false; }
    }

    private async Task<string?> GetAdminTokenAsync(CancellationToken ct)
    {
        if (opts.AdminClientId is null || opts.AdminClientSecret is null)
            return null;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = opts.AdminClientId,
            ["client_secret"] = opts.AdminClientSecret,
        });

        var resp = await http.PostAsync(
            $"{opts.Authority}/realms/{opts.Realm}/protocol/openid-connect/token",
            content, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("access_token").GetString();
    }

    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var idEl)) return null;

        var claims = new Dictionary<string, string>();
        foreach (var p in el.EnumerateObject())
            claims[p.Name] = p.Value.GetRawText().Trim('"');

        return new ExternalUserInfo(
            Subject: idEl.GetString()!,
            Email: el.TryGetProperty("email", out var e) ? e.GetString() : null,
            UserName: el.TryGetProperty("username", out var u) ? u.GetString() : null,
            FirstName: el.TryGetProperty("firstName", out var fn) ? fn.GetString() : null,
            LastName: el.TryGetProperty("lastName", out var ln) ? ln.GetString() : null,
            AvatarUrl: null,
            Claims: claims);
    }
}

public sealed class KeycloakOptions
{
    public string Authority { get; set; } = default!;
    public string Realm { get; set; } = "master";
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string? AdminClientId { get; set; }
    public string? AdminClientSecret { get; set; }
    public string Scope { get; set; } = "openid profile email roles";
    public string FullAuthority => $"{Authority.TrimEnd('/')}/realms/{Realm}";
}

public static class KeycloakExtensions
{
    /// <summary>
    /// Register Keycloak for OIDC sign-in and token validation only (recommended).
    /// User profile is derived from standard OIDC claims in the token.
    /// No admin credentials required.
    /// </summary>
    public static AuthenticationBuilder AddKeycloak(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new KeycloakOptions();
        configuration.GetSection("Identity:ExternalProviders:Keycloak").Bind(opts);

        builder.Services.Configure<KeycloakOptions>(
            configuration.GetSection("Identity:ExternalProviders:Keycloak"));
        builder.Services.AddHttpClient<KeycloakIdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, KeycloakIdentityProvider>();

        builder.AddOpenIdConnect("Keycloak", options =>
        {
            options.Authority = opts.FullAuthority;
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            options.Scope.Add(opts.Scope);
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.TokenValidationParameters.RoleClaimType = "realm_access.roles";
        });

        return builder;
    }

    /// <summary>
    /// Register Keycloak with profile-fetch capability. This is an opt-in method
    /// that additionally enables <see cref="IExternalIdentityProviderWithProfileFetch"/>.
    ///
    /// SECURITY WARNING: This method requires storing Keycloak admin credentials in config.
    /// For production environments, prefer AddKeycloak() which derives user profiles from
    /// standard OIDC claims instead.
    /// </summary>
    public static AuthenticationBuilder AddKeycloakWithProfileFetch(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        AddKeycloak(builder, configuration);
        builder.Services.AddScoped<IExternalIdentityProviderWithProfileFetch, KeycloakIdentityProvider>();
        return builder;
    }
}
