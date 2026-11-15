using RabbitMQ.Client.Exceptions;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class RabbitMqPublishConfirmTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task A_publish_the_broker_refuses_because_the_queue_is_full_fails_and_is_not_reported_as_sent()
    {
        var endpoint = $"bounded-{Guid.NewGuid():N}";
        var options = broker.Options();
        options.MaxQueueLength = 1;
        await using var transport = new RabbitMqTransport(options);
        await transport.Start([endpoint], (_, _) => Task.CompletedTask, CancellationToken.None);

        // The parked queue has no consumer and a bound of one, so the broker refuses a park once it is full. The
        // quorum queue accepts a few more than its bound before it refuses, so keep parking until it does.
        var refusal = await Assert.ThrowsAnyAsync<PublishException>(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                await transport.MoveToError(Envelope(endpoint), new InvalidOperationException($"park {i}"), CancellationToken.None);
            }
        });

        Assert.Contains("rejected", refusal.Message, StringComparison.OrdinalIgnoreCase);
        await transport.Stop(CancellationToken.None);
    }

    private static MessageEnvelope Envelope(string endpoint) =>
        new(Guid.NewGuid(), endpoint, new OrderPlaced(Guid.NewGuid()), new Dictionary<string, string>());
}
