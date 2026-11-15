using System.Collections.Concurrent;
using Modulus.Messaging;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

public sealed record OrderPlaced(Guid OrderId);

/// <summary>A message type no consumer on the test endpoints handles.</summary>
public sealed record Ping;

/// <summary>Messages a recording consumer received, in order.</summary>
public sealed class Received
{
    public ConcurrentQueue<Guid> OrderIds { get; } = new();

    public ConcurrentQueue<Guid> MessageIds { get; } = new();

    /// <summary>When set, the consumer starts an attempt and then blocks until the attempt is cancelled.</summary>
    public bool BlockForever { get; set; }

    /// <summary>Set when a blocked attempt has started.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class RecordingConsumer(Received received) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        if (received.BlockForever)
        {
            // Stands in for a consumer that is still working when the host stops.
            received.Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }

        received.OrderIds.Enqueue(context.Message.OrderId);
        received.MessageIds.Enqueue(context.MessageId);
    }
}

public sealed class AlwaysFailingConsumer : IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> context) =>
        throw new InvalidOperationException("boom");
}
