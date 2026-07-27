namespace Modulus.Identity.Okta;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

/// <summary>
/// Adapter for Okta as an external identity provider.
/// </summary>
public sealed class OktaIdentityProvider(
    HttpClient http, OktaOptions opts) : IExternalIdentityProvider
{
    private readonly OidcDiscoveryValidator _tokenValidator =
        new($"{opts.Authority.TrimEnd('/')}/oauth2/default/.well-known/openid-configuration");

    public string Name => "okta";
    public string DisplayName => "Okta";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        // Use a per-request HttpRequestMessage so the Authorization header is
        // never written to HttpClient.DefaultRequestHeaders, which is shared
        // across concurrent calls and would cause a race condition.
        var url = $"{opts.Authority}/api/v1/users/{subject}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("SSWS", opts.ApiToken);

        using var response = await http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return MapUser(resp);
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
        => _tokenValidator.ValidateAsync(token, ct);

    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var idEl)) return null;

        var profile = el.TryGetProperty("profile", out var p) ? p : default;

        var claims = new Dictionary<string, string>();
        if (el.ValueKind == JsonValueKind.Object)
            foreach (var prop in el.EnumerateObject())
                claims[prop.Name] = prop.Value.GetRawText().Trim('"');

        return new ExternalUserInfo(
            Subject: idEl.GetString()!,
            Email: profile.ValueKind != JsonValueKind.Undefined && profile.TryGetProperty("email", out var e) ? e.GetString() : null,
            UserName: profile.ValueKind != JsonValueKind.Undefined && profile.TryGetProperty("login", out var l) ? l.GetString() : null,
            FirstName: profile.ValueKind != JsonValueKind.Undefined && profile.TryGetProperty("firstName", out var fn) ? fn.GetString() : null,
            LastName: profile.ValueKind != JsonValueKind.Undefined && profile.TryGetProperty("lastName", out var ln) ? ln.GetString() : null,
            AvatarUrl: null,
            Claims: claims);
    }
}

public sealed class OktaOptions
{
    public string Authority { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string? ApiToken { get; set; }
    public string Scope { get; set; } = "openid profile email";
}

public static class OktaExtensions
{
    public static AuthenticationBuilder AddOkta(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new OktaOptions();
        configuration.GetSection("Identity:ExternalProviders:Okta").Bind(opts);

        builder.Services.Configure<OktaOptions>(
            configuration.GetSection("Identity:ExternalProviders:Okta"));
        builder.Services.AddHttpClient<OktaIdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, OktaIdentityProvider>();

        builder.AddOpenIdConnect("Okta", options =>
        {
            options.Authority = $"{opts.Authority}/oauth2/default";
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            options.Scope.Add(opts.Scope);
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
        });

        return builder;
    }
}
