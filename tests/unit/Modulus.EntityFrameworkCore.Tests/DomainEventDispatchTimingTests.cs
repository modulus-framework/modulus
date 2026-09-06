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
    /// wrapping a command handler), domain events are DEFERRED and dispatched AFTER
    /// the transaction commits, not before. This ensures handlers see committed state
    /// and side effects are safe.
    /// </summary>
    [Fact]
    public async Task DomainEventDispatch_WithExplicitTransaction_DefersUntilAfterCommit()
    {
        var dispatchOrder = new List<string>();

        // Create a simple in-process test handler
        var testEventHandled = false;
        Func<TestDomainEvent, CancellationToken, Task> handleFunc = async (evt, ct) =>
        {
            dispatchOrder.Add("event-handler");
            testEventHandled = true;
            await Task.CompletedTask;
        };

        // Manually simulate TransactionBehavior wrapping:
        // 1. Begin transaction
        // 2. Run handler (which calls SaveChangesAsync)
        // 3. Commit transaction
        // 4. Dispatch deferred events
        dispatchOrder.Add("before-txn");

        var txn = await _db.Database.BeginTransactionAsync();
        try
        {
            dispatchOrder.Add("txn-started");

            var aggregate = new TestAggregate();
            aggregate.RaiseEvent();
            _db.Aggregates.Add(aggregate);

            dispatchOrder.Add("before-save");
            await _db.SaveChangesAsync();
            dispatchOrder.Add("after-save");

            // At this point, the event should be deferred, not dispatched yet
            testEventHandled.Should().BeFalse("handler should not run until after commit");

            await txn.CommitAsync();
            dispatchOrder.Add("after-commit");

            // Still not dispatched; must manually dispatch deferred events
            // (simulating what TransactionBehavior does)
            var queue = _sp.GetRequiredService<IDeferredDomainEventQueue>();
            var deferred = queue.DequeueAll();

            dispatchOrder.Add("before-deferred-dispatch");
            if (deferred.Count > 0)
            {
                foreach (var evt in deferred.OfType<TestDomainEvent>())
                {
                    await handleFunc(evt, CancellationToken.None);
                }
            }
            dispatchOrder.Add("after-deferred-dispatch");
        }
        finally
        {
            await txn.DisposeAsync();
        }

        // Expected order: events are deferred, dispatched only after commit
        dispatchOrder.Should().Equal(
            "before-txn",
            "txn-started",
            "before-save",
            "after-save",
            "after-commit",
            "before-deferred-dispatch",
            "event-handler",
            "after-deferred-dispatch");

        testEventHandled.Should().BeTrue("handler should have been called after commit");
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

    private sealed class TrackingEventHandler(List<string> dispatchOrder) : IDomainEventHandler<TestDomainEvent>
    {
        public Task HandleAsync(TestDomainEvent @event, CancellationToken ct)
        {
            dispatchOrder.Add("event-handler");
            return Task.CompletedTask;
        }
    }
}
