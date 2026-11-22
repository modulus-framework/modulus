namespace Modulus.Identity.Casdoor;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

/// <summary>
/// Adapter for Casdoor as an external identity provider. Validates bearer tokens
/// through Casdoor's OIDC discovery document (<c>/.well-known/openid-configuration</c>)
/// and, when opted in, reads user details from Casdoor's REST API
/// (<c>/api/get-user</c>) authenticated with the application's client id and secret.
/// </summary>
public sealed class CasdoorIdentityProvider(
    HttpClient http, CasdoorOptions opts) : IExternalIdentityProviderWithProfileFetch
{
    private readonly OidcDiscoveryValidator _tokenValidator =
        OidcDiscoveryValidatorCache.GetOrCreate(
            CasdoorExtensions.MetadataAddress(opts),
            // Casdoor sets aud to the application's client id.
            opts.Audience is null ? null : [opts.Audience]);

    public string Name => "casdoor";
    public string DisplayName => "Casdoor";

    public async Task<ExternalUserInfo?> GetUserBySubjectAsync(
        string subject, CancellationToken ct = default)
    {
        // Per-request message: the Authorization header must never land on the
        // shared HttpClient's default headers (concurrent calls would race).
        var url = $"{opts.Authority.TrimEnd('/')}/api/get-user?userId={Uri.EscapeDataString(subject)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return MapUser(resp);
    }

    public Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
        => _tokenValidator.ValidateAsync(token, ct);

    private static ExternalUserInfo? MapUser(JsonElement el)
    {
        // Casdoor wraps results as { "status": "ok", "data": { ... } }; a failed
        // lookup is { "status": "error" } or "data": null.
        if (el.ValueKind != JsonValueKind.Object)
            return null;
        if (el.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            && !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            return null;
        if (el.TryGetProperty("data", out var data))
        {
            if (data.ValueKind != JsonValueKind.Object)
                return null;
            el = data;
        }

        var subject = Str(el, "id");
        if (string.IsNullOrEmpty(subject))
            return null;

        var claims = new Dictionary<string, string>();
        foreach (var p in el.EnumerateObject())
            claims[p.Name] = p.Value.ValueKind == JsonValueKind.String
                ? p.Value.GetString() ?? ""
                : p.Value.GetRawText();

        return new ExternalUserInfo(
            Subject: subject,
            Email: Str(el, "email"),
            UserName: Str(el, "name"),
            FirstName: Str(el, "firstName") ?? Str(el, "displayName"),
            LastName: Str(el, "lastName"),
            AvatarUrl: Str(el, "avatar"),
            Claims: claims);
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           && !string.IsNullOrEmpty(v.GetString())
            ? v.GetString()
            : null;
}

public sealed class CasdoorOptions
{
    /// <summary>The Casdoor endpoint, for example <c>https://door.example.com</c> (the OIDC issuer).</summary>
    public string Authority { get; set; } = default!;

    /// <summary>The application's client id.</summary>
    public string ClientId { get; set; } = default!;

    /// <summary>The application's client secret (a secret: user secrets or environment).</summary>
    public string ClientSecret { get; set; } = default!;

    /// <summary>
    /// Optional application name. When set, discovery is read from
    /// <c>/.well-known/{application}/openid-configuration</c>, which advertises that
    /// application's own signing certificate. Leave null to use the global document.
    /// </summary>
    public string? Application { get; set; }

    public string Scope { get; set; } = "openid profile email";

    /// <summary>
    /// Expected audience (<c>aud</c>) of access tokens: Casdoor uses the application's
    /// client id. When null, audience validation is skipped — set it in production.
    /// </summary>
    public string? Audience { get; set; }
}

public static class CasdoorExtensions
{
    internal static string MetadataAddress(CasdoorOptions opts)
    {
        var root = opts.Authority.TrimEnd('/');
        return string.IsNullOrWhiteSpace(opts.Application)
            ? $"{root}/.well-known/openid-configuration"
            : $"{root}/.well-known/{Uri.EscapeDataString(opts.Application)}/openid-configuration";
    }

    /// <summary>
    /// Register Casdoor for OIDC sign-in and token validation only (recommended).
    /// The user profile is derived from standard OIDC claims; no API credentials
    /// beyond the application's own client are used.
    /// </summary>
    public static AuthenticationBuilder AddCasdoor(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var opts = new CasdoorOptions();
        configuration.GetSection("Identity:ExternalProviders:Casdoor").Bind(opts);

        builder.Services.Configure<CasdoorOptions>(
            configuration.GetSection("Identity:ExternalProviders:Casdoor"));
        // The provider takes the raw options in its constructor, so the bound
        // value must also be resolvable (container validation in Development).
        builder.Services.AddSingleton(opts);
        builder.Services.AddHttpClient<CasdoorIdentityProvider>();
        builder.Services.AddScoped<IExternalIdentityProvider, CasdoorIdentityProvider>();

        builder.AddOpenIdConnect("Casdoor", options =>
        {
            options.Authority = opts.Authority;
            options.MetadataAddress = MetadataAddress(opts);
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.ResponseType = "code";
            foreach (var scope in opts.Scope.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries))
            {
                options.Scope.Add(scope);
            }
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = true;
            options.UseTokenLifetime = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            // Casdoor emits the user's roles as a "roles" claim.
            options.TokenValidationParameters.RoleClaimType = "roles";
        });

        return builder;
    }

    /// <summary>
    /// Register Casdoor with profile-fetch capability (opt-in). It calls Casdoor's
    /// <c>/api/get-user</c> with the application's client id and secret, so the secret
    /// must be present at runtime. Prefer <see cref="AddCasdoor"/> where the OIDC claims suffice.
    /// </summary>
    public static AuthenticationBuilder AddCasdoorWithProfileFetch(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        AddCasdoor(builder, configuration);
        builder.Services.AddScoped<IExternalIdentityProviderWithProfileFetch, CasdoorIdentityProvider>();
        return builder;
    }
}
