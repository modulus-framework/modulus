namespace Modulus.Realtime.Tests;

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Realtime.Delivery;
using Modulus.Realtime.Redis;
using Testcontainers.Redis;
using Xunit;

internal static class RealtimeNode
{
    public static ServiceProvider Create(string connectionString, string channel = "test:realtime")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Realtime:Redis:ConnectionString"] = connectionString,
            ["Realtime:Redis:Channel"] = channel,
        }).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddModulusRealtime(configuration)
            .AddRedisRealtimeBackplane(configuration)
            .BuildServiceProvider();
    }

    public static async Task StartAsync(ServiceProvider node)
    {
        foreach (var hosted in node.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    public static RealtimeConnection Listen(ServiceProvider node, string id)
    {
        var connection = new RealtimeConnection(id, "test", new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], "Test")), null, null, 100);
        node.GetRequiredService<RealtimeDispatcher>().TryRegister(connection);
        return connection;
    }

    public static async Task<RealtimeMessage> ReceiveAsync(RealtimeConnection connection)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await connection.Reader.ReadAsync(cts.Token);
    }

    public static Task PublishAsync(ServiceProvider node, string type)
    {
        using var scope = node.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IRealtimePublisher>().PublishAsync(type, new { type }, RealtimeAudience.Tenant);
    }
}

[Trait("Category", "Unit")]
public sealed class RedisRealtimeBackplaneFallbackTests
{
    [Fact]
    public async Task With_Redis_down_a_message_is_still_delivered_on_the_publishing_node()
    {
        await using var node = RealtimeNode.Create("127.0.0.1:1,connectTimeout=200,asyncTimeout=500,syncTimeout=500");
        node.GetRequiredService<IRealtimeBackplane>().Should().BeOfType<RedisRealtimeBackplane>();
        var connection = RealtimeNode.Listen(node, "c1");

        await RealtimeNode.PublishAsync(node, "local.only");

        (await RealtimeNode.ReceiveAsync(connection)).Type.Should().Be("local.only");
    }
}

/// <summary>Two service providers stand in for two replicas sharing one Redis.</summary>
[Trait("Category", "Integration")]
public sealed class RedisRealtimeBackplaneTests : IAsyncLifetime
{
    private RedisContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:7.0").Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task A_message_published_on_one_node_reaches_connections_on_every_node_once_and_in_order()
    {
        await using var a = RealtimeNode.Create(_container.GetConnectionString());
        await using var b = RealtimeNode.Create(_container.GetConnectionString());
        await RealtimeNode.StartAsync(a);
        await RealtimeNode.StartAsync(b);
        await Task.Delay(500);
        var onA = RealtimeNode.Listen(a, "a1");
        var onB = RealtimeNode.Listen(b, "b1");

        await RealtimeNode.PublishAsync(a, "first");
        await RealtimeNode.PublishAsync(a, "second");

        (await RealtimeNode.ReceiveAsync(onB)).Type.Should().Be("first");
        (await RealtimeNode.ReceiveAsync(onB)).Type.Should().Be("second");
        (await RealtimeNode.ReceiveAsync(onA)).Type.Should().Be("first");
        (await RealtimeNode.ReceiveAsync(onA)).Type.Should().Be("second");
        onA.Reader.Count.Should().Be(0, "the publishing node delivers through its own subscription, once");
    }

    [Fact]
    public async Task Nodes_on_different_channels_do_not_share_messages()
    {
        await using var a = RealtimeNode.Create(_container.GetConnectionString(), "app-a");
        await using var b = RealtimeNode.Create(_container.GetConnectionString(), "app-b");
        await RealtimeNode.StartAsync(a);
        await RealtimeNode.StartAsync(b);
        await Task.Delay(500);
        var onA = RealtimeNode.Listen(a, "a1");
        var onB = RealtimeNode.Listen(b, "b1");

        await RealtimeNode.PublishAsync(a, "for-a");
        await RealtimeNode.PublishAsync(b, "for-b");

        (await RealtimeNode.ReceiveAsync(onA)).Type.Should().Be("for-a");
        (await RealtimeNode.ReceiveAsync(onB)).Type.Should().Be("for-b");
    }
}
