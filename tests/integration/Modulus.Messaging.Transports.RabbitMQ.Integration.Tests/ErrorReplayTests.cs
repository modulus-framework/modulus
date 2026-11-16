using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>Replay of parked messages (FR-09) on a real broker: the message is parked in <c>_error</c>, then replayed and handled.</summary>
[Trait("Category", "Integration")]
public sealed class ErrorReplayTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task FR_09_a_parked_message_is_replayed_from_the_error_queue_and_handled_by_the_consumer()
    {
        var endpoint = $"replay-{Guid.NewGuid():N}";
        var gate = new FailureGate();
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<FlakyOnceConsumer, OrderPlaced>(endpoint);
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

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        try
        {
            var bus = provider.GetRequiredService<IBus>();
            await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(Guid.NewGuid()));

            (await WaitUntil(async () => await ErrorCount(endpoint) == 1)).Should().BeTrue("the failed message is parked in _error");
            gate.Failing = false;

            var replayed = await provider.GetRequiredService<IMessageErrorQueue>().ReplayAsync(endpoint);

            replayed.Should().Be(1);
            (await WaitUntil(() => gate.Handled == 1)).Should().BeTrue("the replayed message runs its consumer");
            (await WaitUntil(async () => await ErrorCount(endpoint) == 0)).Should().BeTrue("the replayed message left _error");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>The message count of the endpoint's error queue, read with a passive declare (it does not take messages).</summary>
    private async Task<uint> ErrorCount(string endpoint)
    {
        var factory = new ConnectionFactory
        {
            HostName = broker.Host,
            Port = broker.Port,
            UserName = broker.UserName,
            Password = broker.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            var declared = await channel.QueueDeclarePassiveAsync(endpoint + "_error");
            return declared.MessageCount;
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason?.ReplyCode == 404)
        {
            // The error queue is declared on the first park; before that there is nothing parked.
            return 0;
        }
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

            await Task.Delay(100);
        }

        return condition();
    }

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return await condition();
    }

    private sealed class FailureGate
    {
        private int _handled;

        public volatile bool Failing = true;

        public int Handled => Volatile.Read(ref _handled);

        public void Succeeded() => Interlocked.Increment(ref _handled);
    }

    private sealed class FlakyOnceConsumer(FailureGate gate) : IConsumer<OrderPlaced>
    {
        public Task Consume(ConsumeContext<OrderPlaced> context)
        {
            if (gate.Failing)
            {
                throw new InvalidOperationException("transient");
            }

            gate.Succeeded();
            return Task.CompletedTask;
        }
    }
}
