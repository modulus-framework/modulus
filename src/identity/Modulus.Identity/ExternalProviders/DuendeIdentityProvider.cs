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
    private readonly OidcDiscoveryValidator _tokenValidator =
        OidcDiscoveryValidatorCache.GetOrCreate(
            $"{opts.Authority.TrimEnd('/')}/.well-known/openid-configuration",
            opts.Audience is null ? null : [opts.Audience]);

    public string Name => "duende";
    public string DisplayName => "Duende IdentityServer";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        // Duende ships no admin REST API for looking an arbitrary subject up;
        // /connect/userinfo returns the claims carried by the presented access
        // token. The call therefore must be AUTHENTICATED (previously it sent
        // no credentials at all and always failed with 401). Send a
        // client-credentials bearer token and treat transport failures as
        // "user not found" rather than throwing, matching the other adapters'
        // nullable-return contract.
        var token = await GetAccessTokenAsync(ct);
        if (token is null) return null;

        try
        {
            var url = $"{opts.Authority.TrimEnd('/')}/connect/userinfo";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
            var resp = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

            var claims = new Dictionary<string, string>();
            foreach (var p in resp.EnumerateObject())
                claims[p.Name] = p.Value.GetRawText().Trim('"');

            var sub = resp.TryGetProperty("sub", out var s) ? s.GetString() : subject;

            return new ExternalUserInfo(
                Subject: sub!,
                Email: resp.TryGetProperty("email", out var e) ? e.GetString() : null,
                UserName: resp.TryGetProperty("preferred_username", out var u) ? u.GetString() : null,
                FirstName: resp.TryGetProperty("given_name", out var fn) ? fn.GetString() : null,
                LastName: resp.TryGetProperty("family_name", out var ln) ? ln.GetString() : null,
                AvatarUrl: null,
                Claims: claims);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
        => _tokenValidator.ValidateAsync(token, ct);

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = opts.ClientId,
            ["client_secret"] = opts.ClientSecret,
            ["scope"] = opts.ApiName,
            ["grant_type"] = "client_credentials",
        });

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{opts.Authority.TrimEnd('/')}/connect/token")
        {
            Content = content,
        };

        using var response = await http.SendAsync(req, ct);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.TryGetProperty("access_token", out var tokenEl)
            ? tokenEl.GetString()
            : null;
    }
}

public sealed class DuendeOptions
{
    public string Authority { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string ApiName { get; set; } = "modulus";
    public string Scope { get; set; } = "openid profile email roles";

    /// <summary>
    /// Expected audience (<c>aud</c>) of access tokens issued for this
    /// application (typically the API resource name / <see cref="ApiName"/>).
    /// Configured locally so bearer tokens minted for any other client are
    /// rejected. When null, audience validation is skipped — recommended to
    /// set in production.
    /// </summary>
    public string? Audience { get; set; }
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
