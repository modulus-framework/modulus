using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;
using Modulus.Inbox;
using Modulus.Inbox.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Modulus.Inbox.Tests;

[Trait("Category", "Unit")]
public sealed class IdempotentHandlerTests
{
    private static (TestDbContext db, EfInboxStore store) BuildStore()
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new TestDbContext(opts);
        db.Database.EnsureCreated();
        return (db, new EfInboxStore(db));
    }

    [Fact]
    public async Task HandleAsync_FirstDelivery_ExecutesInnerHandler()
    {
        var (db, store) = BuildStore();
        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store,
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        await sut.HandleAsync(@event, default);

        await inner.Received(1).HandleAsync(@event, default);
        var inbox = db.Set<InboxMessage>().Single();
        inbox.Status.Should().Be(InboxStatus.Processed);
    }

    [Fact]
    public async Task HandleAsync_DuplicateDelivery_SkipsInnerHandler()
    {
        var (db, store) = BuildStore();
        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store,
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        await sut.HandleAsync(@event, default); // first
        await sut.HandleAsync(@event, default); // duplicate

        // Inner called only once despite two deliveries
        await inner.Received(1).HandleAsync(Arg.Any<TestEvent>(), default);
    }

    [Fact]
    public async Task HandleAsync_HandlerThrows_StatusIsFailed()
    {
        var (db, store) = BuildStore();
        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        inner.HandleAsync(Arg.Any<TestEvent>(), default)
             .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store,
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.HandleAsync(@event, default));

        var inbox = db.Set<InboxMessage>().Single();
        inbox.Status.Should().Be(InboxStatus.Failed);
        inbox.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_InFlightElsewhere_DeferredNotReexecuted()
    {
        var (db, store) = BuildStore();
        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store,
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = @event.EventId,
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Processing,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InboxDeferralException>(
            () => sut.HandleAsync(@event, default));

        await inner.DidNotReceive().HandleAsync(Arg.Any<TestEvent>(), default);
    }

    [Fact]
    public async Task HandleAsync_ExceedsMaxRetries_DeadLettered()
    {
        var (db, store) = BuildStore();
        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store,
            Options.Create(new InboxOptions { MaxRetries = 3 }),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = @event.EventId,
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Failed,
            RetryCount = 3, // == MaxRetries
        });
        await db.SaveChangesAsync();

        await sut.HandleAsync(@event, default); // does not throw

        await inner.DidNotReceive().HandleAsync(Arg.Any<TestEvent>(), default);
    }

    [Fact]
    public async Task HandleAsync_AbandonedClaim_ReclaimedAfterTimeout()
    {
        // SQLite (not InMemory) because the reclaim path uses ExecuteUpdateAsync.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TestDbContext(opts);
        db.Database.EnsureCreated();

        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store: new EfInboxStore(db),
            Options.Create(new InboxOptions()), // ClaimTimeoutSeconds = 300
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = @event.EventId,
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Processing,
            // Claim taken 10 minutes ago — far past the 300 s lease.
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        await sut.HandleAsync(@event, default); // reclaims instead of deferring

        await inner.Received(1).HandleAsync(@event, default);
        var inbox = db.Set<InboxMessage>().Single();
        inbox.Status.Should().Be(InboxStatus.Processed);
    }

    [Fact]
    public async Task HandleAsync_LegacyProcessingRow_NoClaimedAt_UsesReceivedAt()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TestDbContext(opts);
        db.Database.EnsureCreated();

        var inner = Substitute.For<IIntegrationEventHandler<TestEvent>>();
        var sut = new IdempotentIntegrationEventHandler<TestEvent>(
            inner, store: new EfInboxStore(db),
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = @event.EventId,
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Processing,
            // Legacy row written before ClaimedAt existed — ReceivedAt is old.
            ReceivedAt = DateTime.UtcNow.AddMinutes(-30),
        });
        await db.SaveChangesAsync();

        await sut.HandleAsync(@event, default);

        await inner.Received(1).HandleAsync(@event, default);
    }

    // ── Test doubles ─────────────────────────────────────────────
    public record TestEvent : IIntegrationEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public string EventType { get; } = "test.event.v1";
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    public class TestDbContext(DbContextOptions<TestDbContext> opts)
        : DbContext(opts)
    {
        protected override void OnModelCreating(ModelBuilder mb)
        {
            new Modulus.Inbox.Configurations.InboxMessageConfiguration()
                .Configure(mb.Entity<InboxMessage>());
        }
    }
}
