namespace Modulus.Identity.Duende;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

/// <summary>
/// Adapter for Duende IdentityServer as an external identity provider.
/// </summary>
public sealed class DuendeIdentityProvider(
    HttpClient http, DuendeOptions opts) : IExternalIdentityProvider
{
    public string Name => "duende";
    public string DisplayName => "Duende IdentityServer";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        var url = $"{opts.Authority}/connect/userinfo";
        var resp = await http.GetFromJsonAsync<JsonElement>(url, ct);

        var claims = new Dictionary<string, string>();
        foreach (var p in resp.EnumerateObject())
            claims[p.Name] = p.Value.GetRawText().Trim('"');

        var sub = resp.TryGetProperty("sub", out var s) ? s.GetString() : subject;

        return new ExternalUserInfo(
            Subject:   sub!,
            Email:     resp.TryGetProperty("email", out var e) ? e.GetString() : null,
            UserName:  resp.TryGetProperty("preferred_username", out var u) ? u.GetString() : null,
            FirstName: resp.TryGetProperty("given_name", out var fn) ? fn.GetString() : null,
            LastName:  resp.TryGetProperty("family_name", out var ln) ? ln.GetString() : null,
            AvatarUrl: null,
            Claims:    claims);
    }

    public async Task<bool> ValidateTokenAsync(
        string token, CancellationToken ct = default)
    {
        try
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            var resp = await http.GetAsync($"{opts.Authority}/connect/userinfo", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}

public sealed class DuendeOptions
{
    public string Authority    { get; set; } = default!;
    public string ClientId     { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string ApiName      { get; set; } = "modulus";
    public string Scope        { get; set; } = "openid profile email roles";
}

public static class DuendeExtensions
{
    public static AuthenticationBuilder AddDuendeIdentityServer(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new DuendeOptions();
        configuration.GetSection("Identity:ExternalProviders:Duende").Bind(opts);

        builder.Services.Configure<DuendeOptions>(
            configuration.GetSection("Identity:ExternalProviders:Duende"));
        builder.Services.AddHttpClient<DuendeIdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, DuendeIdentityProvider>();

        builder.AddOpenIdConnect("Duende", options =>
        {
            options.Authority = opts.Authority;
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            options.Scope.Add(opts.Scope);
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "role";
        });

        return builder;
    }
}
