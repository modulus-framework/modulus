namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DomainEventDispatchTimingTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _sp;
    private readonly TestDbContext _db;
    private readonly List<Guid> _handled = [];

    public DomainEventDispatchTimingTests()
    {
        _conn = new SqliteConnection("DataSource=domain-event-timing;Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, HostTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<DomainEventDispatcher>();
        services.TryAddScoped<IDeferredDomainEventQueue, DeferredDomainEventQueue>();
        services.AddScoped<IDomainEventHandler<TestDomainEvent>>(
            _ => new RecordingHandler(_handled));
        services.AddModuleDatabase<TestDbContext>(o => o.UseSqlite(_conn));
        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<TestDbContext>();

        _db.Database.EnsureCreated();
    }

    /// <summary>
    /// Verifies that when NO explicit transaction is active (implicit transaction),
    /// domain events dispatch immediately after SaveChangesAsync completes if there
    /// are no handlers (the dispatcher still runs but has no-op). The key thing is
    /// the deferred queue should be empty.
    /// </summary>
    [Fact]
    public async Task DomainEventDispatch_WithoutExplicitTransaction_DoesNotQueue()
    {
        var aggregate = new TestAggregate();
        aggregate.RaiseEvent();
        _db.Aggregates.Add(aggregate);

        await _db.SaveChangesAsync();

        // No explicit transaction, so events should be dispatched immediately,
        // not queued
        var queue = _sp.GetRequiredService<IDeferredDomainEventQueue>();
        var deferred = queue.DequeueAll();
        deferred.Should().BeEmpty("events should dispatch immediately without explicit transaction");
    }

    /// <summary>
    /// Verifies that when an explicit transaction is active (e.g., TransactionBehavior
    /// wrapping a command handler, or a <em>manual</em> Begin/Commit outside the
    /// mediator pipeline), domain events are DEFERRED while the transaction runs and
    /// dispatched AFTER it commits — without anyone draining the queue manually.
    /// The transaction interceptor registered by AddModuleDatabase performs the
    /// dispatch, so manual transactions no longer lose their events.
    /// </summary>
    [Fact]
    public async Task DomainEventDispatch_WithExplicitTransaction_DefersUntilAfterCommit()
    {
        var txn = await _db.Database.BeginTransactionAsync();
        try
        {
            var aggregate = new TestAggregate();
            aggregate.RaiseEvent();
            _db.Aggregates.Add(aggregate);

            await _db.SaveChangesAsync();

            // While the transaction is open the event must not have dispatched.
            _handled.Should().BeEmpty("handler should not run until after commit");

            await txn.CommitAsync();

            // The interceptor dispatches the deferred events during commit —
            // no manual drain required (the TransactionBehavior drain is a no-op).
            _handled.Should().HaveCount(1, "commit must dispatch deferred events automatically");

            // The queue must be fully drained so no later dispatch replays them.
            _sp.GetRequiredService<IDeferredDomainEventQueue>()
                .DequeueAll().Should().BeEmpty("events dispatch exactly once");
        }
        finally
        {
            await txn.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies that rolling a transaction back DISCARDS its deferred events:
    /// handlers never observe writes that were rolled back, and a later,
    /// unrelated commit in the same DI scope must not re-dispatch them.
    /// </summary>
    [Fact]
    public async Task DomainEventDispatch_OnRollback_DiscardsDeferredEvents()
    {
        var rolledBack = await _db.Database.BeginTransactionAsync();
        try
        {
            var first = new TestAggregate();
            first.RaiseEvent();
            _db.Aggregates.Add(first);
            await _db.SaveChangesAsync();

            await rolledBack.RollbackAsync();
        }
        finally
        {
            await rolledBack.DisposeAsync();
        }

        _handled.Should().BeEmpty("events from a rolled-back unit must never dispatch");
        _sp.GetRequiredService<IDeferredDomainEventQueue>()
            .DequeueAll().Should().BeEmpty("rollback must clear the deferred queue");

        // A later, unrelated unit in the same scope must not resurrect the
        // rolled-back unit's events.
        var secondTxn = await _db.Database.BeginTransactionAsync();
        try
        {
            var second = new TestAggregate();
            second.RaiseEvent();
            _db.Aggregates.Add(second);
            await _db.SaveChangesAsync();

            _handled.Should().BeEmpty("still deferred while the second transaction is open");

            await secondTxn.CommitAsync();
        }
        finally
        {
            await secondTxn.DisposeAsync();
        }

        _handled.Should().ContainSingle(
            "only the second unit's event dispatches; the rolled-back unit's event was cleared");
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sp.DisposeAsync();
        _conn.Dispose();
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => true;
        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => new NoopDisposable();
        public IDisposable BeginScope() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class TestDbContext : ModuleDbContext
    {
        public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

        public TestDbContext(DbContextOptions options, ICurrentTenant tenant, ICurrentUser user,
            DomainEventDispatcher dispatcher, IServiceProvider sp)
            : base(options, tenant, user, dispatcher, sp)
        {
        }

        protected override string TablePrefix => "test_";

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<TestAggregate>(b => b.HasKey(x => x.Id));
        }
    }

    private sealed class TestAggregate : IAggregateRoot
    {
        private readonly List<IDomainEvent> _domainEvents = [];
        public Guid Id { get; init; } = Guid.NewGuid();
        public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

        public void RaiseEvent() => _domainEvents.Add(new TestDomainEvent(Id));
        public void ClearDomainEvents() => _domainEvents.Clear();
    }

    private sealed record TestDomainEvent(Guid AggregateId) : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    private sealed class RecordingHandler(List<Guid> handled) : IDomainEventHandler<TestDomainEvent>
    {
        public Task HandleAsync(TestDomainEvent @event, CancellationToken ct)
        {
            handled.Add(@event.EventId);
            return Task.CompletedTask;
        }
    }
}
