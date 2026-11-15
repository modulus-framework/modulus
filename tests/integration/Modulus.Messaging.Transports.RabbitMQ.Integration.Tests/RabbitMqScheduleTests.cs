using System.Collections.Concurrent;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class RabbitMqScheduleTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task A_scheduled_message_arrives_at_its_time_through_the_ttl_buckets()
    {
        var endpoint = $"sched-{Guid.NewGuid():N}";
        var arrivals = new ConcurrentQueue<(Guid Id, DateTimeOffset At)>();
        await using var transport = new RabbitMqTransport(broker.Options());
        await transport.Start(
            [endpoint],
            (envelope, _) =>
            {
                arrivals.Enqueue((envelope.MessageId, DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var envelope = Envelope(endpoint);
        var dueAt = DateTimeOffset.UtcNow.AddMilliseconds(1500);
        await transport.Schedule(Guid.NewGuid(), envelope, dueAt, CancellationToken.None);

        await Task.Delay(800);
        Assert.Empty(arrivals);

        Assert.True(await WaitUntil(() => arrivals.Count == 1, TimeSpan.FromSeconds(20)), "the scheduled message did not arrive");
        var (id, at) = Assert.Single(arrivals);
        Assert.Equal(envelope.MessageId, id);
        Assert.True(at >= dueAt.AddMilliseconds(-50), $"arrived at {at:O}, before its time {dueAt:O}");

        await transport.Stop(CancellationToken.None);
    }

    [Fact]
    public async Task A_scheduled_time_already_past_is_delivered_at_once()
    {
        var endpoint = $"sched-{Guid.NewGuid():N}";
        var arrivals = new ConcurrentQueue<Guid>();
        await using var transport = new RabbitMqTransport(broker.Options());
        await transport.Start([endpoint], (envelope, _) => { arrivals.Enqueue(envelope.MessageId); return Task.CompletedTask; }, CancellationToken.None);

        var envelope = Envelope(endpoint);
        await transport.Schedule(Guid.NewGuid(), envelope, DateTimeOffset.UtcNow.AddSeconds(-5), CancellationToken.None);

        Assert.True(await WaitUntil(() => arrivals.Count == 1, TimeSpan.FromSeconds(10)), "a past-due message was not delivered");
        Assert.Equal(envelope.MessageId, Assert.Single(arrivals));

        await transport.Stop(CancellationToken.None);
    }

    [Fact]
    public async Task Cancelling_a_scheduled_message_is_refused_until_it_is_supported()
    {
        await using var transport = new RabbitMqTransport(broker.Options());

        await Assert.ThrowsAsync<NotSupportedException>(() => transport.CancelScheduled(Guid.NewGuid(), CancellationToken.None));
    }

    private static MessageEnvelope Envelope(string endpoint) =>
        new(Guid.NewGuid(), endpoint, new OrderPlaced(Guid.NewGuid()), new Dictionary<string, string>());

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(50);
        }

        return true;
    }
}
