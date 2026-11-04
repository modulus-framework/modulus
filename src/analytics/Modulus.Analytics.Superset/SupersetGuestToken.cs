namespace Modulus.Analytics.Superset;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

/// <summary>A guest token for one embedded dashboard, limited to one company's rows.</summary>
/// <param name="Token">The token to hand to the embedding page (the Superset embedded SDK takes it as-is).</param>
/// <param name="Username">The guest user the token names. It carries no personal data: it is derived from the company id.</param>
public sealed record SupersetGuestToken(string Token, string Username);

/// <summary>Requests guest tokens from Superset.</summary>
public interface ISupersetGuestTokens
{
    /// <summary>Creates a token that shows <paramref name="dashboardId"/> for one company only.</summary>
    /// <param name="tenantId">The company.</param>
    /// <param name="dashboardId">The Superset dashboard id (a number, a uuid or a slug).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException">The dashboard id is not a plain identifier.</exception>
    /// <exception cref="HttpRequestException">Superset refused the sign-in or the token request.</exception>
    Task<SupersetGuestToken> CreateAsync(Guid tenantId, string dashboardId, CancellationToken cancellationToken);
}

/// <summary>
/// Signs in to Superset as the service account and asks for a guest token whose row-level-security rule is <c>{TenantColumn} = '{tenant}'</c>. The tenant is a
/// validated GUID, so the rule cannot carry anything but a company id. No token is cached: each request gets a fresh one, and Superset sets its lifetime.
/// </summary>
internal sealed partial class SupersetGuestTokenService(IHttpClientFactory httpClients, IOptions<SupersetOptions> options) : ISupersetGuestTokens
{
    /// <summary>The name of the HTTP client that calls Superset.</summary>
    public const string HttpClientName = "Modulus.Analytics.Superset";

    public async Task<SupersetGuestToken> CreateAsync(Guid tenantId, string dashboardId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(dashboardId);
        if (!DashboardIdPattern().IsMatch(dashboardId))
        {
            throw new ArgumentException("A dashboard id is 1 to 64 letters, digits, dashes or underscores.", nameof(dashboardId));
        }

        var settings = options.Value;
        if (settings.BaseUrl is null)
        {
            throw new InvalidOperationException("Analytics:Superset:BaseUrl is not set.");
        }

        using var client = httpClients.CreateClient(HttpClientName);
        var accessToken = await SignInAsync(client, settings, cancellationToken).ConfigureAwait(false);

        var username = $"tenant-{tenantId:N}";
        var request = new GuestTokenRequest(
            User: new GuestUser(username, "Company", tenantId.ToString("N")),
            Resources: [new GuestResource("dashboard", dashboardId)],
            Rls: [new RlsRule($"{settings.TenantColumn} = '{tenantId:D}'")]);

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, "api/v1/security/guest_token/"))
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GuestTokenResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException("Superset returned no guest token.");
        return new SupersetGuestToken(body.Token, username);
    }

    private static async Task<string> SignInAsync(HttpClient client, SupersetOptions settings, CancellationToken cancellationToken)
    {
        var login = new LoginRequest(settings.Username ?? string.Empty, settings.Password ?? string.Empty, "db", false);
        using var response = await client.PostAsJsonAsync(new Uri(settings.BaseUrl!, "api/v1/security/login"), login, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The status only: the body and the request are never echoed, so the password cannot reach a log.
            throw new HttpRequestException($"Superset refused the sign-in of the service account (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        }

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken).ConfigureAwait(false);
        return body?.AccessToken ?? throw new HttpRequestException("Superset returned no access token.");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DashboardIdPattern();

    private sealed record LoginRequest(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("refresh")] bool Refresh);

    private sealed record LoginResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record GuestUser(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName);

    private sealed record GuestResource(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] string Id);

    private sealed record RlsRule([property: JsonPropertyName("clause")] string Clause);

    private sealed record GuestTokenRequest(
        [property: JsonPropertyName("user")] GuestUser User,
        [property: JsonPropertyName("resources")] IReadOnlyList<GuestResource> Resources,
        [property: JsonPropertyName("rls")] IReadOnlyList<RlsRule> Rls);

    private sealed record GuestTokenResponse([property: JsonPropertyName("token")] string Token);
}
