namespace Modulus.Identity.Authentik;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

public sealed class AuthentikIdentityProvider(
    HttpClient http, AuthentikOptions opts) : IExternalIdentityProvider
{
    public string Name => "authentik";
    public string DisplayName => "Authentik";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.ApiToken);
        var url = $"{opts.Authority}api/v3/core/users/{subject}/";
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
            var resp = await http.GetAsync($"{opts.Authority}application/o/userinfo/", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        if (!el.TryGetProperty("pk", out var pkEl) && !el.TryGetProperty("sub", out pkEl))
            return null;

        var claims = new Dictionary<string, string>();
        foreach (var p in el.EnumerateObject())
            claims[p.Name] = p.Value.GetRawText().Trim('"');

        return new ExternalUserInfo(
            Subject:   pkEl.GetRawText().Trim('"'),
            Email:     el.TryGetProperty("email", out var e) ? e.GetString() : null,
            UserName:  el.TryGetProperty("username", out var u) ? u.GetString() : null,
            FirstName: el.TryGetProperty("name", out var n) ? n.GetString() : null,
            LastName:  null,
            AvatarUrl: null,
            Claims:    claims);
    }
}

public sealed class AuthentikOptions
{
    public string Authority    { get; set; } = default!;
    public string ClientId     { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string? ApiToken    { get; set; }
    public string Scope        { get; set; } = "openid profile email";
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
        });

        return builder;
    }
}
