namespace Modulus.Identity.Abstractions;

using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Validates bearer JWTs issued by an external OpenID Connect provider using the
/// provider's published signing keys (JWKS via discovery), issuer, lifetime, and
/// (optionally) audience. This replaces the insecure "GET userinfo and treat 200
/// as valid" pattern, which validated nothing locally, relied entirely on a
/// remote round-trip, and mutated the shared <c>HttpClient</c>'s auth header.
/// </summary>
/// <remarks>
/// The issuer is taken from the discovery document itself (which the caller
/// trusts by pointing <c>metadataAddress</c> at the provider's HTTPS well-known
/// endpoint). Audience validation is performed only when <c>validAudiences</c>
/// is non-empty — enabling it is recommended for production so that tokens
/// minted for one client/resource cannot be used against another.
/// </remarks>
public sealed class OidcDiscoveryValidator
{
    private readonly Microsoft.IdentityModel.Protocols.ConfigurationManager<OpenIdConnectConfiguration> _config;
    private readonly HashSet<string> _validAudiences;
    private readonly bool _validateAudience;

    public OidcDiscoveryValidator(
        string metadataAddress,
        IEnumerable<string>? validAudiences = null,
        System.Net.Http.HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataAddress);

        _validAudiences = (validAudiences ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet();
        _validateAudience = _validAudiences.Count > 0;

        var retriever = httpClient is null
            ? new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever()
            : new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever(httpClient);

        _config = new Microsoft.IdentityModel.Protocols.ConfigurationManager<OpenIdConnectConfiguration>(
            metadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            retriever);
    }

    /// <summary>
    /// Validates <paramref name="token"/> against the provider's discovery
    /// document (issuer + JWKS). Returns <see langword="false"/> if the token is
    /// malformed, signed by an unknown key, expired/not-yet-valid, issued by the
    /// wrong issuer, or (when configured) for the wrong audience.
    /// </summary>
    public async Task<bool> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await _config.GetConfigurationAsync(ct);
        }
        catch
        {
            // Discovery is unreachable or malformed — fail closed.
            return false;
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidateIssuer = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateAudience = _validateAudience,
            ValidAudiences = _validateAudience ? _validAudiences : null,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        return await ExternalTokenValidator.ValidateJwtAsync(token, parameters);
    }
}

/// <summary>
/// Pure, network-free JWT validation helpers. Kept separate from
/// <see cref="OidcDiscoveryValidator"/> so the security-critical logic can be
/// unit-tested without standing up a discovery endpoint.
/// </summary>
public static class ExternalTokenValidator
{
    /// <summary>
    /// Validates <paramref name="token"/> against the supplied
    /// <paramref name="parameters"/> (signature, issuer, audience, lifetime).
    /// Never throws — returns <see langword="false"/> on any validation failure.
    /// </summary>
    public static async Task<bool> ValidateJwtAsync(
        string token, TokenValidationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var result = await new JsonWebTokenHandler()
                .ValidateTokenAsync(token, parameters);
            return result.IsValid;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Process-wide cache of <see cref="OidcDiscoveryValidator"/> instances keyed
/// by metadata address + audience set. Provider adapters are DI-scoped; without
/// sharing, every request would construct a fresh
/// <see cref="Microsoft.IdentityModel.Protocols.ConfigurationManager{OpenIdConnectConfiguration}"/>
/// and re-fetch discovery/JWKS from the IdP (a per-request round trip that
/// couples token validation to the IdP's availability). The underlying
/// <see cref="Microsoft.IdentityModel.Protocols.ConfigurationManager{OpenIdConnectConfiguration}"/>
/// is thread-safe and self-refreshing, so one shared instance per
/// configuration is correct.
/// </summary>
public static class OidcDiscoveryValidatorCache
{
    private readonly record struct EntryKey(
        string MetadataAddress,
        string OrderedAudiences);

    private static readonly ConcurrentDictionary<EntryKey, OidcDiscoveryValidator> Cache = new();

    /// <summary>
    /// Returns the shared validator for <paramref name="metadataAddress"/>,
    /// creating it (with the given optional audiences for local validation) on
    /// first use.
    /// </summary>
    public static OidcDiscoveryValidator GetOrCreate(
        string metadataAddress,
        IEnumerable<string>? validAudiences = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataAddress);

        var audiences = (validAudiences ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToArray();

        return Cache.GetOrAdd(
            new EntryKey(metadataAddress, string.Join("|", audiences)),
            _ => new OidcDiscoveryValidator(metadataAddress, audiences));
    }
}
