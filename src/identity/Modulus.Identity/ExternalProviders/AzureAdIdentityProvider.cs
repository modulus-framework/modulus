namespace Modulus.Identity.AzureAd;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

public sealed class AzureAdIdentityProvider(
    HttpClient http, AzureAdOptions opts) : IExternalIdentityProvider
{
    private readonly OidcDiscoveryValidator _tokenValidator =
        OidcDiscoveryValidatorCache.GetOrCreate(
            $"{opts.Authority}/.well-known/openid-configuration",
            opts.Audience is null ? null : [opts.Audience]);

    public string Name => "azuread";
    public string DisplayName => "Microsoft Entra ID";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        if (token is null) return null;

        // Use a per-request HttpRequestMessage so the Authorization header is
        // never written to HttpClient.DefaultRequestHeaders, which is shared
        // across concurrent calls and would cause a race condition.
        var url = $"{opts.GraphApiBaseUrl}users/{Uri.EscapeDataString(subject)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return MapUser(resp);
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
        => _tokenValidator.ValidateAsync(token, ct);

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        // Client credentials flow for Graph API access
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials",
        });

        var resp = await http.PostAsync(
            BuildTokenEndpoint(opts.Authority), content, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("access_token").GetString();
    }

    /// <summary>
    /// Builds the v2.0 token endpoint from the authority. The bound
    /// <see cref="AzureAdOptions.Authority"/> ends with <c>/v2.0</c> (no
    /// trailing slash); the token endpoint lives directly under the tenant
    /// root, so simply concatenating produces
    /// <c>.../&lt;tenant&gt;/v2.0oauth2/v2.0/token</c> — a 404 on every call.
    /// </summary>
    private static string BuildTokenEndpoint(string authority)
    {
        var tenantRoot = authority.TrimEnd('/');
        const string V2Suffix = "/v2.0";
        if (tenantRoot.EndsWith(V2Suffix, StringComparison.OrdinalIgnoreCase))
            tenantRoot = tenantRoot[..^V2Suffix.Length];
        return $"{tenantRoot}/oauth2/v2.0/token";
    }

    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var idEl)) return null;

        var claims = new Dictionary<string, string>();
        foreach (var p in el.EnumerateObject())
            claims[p.Name] = p.Value.GetRawText().Trim('"');

        return new ExternalUserInfo(
            Subject: idEl.GetString()!,
            Email: el.TryGetProperty("mail", out var m) ? m.GetString()
                     : el.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null,
            UserName: el.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null,
            FirstName: el.TryGetProperty("givenName", out var fn) ? fn.GetString() : null,
            LastName: el.TryGetProperty("surname", out var ln) ? ln.GetString() : null,
            AvatarUrl: null,
            Claims: claims);
    }
}

public sealed class AzureAdOptions
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string GraphApiBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";
    public string Authority => $"{Instance}{TenantId}/v2.0";
    public string Scope { get; set; } = "openid profile email User.Read";

    /// <summary>
    /// Expected audience (<c>aud</c>) of access tokens — for Entra ID access
    /// tokens this is usually the client id of this application. Configured
    /// locally so bearer tokens minted for any other client are rejected.
    /// When null, audience validation is skipped — recommended to set in
    /// production.
    /// </summary>
    public string? Audience { get; set; }
}

public static class AzureAdExtensions
{
    public static AuthenticationBuilder AddAzureAd(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new AzureAdOptions();
        configuration.GetSection("Identity:ExternalProviders:AzureAd").Bind(opts);

        builder.Services.Configure<AzureAdOptions>(
            configuration.GetSection("Identity:ExternalProviders:AzureAd"));
        builder.Services.AddHttpClient<AzureAdIdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, AzureAdIdentityProvider>();

        builder.AddOpenIdConnect("AzureAd", options =>
        {
            options.Authority = opts.Authority;
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            options.UseTokenLifetime = true;
            options.SaveTokens = true;
            options.Scope.Add(opts.Scope);
            options.GetClaimsFromUserInfoEndpoint = true;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";
        });

        return builder;
    }
}
