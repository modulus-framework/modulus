using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Outbox;
using Modulus.Outbox.Abstractions;
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
        Func<OutboxMessage, Task>? onDispatch = null,
        Action<IServiceCollection>? configure = null)
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
        configure?.Invoke(services);

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

    [Fact]
    public async Task ProcessAsync_PersistsEachMessageBeforeDispatchingTheNext()
    {
        // Bookkeeping used to be saved once per batch, so a crash mid-batch
        // redelivered every message already dispatched in it. By the time the
        // second message is dispatched, the first must already be committed.
        DateTime? firstProcessedAtWhenSecondDispatched = null;
        TestHarness? harness = null;
        await using var h = harness = await BuildAsync(onDispatch: async m =>
        {
            if (m.Payload != "{\"n\":2}")
                return;
            await using var scope = harness!.Services.CreateAsyncScope();
            var fresh = scope.ServiceProvider.GetRequiredService<TestOutboxDbContext>();
            firstProcessedAtWhenSecondDispatched = await fresh.Set<OutboxMessage>().AsNoTracking()
                .Where(x => x.Payload == "{\"n\":1}")
                .Select(x => x.ProcessedAt)
                .SingleAsync();
        });
        h.Seed("{\"n\":1}", DateTime.UtcNow.AddSeconds(-2));
        h.Seed("{\"n\":2}", DateTime.UtcNow.AddSeconds(-1));
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(2);
        firstProcessedAtWhenSecondDispatched.Should().NotBeNull(
            "the first message's ProcessedAt must be committed before the next dispatch");
    }

    [Fact]
    public async Task ProcessAsync_StopsBeforeTheClaimExpires_AndReleasesTheRest()
    {
        // A batch that outlives its claim would race a peer that reclaims the
        // expired rows (duplicate dispatch). With a 1s claim, a first dispatch
        // that takes longer than the claim's safety margin must end the batch
        // and hand the remaining rows back unlocked.
        await using var h = await BuildAsync(
            options: new OutboxOptions { LockTimeoutSec = 1 },
            onDispatch: m => m.Payload == "{\"n\":1}" ? Task.Delay(1100) : Task.CompletedTask);
        h.Seed("{\"n\":1}", DateTime.UtcNow.AddSeconds(-3));
        h.Seed("{\"n\":2}", DateTime.UtcNow.AddSeconds(-2));
        h.Seed("{\"n\":3}", DateTime.UtcNow.AddSeconds(-1));
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(1, "the claim was about to expire after the first dispatch");
        var rows = await h.ReadAllAsync();
        rows.Single(r => r.Payload == "{\"n\":1}").ProcessedAt.Should().NotBeNull();
        rows.Where(r => r.Payload != "{\"n\":1}").Should().AllSatisfy(r =>
        {
            r.ProcessedAt.Should().BeNull();
            r.RetryCount.Should().Be(0, "an undispatched message burns no retry budget");
            r.LockedBy.Should().BeNull();
            r.LockedUntil.Should().BeNull();
        });
    }

    [Fact]
    public async Task ProcessAsync_RejectedTenant_DeadLettersWithoutDispatching()
    {
        // The row names a tenant the store no longer resolves (deleted / deactivated): it must not
        // be dispatched in an unchecked scope, and retrying cannot fix it.
        await using var h = await BuildAsync(
            options: new OutboxOptions { MaxRetries = 5 },
            configure: s => s.AddSingleton<ITenantContextRestorer>(new RejectingRestorer()));
        h.Seed(tenantId: Guid.NewGuid());
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(0);
        var stored = await h.ReadSingleAsync();
        stored.ProcessedAt.Should().BeNull();
        stored.RetryCount.Should().Be(5);
        stored.Error.Should().Contain("Tenant context rejected");
        stored.LockedBy.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_VerifiedTenant_IsAmbientDuringDispatch()
    {
        // A real AsyncLocal tenant: the dispatcher must observe the verified tenant, which only
        // holds when the processor enters it in its own frame (an AsyncLocal set inside an async
        // helper does not flow back to the caller).
        var tenantId = Guid.NewGuid();
        var current = new AsyncLocalTenant();
        TenantInfo? seen = null;
        await using var h = await BuildAsync(
            onDispatch: _ => { seen = current.Tenant; return Task.CompletedTask; },
            configure: s => s
                .AddSingleton<ICurrentTenant>(current)
                .AddSingleton<ITenantContextRestorer>(new VerifyingRestorer()));
        h.Seed(tenantId: tenantId);
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        seen.Should().NotBeNull();
        seen!.TenantId.Should().Be(tenantId);
        seen.TenantSlug.Should().Be("verified", "the dispatcher sees the store's tenant, not the raw id");
        current.Tenant.Should().BeNull("the scope ends after dispatch");
        (await h.ReadSingleAsync()).ProcessedAt.Should().NotBeNull();
    }

    private sealed class RejectingRestorer : ITenantContextRestorer
    {
        public ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
            => throw new TenantContextRejectedException(tenantId);
    }

    private sealed class VerifyingRestorer : ITenantContextRestorer
    {
        public async ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
        {
            await Task.Yield();
            return new TenantInfo(tenantId, "verified");
        }
    }

    private sealed class AsyncLocalTenant : ICurrentTenant
    {
        private static readonly AsyncLocal<TenantInfo?> s_current = new();

        public TenantInfo? Tenant => s_current.Value;
        public Guid? TenantId => s_current.Value?.TenantId;
        public string? TenantSlug => s_current.Value?.TenantSlug;
        public bool IsAvailable => s_current.Value is not null;
        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = s_current.Value;
            s_current.Value = tenant;
            return new Release(() => s_current.Value = previous);
        }

        private sealed class Release(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    [Fact]
    public async Task ProcessAsync_NonPositiveLockTimeout_StillDispatches()
    {
        await using var h = await BuildAsync(options: new OutboxOptions { LockTimeoutSec = 0 });
        h.Seed();
        await h.SaveChangesAsync();

        await h.Processor.ProcessAsync();

        h.Dispatcher.Calls.Should().Be(1);
    }

    // ── Test doubles ─────────────────────────────────────────────
    internal sealed class TestOutboxDbContext(
        DbContextOptions<TestOutboxDbContext> opts) : DbContext(opts)
    {
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<OutboxMessage>(b =>
            {
                b.ToTable("outbox_messages");
                b.HasKey(x => x.Id);
                b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
                b.Property(x => x.Payload).IsRequired();
                b.Property(x => x.ModuleName).HasMaxLength(100);
                b.HasIndex(x => new { x.ProcessedAt, x.LockedUntil, x.RetryCount });
                b.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
                b.HasIndex(x => x.TenantId);
            });
        }
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

        public ServiceProvider Services => sp;

        public OutboxMessage Seed(string payload = "{}", DateTime? createdAt = null, Guid tenantId = default)
            => db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                TenantId = tenantId,
                MessageType = "Modulus.Outbox.Tests.TestEvent, Modulus.Outbox.Tests",
                Payload = payload,
                ModuleName = "Test",
                CreatedAt = createdAt ?? DateTime.UtcNow,
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

        public async Task<List<OutboxMessage>> ReadAllAsync()
        {
            await using var scope = sp.CreateAsyncScope();
            var fresh = scope.ServiceProvider
                                      .GetRequiredService<TestOutboxDbContext>();
            return await fresh.Set<OutboxMessage>().AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await sp.DisposeAsync();
            await conn.DisposeAsync();
        }
    }
}
