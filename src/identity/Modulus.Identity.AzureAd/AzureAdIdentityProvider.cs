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
    public string Name => "azuread";
    public string DisplayName => "Microsoft Entra ID";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        if (token is null) return null;

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var url = $"{opts.GraphApiBaseUrl}users/{subject}";
        var resp = await http.GetFromJsonAsync<JsonElement>(url, ct);
        return MapUser(resp);
    }

    public async Task<bool> ValidateTokenAsync(
        string token, CancellationToken ct = default)
    {
        try
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            var resp = await http.GetAsync($"{opts.Authority}v1.0/me", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        // Client credentials flow for Graph API access
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["scope"]         = "https://graph.microsoft.com/.default",
            ["grant_type"]    = "client_credentials",
        });

        var resp = await http.PostAsync(
            $"{opts.Authority}oauth2/v2.0/token", content, ct);
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
            Subject:   idEl.GetString()!,
            Email:     el.TryGetProperty("mail", out var m) ? m.GetString()
                     : el.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null,
            UserName:  el.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null,
            FirstName: el.TryGetProperty("givenName", out var fn) ? fn.GetString() : null,
            LastName:  el.TryGetProperty("surname", out var ln) ? ln.GetString() : null,
            AvatarUrl: null,
            Claims:    claims);
    }
}

public sealed class AzureAdOptions
{
    public string Instance        { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId        { get; set; } = default!;
    public string ClientId        { get; set; } = default!;
    public string ClientSecret    { get; set; } = default!;
    public string GraphApiBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";
    public string Authority => $"{Instance}{TenantId}/v2.0";
    public string Scope { get; set; } = "openid profile email User.Read";
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
