namespace Modulus.Bff.Authentication;

using System.Text.Json;

/// <summary>The auth server endpoints the BFF uses, from OIDC discovery plus configured overrides.</summary>
public sealed record BffAuthServerEndpoints(
    string? Issuer,
    string? TokenEndpoint,
    string? RevocationEndpoint,
    string? UserInfoEndpoint,
    string? IntrospectionEndpoint,
    string? EndSessionEndpoint);

/// <summary>Resolves the auth server's endpoints through OIDC discovery.</summary>
public interface IBffDiscovery
{
    /// <summary>Returns the endpoints; the discovery document is cached.</summary>
    Task<BffAuthServerEndpoints> GetEndpointsAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads <c>{Authority}/.well-known/openid-configuration</c> once an hour (on failure, retried on
/// the next call), so every standards-compliant server works without per-product paths. Explicit
/// <c>Bff:*Endpoint</c> settings win over discovered values; relative settings resolve against the authority.
/// </summary>
internal sealed class BffDiscovery(
    IHttpClientFactory httpClientFactory,
    IOptions<BffOptions> options,
    TimeProvider clock) : IBffDiscovery
{
    private static readonly TimeSpan s_refreshInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BffAuthServerEndpoints? _cached;
    private DateTimeOffset _expires;

    public async Task<BffAuthServerEndpoints> GetEndpointsAsync(CancellationToken ct = default)
    {
        if (_cached is { } cached && clock.GetUtcNow() < _expires)
            return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is { } again && clock.GetUtcNow() < _expires)
                return again;

            _cached = await LoadAsync(ct).ConfigureAwait(false);
            _expires = clock.GetUtcNow() + s_refreshInterval;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BffAuthServerEndpoints> LoadAsync(CancellationToken ct)
    {
        var bff = options.Value;
        var authority = bff.ResolveAuthority()
            ?? throw new InvalidOperationException("Bff:Authority is not configured (and there is no 'api' service address to fall back to).");
        var baseUri = new Uri(authority.EndsWith('/') ? authority : authority + "/");

        var http = httpClientFactory.CreateClient(BffDefaults.AuthorityHttpClient);
        using var response = await http.GetAsync(new Uri(baseUri, ".well-known/openid-configuration"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        return new BffAuthServerEndpoints(
            Issuer: Read(root, "issuer"),
            TokenEndpoint: Resolve(baseUri, bff.TokenEndpoint) ?? Read(root, "token_endpoint"),
            RevocationEndpoint: Resolve(baseUri, bff.RevocationEndpoint) ?? Read(root, "revocation_endpoint"),
            UserInfoEndpoint: Resolve(baseUri, bff.UserInfoEndpoint) ?? Read(root, "userinfo_endpoint"),
            IntrospectionEndpoint: Resolve(baseUri, bff.IntrospectionEndpoint) ?? Read(root, "introspection_endpoint"),
            EndSessionEndpoint: Resolve(baseUri, bff.EndSessionEndpoint) ?? Read(root, "end_session_endpoint"));
    }

    private static string? Read(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Resolve(Uri baseUri, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        return Uri.TryCreate(configured, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(baseUri, configured.TrimStart('/')).ToString();
    }
}
