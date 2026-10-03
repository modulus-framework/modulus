namespace Modulus.Webhooks.Tests;

using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

[IntegrationEventName("catalog.product-created.v1")]
public sealed record ProductCreated(Guid ProductId, string Name, string InternalNote) : IntegrationEventBase("catalog.product-created.v1");

[IntegrationEventName("orders.order-placed.v1")]
public sealed record OrderPlaced(Guid OrderId) : IntegrationEventBase("orders.order-placed.v1");

public sealed class TestWebhooksDbContext(DbContextOptions<TestWebhooksDbContext> options) : ModulusWebhooksDbContext(options);

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>An ambient tenant the tests switch.</summary>
public sealed class TestTenant : ICurrentTenant
{
    public Guid? TenantId { get; set; }

    public string? TenantSlug => TenantId?.ToString();

    public bool IsAvailable => TenantId is not null;

    public bool IsHost => TenantId is null;

    public IDisposable Change(TenantInfo? tenant)
    {
        var previous = TenantId;
        TenantId = tenant?.TenantId;
        return new Restore(() => TenantId = previous);
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

/// <summary>Answers each request with the next queued response (default 200) and records what it received.</summary>
public sealed class RecordingEndpoint : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpResponseMessage>> _responses = new();

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>When set, requests wait for it (after signalling <see cref="Arrived"/>).</summary>
    public TaskCompletionSource? Hold { get; set; }

    public SemaphoreSlim Arrived { get; } = new(0);

    public void Enqueue(HttpStatusCode status, Action<HttpResponseMessage>? configure = null)
        => _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("endpoint says no") };
            configure?.Invoke(response);
            return response;
        });

    public void EnqueueException(Exception exception) => _responses.Enqueue(() => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            request.Content?.Headers.ContentType?.MediaType,
            body));
        if (Hold is { } hold)
        {
            Arrived.Release();
            await hold.Task.WaitAsync(cancellationToken);
        }

        return _responses.TryDequeue(out var next) ? next() : new HttpResponseMessage(HttpStatusCode.OK);
    }
}

public sealed record RecordedRequest(Uri Url, IReadOnlyDictionary<string, string> Headers, string? ContentType, string Body);

/// <summary>A service provider with webhooks over an in-memory SQLite store.</summary>
public sealed class WebhookTestServices : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private WebhookTestServices(ServiceProvider provider, SqliteConnection connection, RecordingEndpoint endpoint, ManualClock clock, TestTenant tenant)
    {
        Provider = provider;
        _connection = connection;
        Endpoint = endpoint;
        Clock = clock;
        Tenant = tenant;
    }

    public ServiceProvider Provider { get; }

    public RecordingEndpoint Endpoint { get; }

    public ManualClock Clock { get; }

    public TestTenant Tenant { get; }

    public static async Task<WebhookTestServices> CreateAsync(
        Action<WebhooksBuilder>? webhooks = null,
        IDictionary<string, string?>? settings = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        var endpoint = new RecordingEndpoint();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var tenant = new TestTenant();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ICurrentTenant>(tenant);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddDbContext<TestWebhooksDbContext>(o => o.UseSqlite(connection));
        services.AddModulusWebhooksStore<TestWebhooksDbContext>();
        services.AddModulusWebhooks(configuration, webhooks ?? (w => w
            .AddEvent<ProductCreated>("A product was created.", e => new { e.ProductId, e.Name })
            .AddEvent<OrderPlaced>()));
        services.AddHttpClient(WebhookDeliveryProcessor.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => endpoint);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().Database.EnsureCreatedAsync();

        return new WebhookTestServices(provider, connection, endpoint, clock, tenant);
    }

    public async Task<WebhookSubscription> AddSubscriptionAsync(
        string url = "https://hooks.example.com/in",
        Guid? tenantId = null,
        string secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw",
        params string[] eventTypes)
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<WebhookSecretProtector>();
        var subscription = new WebhookSubscription
        {
            TenantId = tenantId ?? Guid.Empty,
            Url = url,
            EventTypes = eventTypes.Length == 0 ? ["*"] : [.. eventTypes],
            ProtectedSecret = protector.Protect(secret),
            CreatedAt = Clock.GetUtcNow().UtcDateTime,
            UpdatedAt = Clock.GetUtcNow().UtcDateTime,
        };
        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    /// <summary>Hands the event to every registered handler, like the module bus does.</summary>
    public async Task PublishAsync<TEvent>(TEvent @event)
        where TEvent : IIntegrationEvent
    {
        await using var scope = Provider.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>())
            await handler.HandleAsync(@event, CancellationToken.None);
    }

    public async Task<int> RunCycleAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>().ProcessAsync();
    }

    public async Task<List<WebhookDelivery>> DeliveriesAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().WebhookDeliveries
            .AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync();
    }

    public async Task<WebhookSubscription> SubscriptionAsync(Guid id)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().WebhookSubscriptions
            .AsNoTracking().SingleAsync(s => s.Id == id);
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
