using Microsoft.EntityFrameworkCore;
using Modulus.Messaging;
using Modulus.Messaging.Inbox.EntityFrameworkCore;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore.Tests;

public sealed record OrderPlaced(Guid OrderId);

/// <summary>A row the consumer writes, so the tests can see whether an attempt's writes were kept.</summary>
public sealed class ConsumedOrder
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
}

public sealed class InboxTestDb(DbContextOptions<InboxTestDb> options) : DbContext(options)
{
    public DbSet<ConsumedOrder> ConsumedOrders => Set<ConsumedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsumedOrder>().HasKey(o => o.Id);
        modelBuilder.ApplyModulusMessagingInbox();
    }
}

/// <summary>Writes one row per handled message.</summary>
public sealed class RecordingConsumer(InboxTestDb db) : IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> context)
    {
        db.ConsumedOrders.Add(new ConsumedOrder { Id = Guid.NewGuid(), OrderId = context.Message.OrderId });
        return Task.CompletedTask;
    }
}

/// <summary>Writes a row, then fails, so the test can check the row is rolled back.</summary>
public sealed class WriteThenFailConsumer(InboxTestDb db) : IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> context)
    {
        db.ConsumedOrders.Add(new ConsumedOrder { Id = Guid.NewGuid(), OrderId = context.Message.OrderId });
        throw new InvalidOperationException("fails after writing");
    }
}
