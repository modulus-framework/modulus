namespace Modulus.Realtime.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class SseTransportTests
{
    [Fact]
    public async Task A_stream_starts_ready_and_carries_messages_as_id_event_and_data()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var stream = await host.OpenAsync();

        stream.Response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        stream.Ready!.Retry.Should().Be("3000");
        JsonDocument.Parse(stream.Ready.Data!).RootElement.GetProperty("connectionId").GetString().Should().NotBeNullOrEmpty();

        await host.PublishAsync("catalog.product-created.v1", data: "chair");

        var message = await stream.NextAsync();
        message.Event.Should().Be("catalog.product-created.v1");
        message.Id.Should().NotBeNullOrEmpty();
        message.Data.Should().Be("{\"value\":\"chair\"}");
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var stream = await host.OpenAsync(user: null);

        stream.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_callers_get_tenant_wide_messages_when_sign_in_is_not_required()
    {
        await using var host = await RealtimeTestHost.StartAsync(new() { ["Realtime:RequireAuthenticatedUser"] = "false" });
        await using var stream = await host.OpenAsync(user: null);

        await host.PublishAsync("secret", permission: "catalog:read");
        await host.PublishAsync("public");

        (await stream.NextAsync()).Event.Should().Be("public", "a permission is never held anonymously");
    }

    [Fact]
    public async Task A_message_for_a_user_reaches_only_that_users_connections()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var alice = await host.OpenAsync(user: "alice");
        await using var bob = await host.OpenAsync(user: "bob");

        await host.PublishAsync("for-bob", user: "bob");
        await host.PublishAsync("everyone");

        (await bob.NextAsync()).Event.Should().Be("for-bob");
        (await alice.NextAsync()).Event.Should().Be("everyone");
    }

    [Fact]
    public async Task A_permission_narrows_the_audience_through_the_authorization_policy()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var reader = await host.OpenAsync(user: "alice", claims: "permission=catalog:read");
        await using var other = await host.OpenAsync(user: "bob");

        await host.PublishAsync("catalog.changed", permission: "catalog:read");
        await host.PublishAsync("everyone");

        (await reader.NextAsync()).Event.Should().Be("catalog.changed");
        (await other.NextAsync()).Event.Should().Be("everyone");
    }

    [Fact]
    public async Task Without_a_registered_policy_the_permission_claim_decides()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var holder = await host.OpenAsync(user: "alice", claims: "permission=reports:view");
        await using var other = await host.OpenAsync(user: "bob");

        await host.PublishAsync("report.ready", permission: "reports:view");
        await host.PublishAsync("everyone");

        (await holder.NextAsync()).Event.Should().Be("report.ready");
        (await other.NextAsync()).Event.Should().Be("everyone");
    }

    [Fact]
    public async Task Tenants_never_see_each_others_messages()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var a = await host.OpenAsync(tenant: RealtimeTestHost.TenantA);
        await using var b = await host.OpenAsync(tenant: RealtimeTestHost.TenantB);

        await host.PublishAsync("for-a", tenant: RealtimeTestHost.TenantA);
        await host.PublishAsync("for-b", tenant: RealtimeTestHost.TenantB);

        (await a.NextAsync()).Event.Should().Be("for-a");
        (await b.NextAsync()).Event.Should().Be("for-b");
    }

    [Fact]
    public async Task A_type_filter_keeps_only_the_named_events_and_prefixes()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var stream = await host.OpenAsync("?types=catalog.*,orders.shipped");

        await host.PublishAsync("billing.paid");
        await host.PublishAsync("orders.created");
        await host.PublishAsync("Catalog.Product-Created");
        await host.PublishAsync("orders.shipped");

        (await stream.NextAsync()).Event.Should().Be("Catalog.Product-Created");
        (await stream.NextAsync()).Event.Should().Be("orders.shipped");
    }

    [Fact]
    public async Task Topics_must_be_registered_and_allowed()
    {
        await using var host = await RealtimeTestHost.StartAsync();

        await using (var unknown = await host.OpenAsync("?topics=nope"))
            unknown.Response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using (var noPermission = await host.OpenAsync("?topics=orders:42"))
            noPermission.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using (var refusedByCheck = await host.OpenAsync("?topics=orders:forbidden", claims: "permission=orders:read"))
            refusedByCheck.Response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var follower = await host.OpenAsync("?topics=orders:42,catalog:products", claims: "permission=orders:read");
        await using var other = await host.OpenAsync(claims: "permission=orders:read");

        await host.PublishAsync("order.shipped", topic: "orders:42");
        await host.PublishAsync("order.shipped", topic: "orders:43");
        await host.PublishAsync("product.added", topic: "catalog:products");
        await host.PublishAsync("everyone");

        (await follower.NextAsync()).Should().Match<SseEvent>(e => e.Event == "order.shipped");
        (await follower.NextAsync()).Event.Should().Be("product.added");
        (await other.NextAsync()).Event.Should().Be("everyone");
    }

    [Fact]
    public async Task A_reconnect_with_Last_Event_ID_replays_what_was_missed_then_continues_live()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        string lastSeen;
        await using (var first = await host.OpenAsync())
        {
            await host.PublishAsync("one");
            lastSeen = (await first.NextAsync()).Id!;
        }

        await host.PublishAsync("two");
        await host.PublishAsync("not-for-alice", user: "bob");
        await host.PublishAsync("three");

        await using var again = await host.OpenAsync(lastEventId: lastSeen);
        await host.PublishAsync("four");

        (await again.NextAsync()).Event.Should().Be("two");
        (await again.NextAsync()).Event.Should().Be("three");
        (await again.NextAsync()).Event.Should().Be("four");
    }

    [Fact]
    public async Task An_unknown_Last_Event_ID_asks_the_client_to_reload()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        await using var stream = await host.OpenAsync(lastEventId: "gone");

        (await stream.NextAsync()).Event.Should().Be(RealtimeEvents.Reset);
    }

    [Fact]
    public async Task Silence_is_filled_with_event_less_heartbeats()
    {
        await using var host = await RealtimeTestHost.StartAsync(new() { ["Realtime:Sse:HeartbeatInterval"] = "00:00:00.100" });
        await using var stream = await host.OpenAsync();

        var heartbeat = await stream.NextAsync(includeHeartbeats: true);

        heartbeat.Event.Should().Be("modulus.heartbeat");
        heartbeat.Data.Should().BeNullOrEmpty("EventSource dispatches nothing without data");
    }

    [Fact]
    public async Task The_stream_ends_when_the_access_token_expires()
    {
        await using var host = await RealtimeTestHost.StartAsync();
        var exp = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds();
        await using var stream = await host.OpenAsync(claims: $"exp={exp}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        SseEvent? next;
        do
        {
            next = await stream.ReadEventAsync(cts.Token);
        }
        while (next?.Event == "modulus.heartbeat");

        next.Should().BeNull("the server closes the stream at token expiry");
    }

    [Fact]
    public async Task A_user_may_hold_only_so_many_connections()
    {
        await using var host = await RealtimeTestHost.StartAsync(new() { ["Realtime:MaxConnectionsPerUser"] = "1" });
        await using var first = await host.OpenAsync();
        await using var second = await host.OpenAsync();

        second.Response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Integration_events_are_pushed_with_their_name_audience_and_mapped_payload()
    {
        await using var host = await RealtimeTestHost.StartAsync(configure: r => r
            .AddEvent<ProductCreated>(_ => RealtimeAudience.Permission("catalog:read"), e => new { e.Id, e.Name }));
        await using var stream = await host.OpenAsync(claims: "permission=catalog:read");
        var id = Guid.NewGuid();

        using (var scope = host.Services.CreateScope())
        {
            foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<ProductCreated>>())
                await handler.HandleAsync(new ProductCreated(id, "Chair", "do-not-leak"), CancellationToken.None);
        }

        var message = await stream.NextAsync();
        message.Event.Should().Be("catalog.product-created.v1");
        message.Data.Should().Be($"{{\"id\":\"{id}\",\"name\":\"Chair\"}}");
        host.Services.GetRequiredService<IIntegrationEventRegistry>().Should().NotBeNull("broker consumers subscribe through it");
    }

    [Fact]
    public async Task The_endpoint_works_inside_a_route_group()
    {
        await using var host = await RealtimeTestHost.StartAsync(map: app => app.MapGroup("/mobile").MapModulusRealtime());
        using var client = host.Client();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mobile/realtime/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }
}
