namespace Modulus.Bff.Tests;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Forwarder;

/// <summary>A minimal OIDC server: discovery, password + refresh grants, userinfo, revocation.</summary>
internal sealed class FakeAuthServer : IAsyncDisposable
{
    private int _counter;
    private int _refreshCount;

    public FakeAuthServer(bool withRevocation = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        App = builder.Build();

        App.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object?>
        {
            ["issuer"] = "https://auth",
            ["token_endpoint"] = "https://auth/connect/token",
            ["userinfo_endpoint"] = "https://auth/connect/userinfo",
            ["revocation_endpoint"] = withRevocation ? "https://auth/connect/revoke" : null,
            ["end_session_endpoint"] = "https://auth/connect/endsession",
            ["jwks_uri"] = "https://auth/jwks",
            ["authorization_endpoint"] = "https://auth/connect/authorize",
            ["introspection_endpoint"] = "https://auth/connect/introspect",
            ["response_types_supported"] = new[] { "code" },
        }));

        App.MapGet("/jwks", () =>
        {
            var jwk = BffTestHost.PublicJwk();
            return Results.Json(new { keys = new[] { new { kty = jwk.Kty, kid = jwk.Kid, use = "sig", alg = "RS256", n = jwk.N, e = jwk.E } } });
        });

        App.MapPost("/connect/token", async (HttpRequest request) =>
        {
            if (Down)
                return Results.StatusCode(503);
            var form = await request.ReadFormAsync();
            LastTokenForm = form.ToDictionary(f => f.Key, f => f.Value.ToString());
            LastTokenAuthorization = request.Headers.Authorization.ToString();
            switch (form["grant_type"].ToString())
            {
                case "password" when form["username"] == "alice" && form["password"] == "pw":
                    return Issue();
                case "refresh_token" when !RefuseRefresh && form["refresh_token"].ToString().StartsWith("rt-", StringComparison.Ordinal):
                    Interlocked.Increment(ref _refreshCount);
                    await Task.Delay(50);
                    return Issue();
                default:
                    return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }
        });

        App.MapGet("/connect/userinfo", () => Results.Json(new
        {
            sub = "alice",
            preferred_username = "alice",
            email = "alice@example.test",
            realm_access = new { roles = new[] { "admin", "buyer" } },
        }));

        App.MapPost("/connect/introspect", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            IntrospectionCalls++;
            return form["token"] == "opaque-1" && request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.Ordinal)
                ? Results.Json(new { active = true, sub = "carol", client_id = "shop-mobile", scope = "api orders", groups = new[] { "ops" } })
                : Results.Json(new { active = false });
        });

        App.MapPost("/connect/revoke", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            Revoked.Add(form["token"].ToString());
            return Results.Ok();
        });

    }

    public WebApplication App { get; }

    public int ExpiresIn { get; set; } = 3600;

    public bool RefuseRefresh { get; set; }

    /// <summary>The token endpoint answers 503 without a body.</summary>
    public bool Down { get; set; }

    public int IntrospectionCalls { get; private set; }

    public int RefreshCount => Volatile.Read(ref _refreshCount);

    public ConcurrentBag<string> Revoked { get; } = [];

    public Dictionary<string, string>? LastTokenForm { get; private set; }

    public string? LastTokenAuthorization { get; private set; }

    public string? CurrentAccessToken { get; private set; }

    private IResult Issue()
    {
        var n = Interlocked.Increment(ref _counter);
        CurrentAccessToken = "at-" + n;
        return Results.Json(new { access_token = CurrentAccessToken, refresh_token = "rt-" + n, expires_in = ExpiresIn, token_type = "Bearer" });
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();
}

/// <summary>An upstream API that echoes what reached it.</summary>
internal sealed class FakeApi : IAsyncDisposable
{
    public FakeApi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        App = builder.Build();
        App.MapMethods("/{**path}", ["GET", "POST"], (HttpRequest request) =>
        {
            if (request.Path.StartsWithSegments("/fail", StringComparison.Ordinal))
                return Results.NotFound(); // 4xx: not retried by the resilience pipeline
            return Results.Json(new EchoResponse(
                request.Path + request.QueryString,
                request.Headers.Authorization.ToString(),
                request.Headers[BffDefaults.ClientAppHeader].ToString(),
                request.Headers.Cookie.ToString(),
                request.Headers["X-CSRF"].ToString()));
        });
    }

    public WebApplication App { get; }

    public ValueTask DisposeAsync() => App.DisposeAsync();
}

internal sealed record EchoResponse(string Path, string Authorization, string ClientApp, string Cookie, string Csrf);

/// <summary>Routes outgoing calls by host to the in-memory servers (<c>auth</c>, <c>api</c>).</summary>
internal sealed class RouterHandler(IReadOnlyDictionary<string, TestServer> servers) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var server = servers[request.RequestUri!.Host];
        using var invoker = new HttpMessageInvoker(server.CreateHandler(), disposeHandler: true);
        return invoker.SendAsync(request, cancellationToken);
    }
}

internal sealed class RouterForwarderFactory(RouterHandler router) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(router, disposeHandler: false);
}

/// <summary>A BFF host wired to <see cref="FakeAuthServer"/> and <see cref="FakeApi"/>.</summary>
internal sealed class BffTestHost : IAsyncDisposable
{
    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test" };

    // One RSA instance is shared by every test host in the process. Signing a token and exporting the
    // public key both touch it, and under parallel test load a token occasionally came out with a
    // signature that the same key rejected (IDX10511). Every use goes through this lock.
    private static readonly object SigningLock = new();

    /// <summary>The public half of <see cref="SigningKey"/>, exported under the lock.</summary>
    public static JsonWebKey PublicJwk()
    {
        lock (SigningLock)
            return JsonWebKeyConverter.ConvertFromRSASecurityKey(SigningKey);
    }

    private BffTestHost(WebApplication app, FakeAuthServer auth, FakeApi api, ConcurrentQueue<string> authFailures)
        => (App, Auth, Api, _authFailures) = (app, auth, api, authFailures);

    private readonly ConcurrentQueue<string> _authFailures;

    public WebApplication App { get; }

    public FakeAuthServer Auth { get; }

    public FakeApi Api { get; }

    /// <summary>Full JwtBearer validation exceptions seen by this host, for diagnosing a failed assertion.</summary>
    public string AuthFailures => string.Join("\n---\n", _authFailures);

    public TestServer Server => App.GetTestServer();

    public static async Task<BffTestHost> StartAsync(
        Dictionary<string, string?> settings,
        Action<BffBuilder> clients,
        Action<WebApplication>? map = null,
        bool withRevocation = true)
    {
        var auth = new FakeAuthServer(withRevocation);
        var api = new FakeApi();
        await auth.App.StartAsync();
        await api.App.StartAsync();
        var router = new RouterHandler(new Dictionary<string, TestServer>
        {
            ["auth"] = auth.App.GetTestServer(),
            ["api"] = api.App.GetTestServer(),
        });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bff:Authority"] = "https://auth",
            ["Bff:Services:api:Address"] = "https://api",
        });
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddModulusBff(builder.Configuration, clients);
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => router));
        builder.Services.RemoveAll<IForwarderHttpClientFactory>();
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(new RouterForwarderFactory(router));
        var authFailures = new ConcurrentQueue<string>();
        builder.Services.ConfigureAll<JwtBearerOptions>(o =>
        {
            o.BackchannelHttpHandler = router;
            // Keep the full validation exception, not just the short challenge text, so a
            // failing run says which key or check rejected the token.
            o.Events ??= new JwtBearerEvents();
            var previous = o.Events.OnAuthenticationFailed;
            o.Events.OnAuthenticationFailed = context =>
            {
                authFailures.Enqueue(context.Exception.ToString());
                return previous?.Invoke(context) ?? Task.CompletedTask;
            };
        });
        builder.Services.ConfigureAll<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(o => o.BackchannelHttpHandler = router);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseModulusBff();
        app.UseAuthorization();
        app.UseRateLimiter();
        map?.Invoke(app);
        app.MapModulusBff();
        await app.StartAsync();
        return new BffTestHost(app, auth, api, authFailures);
    }

    public static string CreateJwt(string clientId, string scope = "api", string subject = "bob")
    {
        lock (SigningLock)
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "https://auth",
                Expires = DateTime.UtcNow.AddMinutes(10),
                Claims = new Dictionary<string, object> { ["sub"] = subject, ["client_id"] = clientId, ["scope"] = scope },
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
            });
    }

    public HttpClient Client() => Server.CreateClient();

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        await Auth.DisposeAsync();
        await Api.DisposeAsync();
    }
}

/// <summary>A browser stand-in: carries the session cookie and the CSRF header.</summary>
internal sealed class Browser(HttpClient http, string csrf = "1")
{
    private string? _cookie;

    public string? Cookie => _cookie;

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null, bool withCsrf = true, string? accept = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (accept is not null)
            request.Headers.Add("Accept", accept);
        if (withCsrf)
            request.Headers.Add("X-CSRF", csrf);
        if (_cookie is not null)
            request.Headers.Add("Cookie", _cookie);
        var response = await http.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';')[0];
                _cookie = pair.EndsWith('=') ? null : pair;
            }
        }

        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string url, bool withCsrf = true, string? accept = null) => SendAsync(HttpMethod.Get, url, withCsrf: withCsrf, accept: accept);

    public Task<HttpResponseMessage> PostJsonAsync(string url, object body)
        => SendAsync(HttpMethod.Post, url, System.Net.Http.Json.JsonContent.Create(body));

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
}
