using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Outbox;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.Configurations;
using Xunit;

namespace Modulus.Outbox.Tests;

[Trait("Category", "Unit")]
public sealed class OutboxProcessorTests
{
    /// <summary>
    /// Builds a processor backed by a real SQLite in-memory database (kept
    /// alive by the open connection) so ExecuteUpdate, transactions, and the
    /// lock-aware WHERE clause all behave as they would in production.
    /// </summary>
    private static async Task<TestHarness> BuildAsync(
        OutboxOptions? options = null,
        Func<OutboxMessage, Task>? onDispatch = null)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));
        services.AddDbContext<TestOutboxDbContext>(o => o.UseSqlite(conn));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestOutboxDbContext>());

        var opts = options ?? new OutboxOptions();
        var dispatcher = new FakeDispatcher(onDispatch);
        services.AddSingleton<IOutboxDispatcher>(dispatcher);

        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<TestOutboxDbContext>();
        await db.Database.EnsureCreatedAsync();

        var processor = new OutboxProcessor(
            sp,
            Options.Create(opts),
            NullLogger<OutboxProcessor>.Instance);

        return new TestHarness(processor, sp, db, dispatcher, conn);
    }

    [Fact]
    public async Task ProcessAsync_PendingMessage_DispatchesAndMarksProcessed()
    {
        await using var h = await BuildAsync();
        var msg = h.Seed();
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(1);
        var stored = await h.ReadSingleAsync();
        stored.ProcessedAt.Should().NotBeNull();
        stored.LockedBy.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_LockedByAnotherInstance_DoesNotDispatch()
    {
        // A peer instance has already claimed the only message (lock not yet
        // expired). This processor must not pick it up — no double dispatch.
        await using var h = await BuildAsync();
        var msg = h.Seed();
        msg.LockedBy = "another-instance";
        msg.LockedUntil = DateTime.UtcNow.AddMinutes(1); // active lock
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(0);
        var stored = await h.ReadSingleAsync();
        stored.LockedBy.Should().Be("another-instance"); // untouched
    }

    [Fact]
    public async Task ProcessAsync_ExpiredLock_IsReclaimed()
    {
        // A peer crashed mid-dispatch; its lock expired. The message should now
        // be reclaimable so it isn't stranded forever.
        await using var h = await BuildAsync();
        var msg = h.Seed();
        msg.LockedBy = "crashed-instance";
        msg.LockedUntil = DateTime.UtcNow.AddMinutes(-1); // expired
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(1);
        var stored = await h.ReadSingleAsync();
        stored.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_DispatchFails_SchedulesBackoffAndIncrementsRetry()
    {
        await using var h = await BuildAsync(
            onDispatch: _ => throw new InvalidOperationException("broker down"));
        var msg = h.Seed();
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        var stored = await h.ReadSingleAsync();
        stored.RetryCount.Should().Be(1);
        stored.Error.Should().Be("broker down");
        stored.ProcessedAt.Should().BeNull();
        stored.NextAttemptAt.Should().NotBeNull();
        stored.NextAttemptAt.Should().BeAfter(DateTime.UtcNow);
        // Lock released so the message isn't stranded.
        stored.LockedBy.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_BackoffNotElapsed_DoesNotRedispatch()
    {
        // A failed message scheduled for a future retry must NOT be picked up
        // again on the immediately following poll.
        await using var h = await BuildAsync(
            onDispatch: _ => throw new InvalidOperationException("broker down"));
        var msg = h.Seed();
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();            // fails, schedules backoff
        h.Dispatcher.Calls = 0;
        await h.Processor.ProcessAsync();            // backoff not elapsed

        h.Dispatcher.Calls.Should().Be(0, "the message is still in backoff");
    }

    [Fact]
    public async Task ProcessAsync_ExceedsMaxRetries_StopsRetrying()
    {
        // After MaxRetries the message is excluded from processing (dead-
        // lettered) so it doesn't hot-loop the consumer.
        await using var h = await BuildAsync(
            options: new OutboxOptions { MaxRetries = 2, InitialBackoffSec = 0 },
            onDispatch: _ => throw new InvalidOperationException("always fails"));

        var msg = h.Seed();
        msg.RetryCount = 2; // already at the budget
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(0, "messages at the retry budget are skipped");
        var stored = await h.ReadSingleAsync();
        stored.RetryCount.Should().Be(2); // unchanged — not reprocessed
    }

    // ── Test doubles ─────────────────────────────────────────────
    internal sealed class TestOutboxDbContext(
        DbContextOptions<TestOutboxDbContext> opts) : DbContext(opts)
    {
        protected override void OnModelCreating(ModelBuilder mb)
            => new OutboxMessageConfiguration()
                .Configure(mb.Entity<OutboxMessage>());
    }

    internal sealed class FakeDispatcher : IOutboxDispatcher
    {
        private readonly Func<OutboxMessage, Task> _onDispatch;
        public int Calls;

        public FakeDispatcher(Func<OutboxMessage, Task>? onDispatch)
            => _onDispatch = onDispatch ?? (_ => Task.CompletedTask);

        public async Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await _onDispatch(message);
        }
    }

    internal sealed class TestHarness(
        OutboxProcessor processor,
        ServiceProvider sp,
        TestOutboxDbContext db,
        FakeDispatcher dispatcher,
        SqliteConnection conn) : IAsyncDisposable
    {
        public OutboxProcessor Processor => processor;
        public TestOutboxDbContext Db => db;
        public FakeDispatcher Dispatcher => dispatcher;

        public OutboxMessage Seed() => db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            MessageType = "Modulus.Outbox.Tests.TestEvent, Modulus.Outbox.Tests",
            Payload = "{}",
            ModuleName = "Test",
            CreatedAt = DateTime.UtcNow,
        }).Entity;

        public Task<int> SaveChangesAsync() => db.SaveChangesAsync();

        /// <summary>
        /// Reads the single outbox row through a FRESH DbContext (new scope) so
        /// the assertion sees committed DB state rather than a stale tracked
        /// entity from the context that seeded it.
        /// </summary>
        public async Task<OutboxMessage> ReadSingleAsync()
        {
            await using var scope = sp.CreateAsyncScope();
            var fresh = scope.ServiceProvider
                                      .GetRequiredService<TestOutboxDbContext>();
            return await fresh.Set<OutboxMessage>().AsNoTracking().SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await sp.DisposeAsync();
            await conn.DisposeAsync();
        }
    }
}
