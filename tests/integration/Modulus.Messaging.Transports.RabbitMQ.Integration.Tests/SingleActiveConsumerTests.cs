using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>Single-active endpoints across hosts (D10, FR-14): one host takes the endpoint's messages at a time.</summary>
[Trait("Category", "Integration")]
public sealed class SingleActiveConsumerTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task FR_14_a_single_active_endpoint_hands_its_messages_to_one_host_at_a_time_across_two_hosts()
    {
        var endpoint = $"sac-{Guid.NewGuid():N}";
        var first = new Received();
        var second = new Received();
        await using var hostA = await StartNode(endpoint, first);
        await using var hostB = await StartNode(endpoint, second);
        var bus = hostA.Services.GetRequiredService<IBus>();

        var sent = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in sent)
        {
            await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(id));
        }

        (await WaitUntil(() => first.OrderIds.Count + second.OrderIds.Count == sent.Count)).Should().BeTrue();
        (first.OrderIds.IsEmpty || second.OrderIds.IsEmpty).Should().BeTrue("only one of the two hosts is active on the endpoint");
        first.OrderIds.Concat(second.OrderIds).Should().Equal(sent, "one active consumer hands the messages over in send order");
    }

    private async Task<Node> StartNode(string endpoint, Received received)
    {
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint, o => o.SingleActiveConsumer = true);
            b.UseRabbitMq(o =>
            {
                var options = broker.Options();
                o.HostName = options.HostName;
                o.Port = options.Port;
                o.UserName = options.UserName;
                o.Password = options.Password;
                o.StopTimeout = options.StopTimeout;
            });
        });

        var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        return new Node(provider, host);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    private sealed class Node(ServiceProvider provider, IHostedService host) : IAsyncDisposable
    {
        public IServiceProvider Services => provider;

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
