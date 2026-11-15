using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging;
using Modulus.Messaging.Outbox.EntityFrameworkCore;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore.Tests;

public sealed record OrderPlaced(Guid OrderId);

public sealed class DeliveredLog
{
    public ConcurrentQueue<Guid> Orders { get; } = new();
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
