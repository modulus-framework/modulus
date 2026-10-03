namespace Modulus.Realtime.Tests;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>A TestServer host with header-driven auth (<c>X-Test-User</c>, <c>X-Test-Claim: type=value</c>) and tenant (<c>X-Tenant</c>).</summary>
internal sealed class RealtimeTestHost : IAsyncDisposable
{
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private RealtimeTestHost(WebApplication app) => App = app;

    public WebApplication App { get; }

    public TestServer Server => App.GetTestServer();

    public IServiceProvider Services => App.Services;

    public static async Task<RealtimeTestHost> StartAsync(
        Dictionary<string, string?>? settings = null,
        Action<RealtimeBuilder>? configure = null,
        Action<IServiceCollection>? services = null,
        Action<IEndpointRouteBuilder>? map = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? []);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentTenant, TestTenant>();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("catalog:read", p => p.RequireClaim("permission", "catalog:read"));
            o.AddPolicy("orders:read", p => p.RequireClaim("permission", "orders:read"));
        });
        builder.Services.AddModulusRealtime(builder.Configuration, r =>
        {
            r.AddTopic("catalog:products");
            r.AddTopic("orders:*", "orders:read", t => ValueTask.FromResult(t.Key != "forbidden"));
            configure?.Invoke(r);
        });
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        if (map is null)
            app.MapModulusRealtime();
        else
            map(app);

        // Publishes as the X-Tenant of the request: POST /test/publish?type=&user=&topic=&permission=
        app.MapPost("/test/publish", async (HttpContext http, IRealtimePublisher publisher, string type, string? user, string? topic, string? permission, string? data) =>
        {
            var audience = user is not null ? RealtimeAudience.User(user)
                : topic is not null ? RealtimeAudience.ForTopic(topic)
                : RealtimeAudience.Tenant;
            if (permission is not null)
                audience = audience.RequirePermission(permission);
            await publisher.PublishAsync(type, new { value = data ?? type }, audience, http.RequestAborted);
            return Results.NoContent();
        });

        await app.StartAsync();
        return new RealtimeTestHost(app);
    }

    public HttpClient Client(string? user = "alice", Guid? tenant = null, params string[] claims)
    {
        var client = Server.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        if (user is not null)
            client.DefaultRequestHeaders.Add("X-Test-User", user);
        foreach (var claim in claims)
            client.DefaultRequestHeaders.Add("X-Test-Claim", claim);
        if (tenant is { } id)
            client.DefaultRequestHeaders.Add("X-Tenant", id.ToString());
        return client;
    }

    /// <summary>Publishes through the HTTP test endpoint (in <paramref name="tenant"/>).</summary>
    public async Task PublishAsync(string type, string? user = null, string? topic = null, string? permission = null, Guid? tenant = null, string? data = null)
    {
        using var client = Client(user: null, tenant: tenant);
        var query = $"type={Uri.EscapeDataString(type)}"
            + (user is null ? "" : $"&user={Uri.EscapeDataString(user)}")
            + (topic is null ? "" : $"&topic={Uri.EscapeDataString(topic)}")
            + (permission is null ? "" : $"&permission={Uri.EscapeDataString(permission)}")
            + (data is null ? "" : $"&data={Uri.EscapeDataString(data)}");
        using var response = await client.PostAsync($"/test/publish?{query}", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Opens an SSE stream; the first event (<c>modulus.ready</c>) has already been read.</summary>
    public async Task<SseStream> OpenAsync(string query = "", string? user = "alice", Guid? tenant = null, string? lastEventId = null, params string[] claims)
    {
        var client = Client(user, tenant, claims);
        var request = new HttpRequestMessage(HttpMethod.Get, "/realtime/events" + query);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (lastEventId is not null)
            request.Headers.Add("Last-Event-ID", lastEventId);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var stream = new SseStream(client, response);
        if (response.IsSuccessStatusCode)
        {
            var ready = await stream.NextAsync(includeHeartbeats: false);
            if (ready.Event != RealtimeEvents.Ready)
                throw new InvalidOperationException($"Expected {RealtimeEvents.Ready}, got {ready.Event}.");
            stream.Ready = ready;
        }

        return stream;
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }

    private sealed class HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
            foreach (var claim in Request.Headers["X-Test-Claim"].OfType<string>())
            {
                var separator = claim.IndexOf('=', StringComparison.Ordinal);
                claims.Add(new Claim(claim[..separator], claim[(separator + 1)..]));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
        }
    }

    /// <summary>The tenant is the request's <c>X-Tenant</c> header, or what <c>Change</c> set.</summary>
    private sealed class TestTenant(IHttpContextAccessor accessor) : ICurrentTenant
    {
        private static readonly AsyncLocal<TenantInfo?> Changed = new();
        private static readonly AsyncLocal<bool> HasChanged = new();

        public Guid? TenantId => Current?.TenantId;

        public string? TenantSlug => Current?.TenantSlug;

        public bool IsAvailable => Current is not null;

        public bool IsHost => Current is null;

        private TenantInfo? Current
        {
            get
            {
                if (HasChanged.Value)
                    return Changed.Value;
                var header = accessor.HttpContext?.Request.Headers["X-Tenant"].ToString();
                return Guid.TryParse(header, out var id) ? new TenantInfo(id, "t-" + id.ToString("N")[..4]) : null;
            }
        }

        public IDisposable Change(TenantInfo? tenant)
        {
            var (previous, had) = (Changed.Value, HasChanged.Value);
            Changed.Value = tenant;
            HasChanged.Value = true;
            return new Restore(() =>
            {
                Changed.Value = previous;
                HasChanged.Value = had;
            });
        }

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}

/// <summary>One SSE event as written on the wire.</summary>
internal sealed record SseEvent(string? Id, string? Event, string? Data, string? Retry);

/// <summary>Reads an SSE response line by line (heartbeats included, unlike <c>EventSource</c>).</summary>
internal sealed class SseStream : IAsyncDisposable
{
    private readonly HttpClient _client;
    private readonly StreamReader? _reader;

    public SseStream(HttpClient client, HttpResponseMessage response)
    {
        _client = client;
        Response = response;
        if (response.IsSuccessStatusCode)
            _reader = new StreamReader(response.Content.ReadAsStream(), Encoding.UTF8);
    }

    public HttpResponseMessage Response { get; }

    public SseEvent? Ready { get; set; }

    /// <summary>The next event, failing after <paramref name="timeout"/> (default 5 s).</summary>
    public async Task<SseEvent> NextAsync(bool includeHeartbeats = false, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            var next = await ReadEventAsync(cts.Token) ?? throw new EndOfStreamException("The SSE stream ended.");
            if (includeHeartbeats || next.Event != "modulus.heartbeat")
                return next;
        }
    }

    /// <summary>Null when the stream ends.</summary>
    public async Task<SseEvent?> ReadEventAsync(CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(_reader);
        string? id = null, type = null, data = null, retry = null;
        var any = false;
        while (true)
        {
            var line = await _reader.ReadLineAsync(ct);
            if (line is null)
                return any ? new SseEvent(id, type, data, retry) : null;
            if (line.Length == 0)
            {
                if (any)
                    return new SseEvent(id, type, data, retry);
                continue;
            }

            any = true;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..].TrimStart(' ');
            switch (field)
            {
                case "id": id = value; break;
                case "event": type = value; break;
                case "data": data = data is null ? value : data + "\n" + value; break;
                case "retry": retry = value; break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        Response.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>An integration event for the event-bridge tests.</summary>
[IntegrationEventName("catalog.product-created.v1")]
internal sealed record ProductCreated(Guid Id, string Name, string Secret) : IntegrationEventBase("catalog.product-created.v1");
