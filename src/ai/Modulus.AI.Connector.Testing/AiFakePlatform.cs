namespace Modulus.AI.Connector.Testing;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Indexing;
using Modulus.AI.Connector.Revocation;

/// <summary>One call the app made to the fake platform.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The request path (<c>/revocations/scope</c>, ...).</param>
/// <param name="Authorization">The <c>Authorization</c> header as sent.</param>
/// <param name="Body">The request body.</param>
/// <param name="Status">The status the fake platform answered.</param>
public sealed record AiPlatformCall(string Method, string Path, string? Authorization, string Body, int Status);

/// <summary>
/// Stands in for the AI platform in tests: it owns an envelope signing key and the API keys of both directions, signs
/// envelopes for a user, and receives the app's revocation signals (and change hints), answering with queued failures
/// when asked to. <see cref="Configure"/> points a host's connector at it: settings and outgoing HTTP clients. No
/// platform package is involved; this is the wire contract seen from the platform's side.
/// </summary>
public sealed class AiFakePlatform : IDisposable
{
    /// <summary>The issuer and base URL used unless set (<c>.invalid</c> never resolves, so nothing leaves the test).</summary>
    public const string DefaultBaseUrl = "https://ai-platform.conformance.invalid";

    private const string KeyId = "conformance-key";

    private readonly RSA _key = RSA.Create(2048);
    private readonly RSA _strangerKey = RSA.Create(2048);
    private readonly ConcurrentQueue<AiPlatformCall> _calls = new();
    private readonly ConcurrentQueue<HttpStatusCode> _failures = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentBag<AiAppInstanceOptions> _instances = [];
    private Guid? _companyId;

    /// <summary>Creates a platform with fresh keys.</summary>
    public AiFakePlatform()
    {
        ApiKey = "conformance-platform-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        ConnectorApiKey = "conformance-connector-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var parameters = _key.ExportParameters(false);
        SigningKeys = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = SecurityAlgorithms.RsaSha256,
                    kid = KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus),
                    e = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        });
    }

    /// <summary>The envelopes' <c>iss</c>.</summary>
    public string Issuer { get; init; } = DefaultBaseUrl;

    /// <summary>Where the app sends revocation signals and change hints.</summary>
    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>The platform tenant that owns <see cref="AppInstanceId"/>.</summary>
    public string PlatformTenantId { get; init; } = "conformance-tenant";

    /// <summary>The app instance the conformance user calls through.</summary>
    public string AppInstanceId { get; init; } = "conformance-instance";

    /// <summary>A second configured app instance, owned by <see cref="OtherPlatformTenantId"/> (for pairing probes).</summary>
    public string OtherAppInstanceId { get; init; } = "conformance-other-instance";

    /// <summary>The platform tenant of <see cref="OtherAppInstanceId"/>.</summary>
    public string OtherPlatformTenantId { get; init; } = "conformance-other-tenant";

    /// <summary>
    /// The Modulus company <see cref="AppInstanceId"/> reads; null for an app without companies. It may be set after the
    /// host started (a company the test created): the connector's settings are updated in place.
    /// </summary>
    public Guid? CompanyId
    {
        get => _companyId;
        set
        {
            _companyId = value;
            foreach (var instance in _instances)
                instance.TenantId = value;
        }
    }

    /// <summary>The key the platform presents to the app (the app stores only its hash).</summary>
    public string ApiKey { get; }

    /// <summary>The key the app presents to the platform (<c>Ai:Connector:Platform:ApiKey</c>).</summary>
    public string ConnectorApiKey { get; }

    /// <summary>The platform's published signing keys (a JWKS document).</summary>
    public string SigningKeys { get; }

    /// <summary>Every call the app made to the platform, in order.</summary>
    public IReadOnlyList<AiPlatformCall> Calls => [.. _calls];

    /// <summary>The revocation signals the platform acknowledged, in order.</summary>
    public IReadOnlyList<RevocationSignal> Revocations =>
    [
        .. _calls.Where(c => c.Path == "/revocations/scope" && c.Status is >= 200 and < 300)
            .Select(c => JsonSerializer.Deserialize<RevocationSignal>(c.Body, ConnectorJson.Options)!),
    ];

    /// <summary>Answers the next <paramref name="count"/> calls with <paramref name="status"/> (default <c>503</c>).</summary>
    public void FailNext(int count, HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
    {
        for (var i = 0; i < count; i++)
            _failures.Enqueue(status);
    }

    // Drops failures nobody consumed, so they never leak into a later check.
    internal void ClearFailures()
    {
        while (_failures.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Points the connector at this platform (call it from <c>ConfigureTestServices</c>, after the app's own
    /// registrations): enabled, this platform's issuer and keys, the hash of <see cref="ApiKey"/>, two app instances
    /// (<see cref="AppInstanceId"/> in <see cref="CompanyId"/>, <see cref="OtherAppInstanceId"/> without a company),
    /// revocations retried within 50 ms, no change settle delay, change hints off, and the connector's outgoing HTTP
    /// clients answered here.
    /// </summary>
    public void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.PostConfigure<ModulusAiConnectorOptions>(o =>
        {
            o.Enabled = true;
            o.ApiKeyHashes = [AiApiKeys.Hash(ApiKey)];
            o.Platform.Issuer = Issuer;
            o.Platform.JwksUrl = null;
            o.Platform.SigningKeys = SigningKeys;
            o.Platform.BaseUrl = BaseUrl;
            o.Platform.ApiKey = ConnectorApiKey;
            o.Platform.RevocationMaxBackoff = TimeSpan.FromMilliseconds(50);
            // Options are built once per options cache (IOptions, IOptionsMonitor): remember each copy of the instance, so
            // a company set later reaches all of them.
            var instance = new AiAppInstanceOptions { AppInstanceId = AppInstanceId, PlatformTenantId = PlatformTenantId, TenantId = CompanyId };
            _instances.Add(instance);
            o.Instances =
            [
                instance,
                new AiAppInstanceOptions { AppInstanceId = OtherAppInstanceId, PlatformTenantId = OtherPlatformTenantId },
            ];
            o.Indexing.ChangesSettleDelay = TimeSpan.Zero;
            o.Indexing.ChangeHints.Enabled = false;
        });

        foreach (var name in new[] { AiRevocationDispatcher.HttpClientName, AiChangeHintService.HttpClientName, AiPlatformKeyProvider.HttpClientName })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new Receiver(this));
    }

    /// <summary>
    /// A signed envelope for <paramref name="user"/> (the value of the connector's user claim, <paramref name="userClaim"/>),
    /// for <paramref name="appInstanceId"/> (default <see cref="AppInstanceId"/>) of <paramref name="platformTenantId"/>
    /// (default <see cref="PlatformTenantId"/>), valid for 60 seconds. <paramref name="tweak"/> changes the token before
    /// it is signed; <paramref name="signedByStranger"/> signs it with a key the app does not know.
    /// </summary>
    public string Envelope(
        string user,
        string? appInstanceId = null,
        string? platformTenantId = null,
        string userClaim = AiEnvelopeClaims.Subject,
        Action<SecurityTokenDescriptor>? tweak = null,
        bool signedByStranger = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var instance = appInstanceId ?? AppInstanceId;
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = instance,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(60),
            Claims = new Dictionary<string, object>
            {
                ["jti"] = Guid.NewGuid().ToString("N"),
                [AiEnvelopeClaims.Subject] = user,
                [userClaim] = user,
                [AiEnvelopeClaims.TenantId] = platformTenantId ?? PlatformTenantId,
                [AiEnvelopeClaims.AppInstanceId] = instance,
                [AiEnvelopeClaims.CorrelationId] = "conformance-" + Guid.NewGuid().ToString("N")[..8],
            },
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(signedByStranger ? _strangerKey : _key) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        };
        tweak?.Invoke(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>An unsigned (<c>alg: none</c>) envelope with the claims of a valid one.</summary>
    public string UnsignedEnvelope(string user)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = AppInstanceId,
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 60,
            ["jti"] = Guid.NewGuid().ToString("N"),
            [AiEnvelopeClaims.Subject] = user,
            [AiEnvelopeClaims.TenantId] = PlatformTenantId,
            [AiEnvelopeClaims.AppInstanceId] = AppInstanceId,
        }));
        return $"{header}.{payload}.";
    }

    /// <summary>
    /// A request to the connector at <paramref name="path"/> (relative to <paramref name="pathPrefix"/>) with the
    /// platform's API key (or <paramref name="apiKey"/>; empty sends none), the envelope when given, and a JSON body.
    /// </summary>
    public HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? envelope = null,
        string? json = null,
        string? apiKey = null,
        string pathPrefix = "/_ai/connector")
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        var request = new HttpRequestMessage(method, new Uri(pathPrefix.TrimEnd('/') + path, UriKind.Relative));
        var key = apiKey ?? ApiKey;
        if (key.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        if (envelope is not null)
            request.Headers.Add("AiPlatform-Envelope", envelope);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>Waits until <paramref name="condition"/> holds over <see cref="Calls"/>, or <paramref name="timeout"/> passes.</summary>
    public async Task<bool> WaitForAsync(Func<IReadOnlyList<AiPlatformCall>, bool> condition, TimeSpan timeout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (!condition(Calls))
        {
            try
            {
                await _signal.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return condition(Calls);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key.Dispose();
        _strangerKey.Dispose();
        _signal.Dispose();
    }

    private async Task<HttpResponseMessage> ReceiveAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var status = _failures.TryDequeue(out var failure) ? failure : HttpStatusCode.OK;
        _calls.Enqueue(new AiPlatformCall(
            request.Method.Method, request.RequestUri?.AbsolutePath ?? string.Empty, request.Headers.Authorization?.ToString(), body, (int)status));
        _signal.Release();
        return new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }

    // A fresh handler per pipeline: the client factory disposes handlers it rotates out, the platform stays.
    private sealed class Receiver(AiFakePlatform platform) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => platform.ReceiveAsync(request, cancellationToken);
    }
}
