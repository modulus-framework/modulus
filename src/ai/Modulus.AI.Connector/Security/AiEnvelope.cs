namespace Modulus.AI.Connector;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>The claims of the platform's signed envelope that the connector reads.</summary>
public static class AiEnvelopeClaims
{
    /// <summary>The platform user (also the default user-match claim).</summary>
    public const string Subject = "sub";

    /// <summary>The user's e-mail address, when the platform knows it.</summary>
    public const string Email = "email";

    /// <summary>The platform tenant.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>The platform app instance; must also be the envelope's audience.</summary>
    public const string AppInstanceId = "app_instance_id";

    /// <summary>The platform's correlation id of the question.</summary>
    public const string CorrelationId = "correlation_id";
}

/// <summary>API-key hashing for <see cref="ModulusAiConnectorOptions.ApiKeyHashes"/>.</summary>
public static class AiApiKeys
{
    /// <summary>The lower-case hex SHA-256 of <paramref name="apiKey"/> (what the settings store instead of the key).</summary>
    public static string Hash(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
    }

    /// <summary>Whether <paramref name="apiKey"/> hashes to one of <paramref name="hashes"/> (constant-time per hash).</summary>
    public static bool Matches(string apiKey, IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (string.IsNullOrEmpty(apiKey))
            return false;

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        var match = false;
        foreach (var hash in hashes)
        {
            byte[] expected;
            try
            {
                expected = Convert.FromHexString(hash);
            }
            catch (FormatException)
            {
                continue;
            }

            match |= CryptographicOperations.FixedTimeEquals(presented, expected);
        }

        return match;
    }
}

/// <summary>A verified envelope.</summary>
internal sealed record AiEnvelope(
    string Id,
    string PlatformTenantId,
    string AppInstanceId,
    IReadOnlyDictionary<string, string> Claims,
    string? CorrelationId,
    DateTimeOffset ExpiresAt);

/// <summary>The outcome of envelope verification: the envelope, or why it was refused (for the audit trail only).</summary>
internal readonly record struct AiEnvelopeResult(AiEnvelope? Envelope, string? Failure)
{
    public static AiEnvelopeResult Fail(string reason) => new(null, reason);
}

/// <summary>
/// The platform's published envelope signing keys: an inline JWKS (<see cref="AiPlatformOptions.SigningKeys"/>) and/or
/// a JWKS URL fetched and kept for <see cref="AiPlatformOptions.KeyRefreshInterval"/>. An unknown key id forces a
/// fetch, at most once a minute, so a rotated key is picked up without a restart.
/// </summary>
internal sealed class AiPlatformKeyProvider(
    IHttpClientFactory httpClients,
    IOptions<ModulusAiConnectorOptions> options,
    TimeProvider clock,
    ILogger<AiPlatformKeyProvider> logger)
{
    public const string HttpClientName = "Modulus.AI.Connector.Keys";

    private static readonly TimeSpan MinForcedRefresh = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<SecurityKey>? _inline;
    private IReadOnlyList<SecurityKey> _remote = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    public async ValueTask<IReadOnlyList<SecurityKey>> GetKeysAsync(bool forceRefresh, CancellationToken ct)
    {
        var platform = options.Value.Platform;
        _inline ??= string.IsNullOrWhiteSpace(platform.SigningKeys)
            ? []
            : new JsonWebKeySet(platform.SigningKeys).GetSigningKeys().ToList();

        if (string.IsNullOrWhiteSpace(platform.JwksUrl))
            return _inline;

        var now = clock.GetUtcNow();
        var stale = now - _fetchedAt > platform.KeyRefreshInterval;
        var forced = forceRefresh && now - _fetchedAt > MinForcedRefresh;
        if (stale || forced)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (clock.GetUtcNow() - _fetchedAt > (forced ? MinForcedRefresh : platform.KeyRefreshInterval))
                {
                    try
                    {
                        var client = httpClients.CreateClient(HttpClientName);
                        var json = await client.GetStringAsync(new Uri(platform.JwksUrl), ct).ConfigureAwait(false);
                        _remote = new JsonWebKeySet(json).GetSigningKeys().ToList();
                        _fetchedAt = clock.GetUtcNow();
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
                    {
                        // Keep the last good keys; verification fails closed if none match.
                        logger.LogWarning(ex, "Fetching the AI platform's signing keys from {JwksUrl} failed.", platform.JwksUrl);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return [.. _inline, .. _remote];
    }
}

/// <summary>Envelope ids already accepted, kept until they expire, so a captured envelope cannot be replayed.</summary>
/// <remarks>Per process: with several replicas a replay could reach another node within the envelope's lifetime (~60 s).</remarks>
internal sealed class AiEnvelopeReplayCache(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private int _adds;

    /// <summary>Records <paramref name="id"/>; false when it was already seen.</summary>
    public bool TryRecord(string id, DateTimeOffset expiresAt)
    {
        if (Interlocked.Increment(ref _adds) % 256 == 0)
        {
            var now = clock.GetUtcNow();
            foreach (var (key, expiry) in _seen)
            {
                if (expiry < now)
                    _seen.TryRemove(key, out _);
            }
        }

        return _seen.TryAdd(id, expiresAt);
    }
}

/// <summary>
/// Verifies the platform-signed envelope (AD-25, FR-27): signature against the published keys, issuer, audience = the
/// app instance, lifetime (with the configured skew and a cap on <c>exp - iat</c>), a unique id that was not seen
/// before, and the tenant and app instance claims.
/// </summary>
internal sealed class AiEnvelopeValidator(
    AiPlatformKeyProvider keys,
    AiEnvelopeReplayCache replays,
    IOptions<ModulusAiConnectorOptions> options)
{
    private static readonly string[] Algorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public async Task<AiEnvelopeResult> ValidateAsync(string token, CancellationToken ct)
    {
        var connector = options.Value;
        if (string.IsNullOrWhiteSpace(connector.Platform.Issuer))
            return AiEnvelopeResult.Fail("no platform issuer configured");

        var result = await ValidateWithKeysAsync(token, connector, forceRefresh: false, ct).ConfigureAwait(false);
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            result = await ValidateWithKeysAsync(token, connector, forceRefresh: true, ct).ConfigureAwait(false);
        if (!result.IsValid)
            return AiEnvelopeResult.Fail($"envelope rejected: {result.Exception?.GetType().Name ?? "invalid"}");

        var jwt = (JsonWebToken)result.SecurityToken;
        string? Claim(string type) => jwt.TryGetClaim(type, out var c) ? c.Value : null;

        if (string.IsNullOrWhiteSpace(jwt.Id))
            return AiEnvelopeResult.Fail("envelope has no jti");
        if (jwt.IssuedAt == DateTime.MinValue)
            return AiEnvelopeResult.Fail("envelope has no iat");
        if (jwt.ValidTo - jwt.IssuedAt > connector.Platform.MaxEnvelopeLifetime)
            return AiEnvelopeResult.Fail("envelope lifetime too long");

        var tenant = Claim(AiEnvelopeClaims.TenantId);
        var instance = Claim(AiEnvelopeClaims.AppInstanceId);
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(instance))
            return AiEnvelopeResult.Fail("envelope names no tenant or app instance");
        if (!jwt.Audiences.Contains(instance, StringComparer.Ordinal))
            return AiEnvelopeResult.Fail("envelope audience is not its app instance");

        var expiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        if (!replays.TryRecord(jwt.Id, expiresAt + connector.Platform.ClockSkew))
            return AiEnvelopeResult.Fail("envelope replayed");

        var claims = jwt.Claims
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        return new AiEnvelopeResult(
            new AiEnvelope(jwt.Id, tenant, instance, claims, Claim(AiEnvelopeClaims.CorrelationId), expiresAt),
            null);
    }

    private async Task<TokenValidationResult> ValidateWithKeysAsync(
        string token, ModulusAiConnectorOptions connector, bool forceRefresh, CancellationToken ct)
    {
        var signingKeys = await keys.GetKeysAsync(forceRefresh, ct).ConfigureAwait(false);
        return await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = connector.Platform.Issuer,
            ValidAudiences = connector.Instances.Select(i => i.AppInstanceId).ToList(),
            IssuerSigningKeys = signingKeys,
            ValidAlgorithms = Algorithms,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = connector.Platform.ClockSkew,
        }).ConfigureAwait(false);
    }
}
