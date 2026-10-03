namespace Modulus.Webhooks.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class WebhookManagementTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentTenant, HeaderTenant>();
        builder.Services.AddDbContext<TestWebhooksDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddModulusWebhooksStore<TestWebhooksDbContext>();
        builder.Services.AddModulusWebhooks(builder.Configuration, w => w
            .AddEvent<ProductCreated>("A product was created.")
            .AddEvent<OrderPlaced>());
        builder.Services.Configure<ModulusWebhooksOptions>(o =>
        {
            o.EnableDelivery = false;
            o.MaxSubscriptionsPerTenant = 3;
        });
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusWebhooks();
        await _app.StartAsync();

        await using var scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Every_endpoint_needs_the_permission()
    {
        (await Client(signedIn: false).GetAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client(permission: false).GetAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client().GetAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lists_the_exposed_event_types()
    {
        var types = await Client().GetFromJsonAsync<List<WebhookEventTypeResponse>>(new Uri("/api/webhooks/event-types", UriKind.Relative));

        types.Should().BeEquivalentTo([
            new WebhookEventTypeResponse("catalog.product-created.v1", "A product was created."),
            new WebhookEventTypeResponse("orders.order-placed.v1", null),
        ]);
    }

    [Fact]
    public async Task Creating_returns_the_secret_once_and_stores_it_encrypted()
    {
        var client = Client();
        var response = await client.PostAsJsonAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative),
            new WebhookSubscriptionRequest("https://hooks.example.com/in", ["catalog.*"], "ERP"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>())!;
        created.Secret.Should().StartWith("whsec_");
        response.Headers.Location!.ToString().Should().Be($"/api/webhooks/subscriptions/{created.Id}");

        var fetched = await client.GetFromJsonAsync<WebhookSubscriptionResponse>(response.Headers.Location);
        fetched!.Secret.Should().BeNull();
        fetched.EventTypes.Should().Equal("catalog.*");

        await using var scope = _app.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().WebhookSubscriptions.SingleAsync();
        stored.ProtectedSecret.Should().NotContain(created.Secret!["whsec_".Length..]);
    }

    [Fact]
    public async Task Invalid_subscriptions_are_rejected_field_by_field()
    {
        var response = await Client().PostAsJsonAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative),
            new WebhookSubscriptionRequest("http://10.0.0.1/hook", ["billing.*", "orders.order-placed.v1"], Secret: "whsec_c2hvcnQ="));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        problem!.Errors.Keys.Should().BeEquivalentTo(["url", "eventTypes", "secret"]);
        problem.Errors["eventTypes"].Should().Equal("Unknown event type 'billing.*'.");
    }

    [Fact]
    public async Task Tenants_only_see_their_own_subscriptions()
    {
        var tenantA = Guid.NewGuid();
        var created = await CreateAsync(Client(tenant: tenantA));

        (await Client(tenant: Guid.NewGuid()).GetAsync(new Uri($"/api/webhooks/subscriptions/{created.Id}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client(tenant: Guid.NewGuid()).GetFromJsonAsync<List<WebhookSubscriptionResponse>>(new Uri("/api/webhooks/subscriptions", UriKind.Relative)))
            .Should().BeEmpty();
        (await Client(tenant: tenantA).GetFromJsonAsync<List<WebhookSubscriptionResponse>>(new Uri("/api/webhooks/subscriptions", UriKind.Relative)))
            .Should().ContainSingle(s => s.Id == created.Id);
    }

    [Fact]
    public async Task A_tenant_has_a_subscription_limit()
    {
        var client = Client(tenant: Guid.NewGuid());
        for (var i = 0; i < 3; i++)
            await CreateAsync(client);

        var response = await client.PostAsJsonAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative),
            new WebhookSubscriptionRequest("https://hooks.example.com/in", ["*"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Updating_and_re_enabling_clears_the_automatic_disable()
    {
        var client = Client();
        var created = await CreateAsync(client);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>();
            var stored = await db.WebhookSubscriptions.SingleAsync(s => s.Id == created.Id);
            stored.IsEnabled = false;
            stored.DisabledReason = "The endpoint answered 410 Gone.";
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(new Uri($"/api/webhooks/subscriptions/{created.Id}", UriKind.Relative),
            new WebhookSubscriptionRequest("https://other.example.com/in", ["orders.order-placed.v1"], IsEnabled: true));

        var updated = (await response.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>())!;
        updated.Url.Should().Be("https://other.example.com/in");
        updated.IsEnabled.Should().BeTrue();
        updated.DisabledReason.Should().BeNull();
    }

    [Fact]
    public async Task Rotating_returns_a_new_secret_and_keeps_the_old_one_for_the_overlap()
    {
        var client = Client();
        var created = await CreateAsync(client);

        var rotated = await (await client.PostAsync(new Uri($"/api/webhooks/subscriptions/{created.Id}/rotate-secret", UriKind.Relative), null))
            .Content.ReadFromJsonAsync<WebhookSubscriptionResponse>();

        rotated!.Secret.Should().StartWith("whsec_").And.NotBe(created.Secret);
        await using var scope = _app.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<WebhookSecretProtector>();
        var stored = await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().WebhookSubscriptions.SingleAsync(s => s.Id == created.Id);
        protector.Unprotect(stored.ProtectedSecret).Should().Be(rotated.Secret);
        protector.Unprotect(stored.ProtectedPreviousSecret!).Should().Be(created.Secret);
        stored.PreviousSecretExpiresAt.Should().BeAfter(DateTime.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task A_test_delivery_is_queued_and_can_be_inspected_and_retried()
    {
        var client = Client();
        var created = await CreateAsync(client);

        var test = await client.PostAsync(new Uri($"/api/webhooks/subscriptions/{created.Id}/test", UriKind.Relative), null);
        test.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var queued = (await test.Content.ReadFromJsonAsync<WebhookDeliveryResponse>())!;
        queued.EventType.Should().Be("webhook.test");
        queued.Status.Should().Be(WebhookDeliveryStatus.Pending);

        var page = await client.GetFromJsonAsync<WebhookPage<WebhookDeliveryResponse>>(
            new Uri($"/api/webhooks/subscriptions/{created.Id}/deliveries?status=pending", UriKind.Relative));
        page!.Items.Should().ContainSingle(d => d.Id == queued.Id);
        page.TotalCount.Should().Be(1);

        var detail = await client.GetFromJsonAsync<WebhookDeliveryResponse>(test.Headers.Location!);
        detail!.Payload.Should().Contain("\"type\":\"webhook.test\"");

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>();
            var stored = await db.WebhookDeliveries.SingleAsync(d => d.Id == queued.Id);
            stored.DeadLetteredAt = DateTime.UtcNow;
            stored.AttemptCount = 8;
            await db.SaveChangesAsync();
        }

        (await client.GetFromJsonAsync<WebhookPage<WebhookDeliveryResponse>>(
            new Uri($"/api/webhooks/subscriptions/{created.Id}/deliveries?status=failed", UriKind.Relative)))!.Items.Should().ContainSingle();
        var retried = await client.PostAsync(new Uri($"/api/webhooks/deliveries/{queued.Id}/retry", UriKind.Relative), null);
        retried.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var reset = (await retried.Content.ReadFromJsonAsync<WebhookDeliveryResponse>())!;
        reset.Status.Should().Be(WebhookDeliveryStatus.Pending);
        reset.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Deleting_removes_the_subscription_and_its_deliveries()
    {
        var client = Client();
        var created = await CreateAsync(client);
        await client.PostAsync(new Uri($"/api/webhooks/subscriptions/{created.Id}/test", UriKind.Relative), null);

        (await client.DeleteAsync(new Uri($"/api/webhooks/subscriptions/{created.Id}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = _app.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>().WebhookDeliveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void Mapping_without_a_store_fails_with_a_clear_message()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddModulusWebhooks(builder.Configuration);
        var app = builder.Build();

        var map = () => app.MapModulusWebhooks();

        map.Should().Throw<InvalidOperationException>().WithMessage("*AddModulusWebhooksStore*");
    }

    private static async Task<WebhookSubscriptionResponse> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/webhooks/subscriptions", UriKind.Relative),
            new WebhookSubscriptionRequest("https://hooks.example.com/in", ["*"]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>())!;
    }

    private HttpClient Client(bool signedIn = true, bool permission = true, Guid? tenant = null)
    {
        var client = _app.GetTestClient();
        if (signedIn)
            client.DefaultRequestHeaders.Add("X-Test-User", "alice");
        if (permission)
            client.DefaultRequestHeaders.Add("X-Test-Permission", WebhookPermissions.Manage);
        if (tenant is not null)
            client.DefaultRequestHeaders.Add("X-Tenant", tenant.Value.ToString());
        return client;
    }

    private sealed class HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            claims.AddRange(Request.Headers["X-Test-Permission"].Select(p => new Claim("permission", p!)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class HeaderTenant(IHttpContextAccessor accessor) : ICurrentTenant
    {
        public Guid? TenantId => Guid.TryParse(accessor.HttpContext?.Request.Headers["X-Tenant"], out var id) ? id : null;

        public string? TenantSlug => TenantId?.ToString();

        public bool IsAvailable => TenantId is not null;

        public bool IsHost => TenantId is null;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
