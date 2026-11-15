using System.Collections.Concurrent;
using System.Linq;
using Modulus.Messaging;

namespace Modulus.Messaging.Tests;

public interface IOrderEvent
{
}

public sealed record OrderSubmitted(Guid OrderId) : IOrderEvent;

public sealed record CreateInvoice(Guid OrderId);

public sealed record Ping;

/// <summary>Records every consumed message as "endpoint:consumer".</summary>
public sealed class Sink
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<Guid> ScopedIds { get; } = new();

    public int CountFor(string endpoint) => Calls.Count(c => c.StartsWith(endpoint + ":", StringComparison.Ordinal));
}

public sealed class ScopedMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class BillingConsumer(Sink sink) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        sink.Calls.Enqueue($"{context.Endpoint}:billing");
        return Task.CompletedTask;
    }
}

public sealed class ShippingConsumer(Sink sink) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        sink.Calls.Enqueue($"{context.Endpoint}:shipping");
        return Task.CompletedTask;
    }
}

public sealed class AuditConsumer(Sink sink) : IConsumer<IOrderEvent>
{
    public Task Consume(ConsumeContext<IOrderEvent> context)
    {
        sink.Calls.Enqueue($"{context.Endpoint}:audit");
        return Task.CompletedTask;
    }
}

public sealed class CreateInvoiceConsumer(Sink sink) : IConsumer<CreateInvoice>
{
    public Task Consume(ConsumeContext<CreateInvoice> context)
    {
        sink.Calls.Enqueue($"{context.Endpoint}:create-invoice");
        return Task.CompletedTask;
    }
}

public sealed class ScopedConsumer(Sink sink, ScopedMarker marker) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        sink.ScopedIds.Enqueue(marker.Id);
        return Task.CompletedTask;
    }
}

public sealed class FailingConsumer : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context) =>
        throw new InvalidOperationException("boom");
}

/// <summary>Used with the default endpoint name: <c>order-status</c>.</summary>
public sealed class OrderStatusConsumer(Sink sink) : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        sink.Calls.Enqueue($"{context.Endpoint}:status");
        return Task.CompletedTask;
    }
}
