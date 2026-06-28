namespace Modulus.Identity.Auth0;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

public sealed class Auth0IdentityProvider(
    HttpClient http,
    Auth0Options opts) : IExternalIdentityProvider
{
    private readonly OidcDiscoveryValidator _tokenValidator =
        new($"{opts.Authority}.well-known/openid-configuration");

    public string Name => "auth0";
    public string DisplayName => "Auth0";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.ManagementToken);

        var url = $"{opts.Authority}api/v2/users/{subject}";
        var resp = await http.GetFromJsonAsync<JsonElement>(url, ct);
        return MapUser(resp);
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
        => _tokenValidator.ValidateAsync(token, ct);

    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        if (!el.TryGetProperty("user_id", out var subEl)) return null;

        var claims = new Dictionary<string, string>();
        foreach (var p in el.EnumerateObject())
            claims[p.Name] = p.Value.GetRawText().Trim('"');

        return new ExternalUserInfo(
            Subject: subEl.GetString()!,
            Email: el.TryGetProperty("email", out var e) ? e.GetString() : null,
            UserName: el.TryGetProperty("nickname", out var n) ? n.GetString() : null,
            FirstName: el.TryGetProperty("given_name", out var fn) ? fn.GetString() : null,
            LastName: el.TryGetProperty("family_name", out var ln) ? ln.GetString() : null,
            AvatarUrl: el.TryGetProperty("picture", out var pic) ? pic.GetString() : null,
            Claims: claims);
    }
}

public sealed class Auth0Options
{
    public string Authority { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string? ManagementToken { get; set; }
    public string Scope { get; set; } = "openid profile email";
}

public static class Auth0Extensions
{
    public static AuthenticationBuilder AddAuth0(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new Auth0Options();
        configuration.GetSection("Identity:ExternalProviders:Auth0").Bind(opts);

        builder.Services.Configure<Auth0Options>(
            configuration.GetSection("Identity:ExternalProviders:Auth0"));
        builder.Services.AddHttpClient<Auth0IdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, Auth0IdentityProvider>();

        builder.AddOpenIdConnect("Auth0", options =>
        {
            options.Authority = opts.Authority;
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            options.Scope.Add(opts.Scope);
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "nickname";
            options.TokenValidationParameters.RoleClaimType = "https://schemas.modulus.app/roles";
        });

        return builder;
    }
}
