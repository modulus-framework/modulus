using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class RabbitMqConcurrencyTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task FR_14_ordered_messages_stay_in_send_order_under_concurrency_16()
    {
        var endpoint = Endpoint();

        var gauge = new ConcurrencyGauge();
        void Consumers(MessagingBusBuilder b) => b.AddConsumer<SlowKeyedOrderConsumer, KeyedOrder>(endpoint, o =>
        {
            o.Ordered = true;
            o.ConcurrentMessageLimit = 16;
        });

        var keyed = new KeyedLog();
        await using var host = await StartBus(Consumers, s => s.AddSingleton(keyed).AddSingleton(gauge));
        for (var seq = 0; seq < 100; seq++)
        {
            foreach (var key in new[] { "a", "b", "c", "d" })
            {
                await host.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder(key, seq));
            }
        }

        (await WaitUntil(() => keyed.Done.Count == 400)).Should().BeTrue();
        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            keyed.Done.Where(m => m.Key == key).Select(m => m.Seq)
                .Should().Equal(Enumerable.Range(0, 100), $"partition {key} is handled in send order under concurrency 16");
        }

        gauge.Peak.Should().BeGreaterThan(1, "different partitions run at the same time");
        gauge.Peak.Should().BeLessOrEqualTo(16);
    }

    [Fact]
    public async Task FR_14_a_single_host_runs_up_to_the_concurrency_limit()
    {
        var endpoint = Endpoint();

        var gauge = new ConcurrencyGauge();
        void Consumers(MessagingBusBuilder b) => b.AddConsumer<SlowKeyedOrderConsumer, KeyedOrder>(endpoint, o => o.ConcurrentMessageLimit = 16);

        var keyed = new KeyedLog();
        await using var host = await StartBus(Consumers, s => s.AddSingleton(keyed).AddSingleton(gauge));
        for (var seq = 0; seq < 200; seq++)
        {
            await host.Bus.Send(new Uri($"queue:{endpoint}"), new KeyedOrder($"k{seq}", 0));
        }

        (await WaitUntil(() => keyed.Done.Count == 200)).Should().BeTrue();
        gauge.Peak.Should().BeGreaterThan(1, "a limit of 16 lets messages overlap");
        gauge.Peak.Should().BeLessOrEqualTo(16);
    }

    private static string Endpoint() => $"concurrent-{Guid.NewGuid():N}";

    private async Task<TestHost> StartBus(Action<MessagingBusBuilder> consumers, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        services.AddModulusMessaging(b =>
        {
            consumers(b);
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
        return new TestHost(provider, host, provider.GetRequiredService<IBus>());
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private sealed class TestHost(ServiceProvider provider, IHostedService host, IBus bus) : IAsyncDisposable
    {
        public IBus Bus { get; } = bus;

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
