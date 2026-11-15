using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class RabbitMqTransportTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task FR_01_FR_02_publish_and_send_round_trip_through_the_broker()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);

        var orderId = Guid.NewGuid();
        await host.Bus.Publish(new OrderPlaced(orderId));
        await host.Bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(orderId));

        (await WaitUntil(() => received.OrderIds.Count == 2)).Should().BeTrue();
        received.OrderIds.Should().AllBeEquivalentTo(orderId);
        received.MessageIds.Distinct().Should().HaveCount(2, "each delivery carries its own message id");
    }

    [Fact]
    public async Task FR_07_FR_08_a_failing_message_is_redelivered_then_parked_in_error_with_its_fault()
    {
        var endpoint = Endpoint();
        await using var host = await StartBus(b => b.AddConsumer<AlwaysFailingConsumer, OrderPlaced>(endpoint, o =>
            o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromMilliseconds(100))));

        await host.Bus.Publish(new OrderPlaced(Guid.NewGuid()));

        await using var inspect = await Inspect();
        var parked = await TryGet(inspect, $"{endpoint}_error");
        parked.Should().NotBeNull("the message should be parked after its redelivery fails");
        var headers = RabbitMqMessageCodec.StringHeaders(parked!.BasicProperties.Headers);
        headers["mod-error-message"].Should().Be("boom");
        headers["mod-redelivery-count"].Should().Be("1");
    }

    [Fact]
    public void D3_a_quorum_redelivery_count_becomes_the_delivery_number()
    {
        // A quorum queue reports the deliveries returned before this one; this delivery is one more.
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Headers = new Dictionary<string, object?>
            {
                [RabbitMqMessageCodec.MessageTypeHeader] = typeof(OrderPlaced).AssemblyQualifiedName,
                ["x-delivery-count"] = 19L,
            },
        };
        var body = System.Text.Encoding.UTF8.GetBytes($"{{\"orderId\":\"{Guid.NewGuid()}\"}}");

        RabbitMqMessageCodec.TryDecode("billing", body, properties, out var envelope, out var reason).Should().BeTrue(reason);
        envelope!.Headers[MessagingHeaders.DeliveryCount].Should().Be("20");
    }

    [Fact]
    public async Task FR_09_a_message_no_consumer_handles_is_parked_in_skipped()
    {
        var endpoint = Endpoint();
        var received = new Received();
        await using var host = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), received);

        await host.Bus.Send(new Uri($"queue:{endpoint}"), new Ping());

        await using var inspect = await Inspect();
        (await TryGet(inspect, $"{endpoint}_skipped")).Should().NotBeNull();
        received.OrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RULE_02_a_message_in_flight_at_shutdown_is_delivered_again_to_the_next_host()
    {
        var endpoint = Endpoint();
        var first = new Received { BlockForever = true };
        var orderId = Guid.NewGuid();

        var firstHost = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), first, stopTimeout: TimeSpan.FromMilliseconds(300));
        await firstHost.Bus.Publish(new OrderPlaced(orderId));
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The consumer never finishes, so the stop times out, aborts the attempt and the broker takes the message back.
        await firstHost.StopAsync();
        await firstHost.DisposeAsync();

        var second = new Received();
        await using var secondHost = await StartBus(b => b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint), second);

        (await WaitUntil(() => second.OrderIds.Count == 1)).Should().BeTrue();
        second.OrderIds.Should().ContainSingle().Which.Should().Be(orderId);
    }

    private static string Endpoint() => $"orders-{Guid.NewGuid():N}";

    private async Task<TestHost> StartBus(Action<MessagingBusBuilder> consumers, Received? received = null, TimeSpan? stopTimeout = null)
    {
        received ??= new Received();
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddModulusMessaging(b =>
        {
            consumers(b);
            b.UseRabbitMq(o =>
            {
                var options = broker.Options(stopTimeout);
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

    private async Task<ConnectionFactoryHandle> Inspect()
    {
        var factory = new ConnectionFactory
        {
            HostName = broker.Host,
            Port = broker.Port,
            UserName = broker.UserName,
            Password = broker.Password,
        };
        return new ConnectionFactoryHandle(await factory.CreateConnectionAsync());
    }

    /// <summary>Polls the queue for one message. A failed poll closes its channel, so each poll uses a fresh one.</summary>
    private static async Task<BasicGetResult?> TryGet(ConnectionFactoryHandle inspect, string queue)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var channel = await inspect.Connection.CreateChannelAsync();
            try
            {
                var result = await channel.BasicGetAsync(queue, autoAck: true);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (OperationInterruptedException)
            {
                // The queue does not exist yet, or the broker closed the channel after a failed poll (AlreadyClosedException
                // derives from it): the message has not been parked yet.
            }
            finally
            {
                if (channel.IsOpen)
                {
                    await channel.CloseAsync();
                }
            }

            await Task.Delay(100);
        }

        return null;
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
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

        public Task StopAsync() => host.StopAsync(CancellationToken.None);

        public async ValueTask DisposeAsync() => await provider.DisposeAsync();
    }

    private sealed class ConnectionFactoryHandle(IConnection connection) : IAsyncDisposable
    {
        public IConnection Connection { get; } = connection;

        public async ValueTask DisposeAsync()
        {
            await Connection.CloseAsync();
            await Connection.DisposeAsync();
        }
    }
}
