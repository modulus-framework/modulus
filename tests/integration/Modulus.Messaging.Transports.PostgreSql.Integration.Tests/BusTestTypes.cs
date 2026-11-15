using System.Collections.Concurrent;
using Modulus.Messaging;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

public sealed record OrderPlaced(Guid OrderId);

/// <summary>A message type no consumer on the test endpoints handles.</summary>
public sealed record Ping;

/// <summary>Higher priority than <see cref="OrderPlaced"/>: a PostgreSQL host takes it first (FR-13).</summary>
[MessagePriority(10)]
public sealed record UrgentOrder(Guid OrderId);

public sealed class UrgentConsumer(Received received) : IConsumer<UrgentOrder>
{
    public Task Consume(ConsumeContext<UrgentOrder> context)
    {
        received.OrderIds.Enqueue(context.Message.OrderId);
        return Task.CompletedTask;
    }
}

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

/// <summary>A message of one partition (customer) with its position in that partition's send order (FR-14).</summary>
public sealed record KeyedOrder([property: PartitionKey] string Key, int Seq);

/// <summary>Completed keyed messages in the order they finished, and one message to fail once.</summary>
public sealed class KeyedLog
{
    public ConcurrentQueue<KeyedOrder> Done { get; } = new();

    public KeyedOrder? FailOnce { get; set; }
}

public sealed class KeyedOrderConsumer(KeyedLog log) : IConsumer<KeyedOrder>
{
    public Task Consume(ConsumeContext<KeyedOrder> context)
    {
        if (log.FailOnce is { } once && once == context.Message)
        {
            log.FailOnce = null;
            throw new InvalidOperationException("fail once");
        }

        log.Done.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}
