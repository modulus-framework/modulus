using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Inbox.Abstractions;
using Xunit;

namespace Modulus.Inbox.Tests;

/// <summary>
/// Regression coverage for B6: a losing concurrent claim used to leave the
/// failed <c>InboxMessage</c> tracked as <c>Added</c> on <see cref="EfInboxStore"/>'s
/// <c>DbContext</c> after catching the <see cref="DbUpdateException"/>. In
/// production that <c>DbContext</c> is the SAME instance <c>OutboxProcessor</c>
/// is mid-batch on (dispatch runs handlers, which claim through the same
/// scope), so the next <c>SaveChangesAsync</c> on it -- the processor's own
/// terminal write-back -- retried the same failed INSERT, threw again, and
/// discarded every OTHER message's <c>ProcessedAt</c> in that call along with
/// it: one benign inbox collision voided a whole batch (up to
/// <c>BatchSize</c>) and caused it to redispatch.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EfInboxStoreConcurrencyTests
{
    [Fact]
    public async Task LosingClaim_DetachesTheFailedInsert_SoOtherPendingChangesStillCommit()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var opts = new DbContextOptionsBuilder<IdempotentHandlerTests.TestDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new IdempotentHandlerTests.TestDbContext(opts);
        await db.Database.EnsureCreatedAsync();

        var eventId = Guid.NewGuid();
        const string handlerName = "MyHandler";

        // Simulates a batch-mate's already-applied change sitting tracked on
        // the same DbContext when the collision happens -- exactly what
        // OutboxProcessor's earlier loop iterations leave behind before its
        // own terminal SaveChangesAsync runs.
        var otherBatchMate = new InboxMessage
        {
            Id = Guid.NewGuid(),
            HandlerName = "OtherHandler",
            MessageType = "x",
            Payload = "{}",
            ModuleName = "Test",
            Status = InboxStatus.Processing,
        };
        db.Set<InboxMessage>().Add(otherBatchMate);
        await db.SaveChangesAsync();
        otherBatchMate.Status = InboxStatus.Processed;
        otherBatchMate.ProcessedAt = DateTime.UtcNow;

        var store = new EfInboxStore(db);

        // db's own SELECT (inside TryClaimAsync) genuinely finds nothing for
        // (eventId, handlerName) at this point -- another consumer only wins
        // the race a moment later, via a separate connection sharing this
        // same in-memory database, right as db's own INSERT is about to run.
        var stolen = false;
        var interceptingOptions = new DbContextOptionsBuilder<IdempotentHandlerTests.TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new StealBeforeSaveInterceptor(() =>
            {
                if (stolen) return;
                stolen = true;
                using var rival = new IdempotentHandlerTests.TestDbContext(
                    new DbContextOptionsBuilder<IdempotentHandlerTests.TestDbContext>().UseSqlite(connection).Options);
                rival.Set<InboxMessage>().Add(new InboxMessage
                {
                    Id = eventId,
                    HandlerName = handlerName,
                    MessageType = "x",
                    Payload = "{}",
                    ModuleName = "Test",
                    Status = InboxStatus.Processing,
                    ClaimedAt = DateTime.UtcNow,
                });
                rival.SaveChanges();
            }))
            .Options;
        var raceDb = new IdempotentHandlerTests.TestDbContext(interceptingOptions);
        // Re-track the batch-mate on the intercepted context, matching the
        // real scenario: one DbContext instance, spanning the whole batch.
        raceDb.Set<InboxMessage>().Attach(otherBatchMate).State = EntityState.Modified;
        var raceStore = new EfInboxStore(raceDb);

        var act = () => raceStore.TryClaimAsync(
            eventId, handlerName, "x", "{}", maxRetries: 5, claimTimeout: TimeSpan.FromMinutes(5), default);

        await act.Should().ThrowAsync<InboxDeferralException>();

        // The fix under test: the failed Added entity must not still be
        // tracked, or it poisons the next save.
        raceDb.ChangeTracker.Entries<InboxMessage>()
            .Should().NotContain(e => e.Entity.Id == eventId && e.State == EntityState.Added);

        // The real-world consequence: the processor's terminal write-back for
        // the rest of the batch must still succeed.
        await raceDb.SaveChangesAsync();
        var persisted = await db.Set<InboxMessage>().SingleAsync(m => m.Id == otherBatchMate.Id);
        persisted.Status.Should().Be(InboxStatus.Processed, "the other batch-mate's outcome must survive the collision");
    }

    private sealed class StealBeforeSaveInterceptor(Action beforeSave)
        : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            beforeSave();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            beforeSave();
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }
}
