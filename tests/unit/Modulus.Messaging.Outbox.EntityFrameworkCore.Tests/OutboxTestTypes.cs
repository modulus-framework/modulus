using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging;
using Modulus.Messaging.Outbox.EntityFrameworkCore;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore.Tests;

public sealed record OrderPlaced(Guid OrderId);

public sealed class DeliveredLog
{
    public ConcurrentQueue<Guid> Orders { get; } = new();

    public ConcurrentQueue<RequestHeaders> Requests { get; } = new();
}

public sealed record RequestHeaders(string? RequestId, string? ReplyTo);

/// <summary>Records the request headers a delivery carries, so a test can see what the outbox kept.</summary>
public sealed record PriceQuoteRequested(Guid OrderId);

public sealed class RequestHeaderConsumer(DeliveredLog log) : IConsumer<PriceQuoteRequested>
{
    public Task Consume(ConsumeContext<PriceQuoteRequested> context)
    {
        context.Headers.TryGetValue(MessagingHeaders.RequestId, out var requestId);
        context.Headers.TryGetValue(MessagingHeaders.ReplyTo, out var replyTo);
        log.Requests.Enqueue(new RequestHeaders(requestId, replyTo));
        return Task.CompletedTask;
    }
}

public sealed class OrderPlacedConsumer(DeliveredLog log) : IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> context)
    {
        log.Orders.Enqueue(context.Message.OrderId);
        return Task.CompletedTask;
    }
}

public sealed class OutboxTestDb(DbContextOptions<OutboxTestDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyModulusMessagingOutbox();
}
