namespace Modulus.Webhooks.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class WebhookFanOutTests
{
    [Fact]
    public async Task An_event_is_recorded_for_each_matching_subscription_of_its_tenant()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        var tenant = Guid.NewGuid();
        var all = await services.AddSubscriptionAsync(tenantId: tenant);
        var catalog = await services.AddSubscriptionAsync(tenantId: tenant, eventTypes: "catalog.*");
        await services.AddSubscriptionAsync(tenantId: tenant, eventTypes: "orders.order-placed.v1");
        await services.AddSubscriptionAsync(tenantId: Guid.NewGuid());

        services.Tenant.TenantId = tenant;
        await services.PublishAsync(new ProductCreated(Guid.NewGuid(), "Lamp", "secret margin"));

        var deliveries = await services.DeliveriesAsync();
        deliveries.Select(d => d.SubscriptionId).Should().BeEquivalentTo([all.Id, catalog.Id]);
        deliveries.Should().OnlyContain(d => d.TenantId == tenant && d.EventType == "catalog.product-created.v1");
    }

    [Fact]
    public async Task The_body_follows_the_standard_and_uses_the_mapped_payload()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        await services.AddSubscriptionAsync();
        var product = new ProductCreated(Guid.NewGuid(), "Lamp", "secret margin");

        await services.PublishAsync(product);

        using var body = JsonDocument.Parse((await services.DeliveriesAsync()).Single().Payload);
        body.RootElement.GetProperty("type").GetString().Should().Be("catalog.product-created.v1");
        body.RootElement.GetProperty("timestamp").GetDateTime().Should().BeCloseTo(product.OccurredAt, TimeSpan.FromMilliseconds(1));
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("productId").GetGuid().Should().Be(product.ProductId);
        data.GetProperty("name").GetString().Should().Be("Lamp");
        data.TryGetProperty("internalNote", out _).Should().BeFalse("the mapping leaves it out");
    }

    [Fact]
    public async Task An_unmapped_event_is_sent_as_it_is()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        await services.AddSubscriptionAsync();
        var order = new OrderPlaced(Guid.NewGuid());

        await services.PublishAsync(order);

        using var body = JsonDocument.Parse((await services.DeliveriesAsync()).Single().Payload);
        body.RootElement.GetProperty("data").GetProperty("orderId").GetGuid().Should().Be(order.OrderId);
    }

    [Fact]
    public async Task A_redelivered_event_is_recorded_once()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        await services.AddSubscriptionAsync();
        var product = new ProductCreated(Guid.NewGuid(), "Lamp", "x");

        await services.PublishAsync(product);
        await services.PublishAsync(product);

        (await services.DeliveriesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Disabled_subscriptions_receive_nothing()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        var subscription = await services.AddSubscriptionAsync();
        await using (var scope = services.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>();
            (await db.WebhookSubscriptions.FindAsync(subscription.Id))!.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        (await services.DeliveriesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Exposed_events_join_the_routing_keys_broker_consumers_subscribe_to()
    {
        await using var services = await WebhookTestServices.CreateAsync();

        services.Provider.GetRequiredService<IIntegrationEventRegistry>().GetRoutingKeys()
            .Should().Contain(["catalog.product-created.v1", "orders.order-placed.v1"]);
    }
}

[Trait("Category", "Unit")]
public sealed class WebhookDeliveryTests
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";

    [Fact]
    public async Task A_delivery_is_posted_signed_and_marked_delivered()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        var subscription = await services.AddSubscriptionAsync(url: "https://hooks.example.com/in");
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));

        (await services.RunCycleAsync()).Should().Be(1);

        var request = services.Endpoint.Requests.Should().ContainSingle().Subject;
        var delivery = (await services.DeliveriesAsync()).Single();
        request.Url.Should().Be(new Uri("https://hooks.example.com/in"));
        request.ContentType.Should().Be("application/json");
        request.Body.Should().Be(delivery.Payload);
        request.Headers["webhook-id"].Should().Be(delivery.MessageId);
        StandardWebhooks.Verify([Secret], request.Headers["webhook-id"], request.Headers["webhook-timestamp"],
                request.Headers["webhook-signature"], request.Body, services.Clock.GetUtcNow())
            .Should().Be(WebhookVerificationResult.Valid);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Delivered);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastStatusCode.Should().Be(200);
        delivery.LockedBy.Should().BeNull();
        (await services.SubscriptionAsync(subscription.Id)).FailingSince.Should().BeNull();
    }

    [Fact]
    public async Task A_failure_is_retried_on_the_schedule_with_the_same_message_id()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        await services.AddSubscriptionAsync();
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        services.Endpoint.Enqueue(HttpStatusCode.InternalServerError);

        await services.RunCycleAsync();
        var failed = (await services.DeliveriesAsync()).Single();
        failed.Status.Should().Be(WebhookDeliveryStatus.Pending);
        failed.AttemptCount.Should().Be(1);
        failed.LastStatusCode.Should().Be(500);
        failed.LastError.Should().Contain("500").And.Contain("endpoint says no");
        failed.NextAttemptAt.Should().Be(services.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(5));

        (await services.RunCycleAsync()).Should().Be(0, "the retry is not due yet");
        services.Clock.Advance(TimeSpan.FromSeconds(5));
        (await services.RunCycleAsync()).Should().Be(1);

        (await services.DeliveriesAsync()).Single().Status.Should().Be(WebhookDeliveryStatus.Delivered);
        services.Endpoint.Requests.Select(r => r.Headers["webhook-id"]).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task A_delivery_is_dead_lettered_when_the_schedule_is_used_up()
    {
        await using var services = await WebhookTestServices.CreateAsync(settings: new Dictionary<string, string?>
        {
            ["Webhooks:RetrySchedule:0"] = "00:00:10",
        });
        await services.AddSubscriptionAsync();
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        services.Endpoint.EnqueueException(new HttpRequestException("connection refused"));
        services.Endpoint.Enqueue(HttpStatusCode.BadGateway);

        await services.RunCycleAsync();
        services.Clock.Advance(TimeSpan.FromSeconds(10));
        await services.RunCycleAsync();

        var delivery = (await services.DeliveriesAsync()).Single();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Failed);
        delivery.AttemptCount.Should().Be(2);
        delivery.LastStatusCode.Should().Be(502);
    }

    [Fact]
    public async Task A_longer_retry_after_is_honored()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        await services.AddSubscriptionAsync();
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        services.Endpoint.Enqueue(HttpStatusCode.TooManyRequests, r => r.Headers.RetryAfter = new(TimeSpan.FromMinutes(2)));

        await services.RunCycleAsync();

        (await services.DeliveriesAsync()).Single().NextAttemptAt
            .Should().Be(services.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Gone_disables_the_subscription_and_its_other_deliveries_are_dead_lettered()
    {
        await using var services = await WebhookTestServices.CreateAsync(settings: new Dictionary<string, string?>
        {
            ["Webhooks:BatchSize"] = "1",
        });
        var subscription = await services.AddSubscriptionAsync();
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        services.Endpoint.Enqueue(HttpStatusCode.Gone);

        await services.RunCycleAsync();
        await services.RunCycleAsync();

        var disabled = await services.SubscriptionAsync(subscription.Id);
        disabled.IsEnabled.Should().BeFalse();
        disabled.DisabledReason.Should().Contain("410");
        services.Endpoint.Requests.Should().ContainSingle("the second delivery is not sent to a disabled subscription");
        (await services.DeliveriesAsync()).Should().HaveCount(2).And.OnlyContain(d => d.Status == WebhookDeliveryStatus.Failed);
    }

    [Fact]
    public async Task A_subscription_failing_for_too_long_is_disabled()
    {
        await using var services = await WebhookTestServices.CreateAsync(settings: new Dictionary<string, string?>
        {
            ["Webhooks:DisableAfterFailingFor"] = "00:01:00",
        });
        var subscription = await services.AddSubscriptionAsync();
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        services.Endpoint.Enqueue(HttpStatusCode.ServiceUnavailable);
        services.Endpoint.Enqueue(HttpStatusCode.ServiceUnavailable);

        await services.RunCycleAsync();
        (await services.SubscriptionAsync(subscription.Id)).IsEnabled.Should().BeTrue();
        services.Clock.Advance(TimeSpan.FromMinutes(6));
        await services.RunCycleAsync();

        var disabled = await services.SubscriptionAsync(subscription.Id);
        disabled.IsEnabled.Should().BeFalse();
        disabled.DisabledReason.Should().Contain("failed every attempt");
    }

    [Fact]
    public async Task A_rotated_secret_signs_next_to_the_new_one_until_the_overlap_ends()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        var subscription = await services.AddSubscriptionAsync();
        var next = StandardWebhooks.GenerateSecret();
        await using (var scope = services.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestWebhooksDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<WebhookSecretProtector>();
            var stored = (await db.WebhookSubscriptions.FindAsync(subscription.Id))!;
            stored.ProtectedPreviousSecret = stored.ProtectedSecret;
            stored.PreviousSecretExpiresAt = services.Clock.GetUtcNow().UtcDateTime.AddHours(1);
            stored.ProtectedSecret = protector.Protect(next);
            await db.SaveChangesAsync();
        }

        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await services.RunCycleAsync();
        services.Clock.Advance(TimeSpan.FromHours(2));
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await services.RunCycleAsync();

        var requests = services.Endpoint.Requests.ToList();
        requests[0].Headers["webhook-signature"].Split(' ').Should().HaveCount(2);
        Verify(requests[0], Secret).Should().Be(WebhookVerificationResult.Valid);
        Verify(requests[0], next).Should().Be(WebhookVerificationResult.Valid);
        requests[1].Headers["webhook-signature"].Split(' ').Should().ContainSingle();
        Verify(requests[1], Secret).Should().Be(WebhookVerificationResult.InvalidSignature);
    }

    [Fact]
    public async Task A_claimed_delivery_is_not_picked_up_by_another_worker()
    {
        await using var services = await WebhookTestServices.CreateAsync();
        await services.AddSubscriptionAsync();
        for (var i = 0; i < 3; i++)
            await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        services.Endpoint.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = services.RunCycleAsync();
        await services.Endpoint.Arrived.WaitAsync();
        var second = await services.RunCycleAsync();
        services.Endpoint.Hold.SetResult();

        second.Should().Be(0, "the first worker holds the claim while its requests are in flight");
        (await first).Should().Be(3);
        services.Endpoint.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Old_finished_deliveries_are_purged()
    {
        await using var services = await WebhookTestServices.CreateAsync(settings: new Dictionary<string, string?>
        {
            ["Webhooks:PurgeAfter"] = "1.00:00:00",
        });
        await services.AddSubscriptionAsync();
        await services.PublishAsync(new OrderPlaced(Guid.NewGuid()));
        await services.RunCycleAsync();

        services.Clock.Advance(TimeSpan.FromDays(2));
        await services.RunCycleAsync();

        (await services.DeliveriesAsync()).Should().BeEmpty();
    }

    // Checked as of the request's own timestamp (the clock has moved on since).
    private static WebhookVerificationResult Verify(RecordedRequest request, string secret)
        => StandardWebhooks.Verify([secret], request.Headers["webhook-id"], request.Headers["webhook-timestamp"],
            request.Headers["webhook-signature"], request.Body,
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(request.Headers["webhook-timestamp"], System.Globalization.CultureInfo.InvariantCulture)));
}
