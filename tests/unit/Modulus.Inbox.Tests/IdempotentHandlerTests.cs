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

    [Fact]
    public async Task HandleAsync_LegacyProcessedRow_HonouredForAnyHandler_SkipsWithoutReexecuting()
    {
        // B1/B2 legacy-migration contract: a row written before HandlerName
        // existed (HandlerName == "") must be honoured for EVERY handler
        // claiming that EventId — not just re-run because the row's
        // HandlerName doesn't match this handler's computed name.
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
            // HandlerName intentionally left unset (defaults to "" — the
            // legacy sentinel), simulating a row from before this column
            // existed.
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Processed,
        });
        await db.SaveChangesAsync();

        await sut.HandleAsync(@event, default); // must not throw, must not re-run

        await inner.DidNotReceive().HandleAsync(Arg.Any<TestEvent>(), default);
        db.Set<InboxMessage>().Count(m => m.Id == @event.EventId).Should().Be(1,
            "the legacy row is honoured, not duplicated with a fresh claim");
    }

    [Fact]
    public async Task HandleAsync_LegacyPendingRow_AdoptedByFirstHandler_SecondHandlerGetsOwnRow()
    {
        // Two DIFFERENT handler classes subscribed to the same event (the B2
        // fan-out scenario) both racing to claim ONE pre-migration legacy row:
        // the first adopts it; the second must not be blocked by that
        // adoption — it gets its own fresh row instead of deferring forever.
        // SQLite (not InMemory): adoption uses ExecuteUpdateAsync, which the
        // InMemory provider does not support — see
        // HandleAsync_AbandonedClaim_ReclaimedAfterTimeout above.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var opts = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TestDbContext(opts);
        db.Database.EnsureCreated();
        var store = new EfInboxStore(db);

        var innerA = new LegacyFanOutHandlerA();
        var sutA = new IdempotentIntegrationEventHandler<TestEvent>(
            innerA, store,
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var innerB = new LegacyFanOutHandlerB();
        var sutB = new IdempotentIntegrationEventHandler<TestEvent>(
            innerB, store,
            Options.Create(new InboxOptions()),
            NullLogger<IdempotentIntegrationEventHandler<TestEvent>>.Instance);

        var @event = new TestEvent();
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = @event.EventId,
            MessageType = typeof(TestEvent).AssemblyQualifiedName!,
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Pending,
        });
        await db.SaveChangesAsync();

        await sutA.HandleAsync(@event, default);
        await sutB.HandleAsync(@event, default);

        innerA.CallCount.Should().Be(1);
        innerB.CallCount.Should().Be(1,
            "adopting the legacy row for handler A must not block handler B");
        db.Set<InboxMessage>().Count(m => m.Id == @event.EventId).Should().Be(2,
            "the adopted legacy row plus handler B's fresh row");
    }

    // ── Test doubles ─────────────────────────────────────────────
    private sealed class LegacyFanOutHandlerA : IIntegrationEventHandler<TestEvent>
    {
        public int CallCount;
        public Task HandleAsync(TestEvent e, CancellationToken ct)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyFanOutHandlerB : IIntegrationEventHandler<TestEvent>
    {
        public int CallCount;
        public Task HandleAsync(TestEvent e, CancellationToken ct)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

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
