namespace Modulus.Realtime.Tests;

using System.Net;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

[Trait("Category", "Unit")]
public sealed class SignalRTransportTests
{
    private static readonly Dictionary<string, string?> Enabled = new() { ["Realtime:SignalR:Enabled"] = "true" };

    private static async Task<(HubConnection Connection, ChannelReader<RealtimeEnvelope> Received)> ConnectAsync(
        RealtimeTestHost host, string user = "alice", string query = "", params string[] claims)
    {
        var received = Channel.CreateUnbounded<RealtimeEnvelope>();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, "/realtime/hub" + query), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                o.Headers["X-Test-User"] = user;
                if (claims.Length > 0)
                    o.Headers["X-Test-Claim"] = string.Join(",", claims);
            })
            .Build();
        connection.On<RealtimeEnvelope>(RealtimeHub.ClientMethod, e => received.Writer.TryWrite(e));
        await connection.StartAsync();
        return (connection, received.Reader);
    }

    private static async Task<RealtimeEnvelope> NextAsync(ChannelReader<RealtimeEnvelope> reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await reader.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task The_hub_delivers_the_same_messages_as_envelopes()
    {
        await using var host = await RealtimeTestHost.StartAsync(Enabled);
        var (connection, received) = await ConnectAsync(host);
        await using var _ = connection;

        await host.PublishAsync("catalog.changed", data: "x");

        var envelope = await NextAsync(received);
        envelope.Type.Should().Be("catalog.changed");
        envelope.Data.GetProperty("value").GetString().Should().Be("x");
        envelope.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Topics_can_be_followed_and_left_while_connected()
    {
        await using var host = await RealtimeTestHost.StartAsync(Enabled);
        var (connection, received) = await ConnectAsync(host, claims: "permission=orders:read");
        await using var _ = connection;

        await connection.InvokeAsync(nameof(RealtimeHub.Subscribe), "orders:7");
        await host.PublishAsync("order.shipped", topic: "orders:7");
        (await NextAsync(received)).Topic.Should().Be("orders:7");

        await connection.InvokeAsync(nameof(RealtimeHub.Unsubscribe), "orders:7");
        await host.PublishAsync("order.shipped", topic: "orders:7");
        await host.PublishAsync("everyone");
        (await NextAsync(received)).Type.Should().Be("everyone");
    }

    [Fact]
    public async Task Unknown_and_forbidden_topics_are_refused()
    {
        await using var host = await RealtimeTestHost.StartAsync(Enabled);
        var (connection, _) = await ConnectAsync(host);
        await using var _1 = connection;

        var unknown = () => connection.InvokeAsync(nameof(RealtimeHub.Subscribe), "nope");
        var forbidden = () => connection.InvokeAsync(nameof(RealtimeHub.Subscribe), "orders:1");

        (await unknown.Should().ThrowAsync<HubException>()).WithMessage("*not a topic*");
        (await forbidden.Should().ThrowAsync<HubException>()).WithMessage("*Not allowed*");
    }

    [Fact]
    public async Task Resume_replays_what_a_reconnecting_client_missed()
    {
        await using var host = await RealtimeTestHost.StartAsync(Enabled);
        string last;
        {
            var (first, received) = await ConnectAsync(host);
            await host.PublishAsync("one");
            last = (await NextAsync(received)).Id;
            await first.DisposeAsync();
        }

        await host.PublishAsync("two");
        var (again, replayed) = await ConnectAsync(host);
        await using var _ = again;

        (await again.InvokeAsync<bool>(nameof(RealtimeHub.Resume), last)).Should().BeTrue();
        (await NextAsync(replayed)).Type.Should().Be("two");
        (await again.InvokeAsync<bool>(nameof(RealtimeHub.Resume), "gone")).Should().BeFalse();
    }

    [Fact]
    public async Task The_hub_requires_a_signed_in_user_and_is_off_by_default()
    {
        await using (var host = await RealtimeTestHost.StartAsync(Enabled))
        {
            using var anonymous = host.Client(user: null);
            (await anonymous.PostAsync("/realtime/hub/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await using (var host = await RealtimeTestHost.StartAsync())
        {
            using var client = host.Client();
            (await client.PostAsync("/realtime/hub/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
