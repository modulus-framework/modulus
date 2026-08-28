using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modulus.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Outbox.Abstractions;

public sealed class OutboxProcessor(
    IServiceProvider sp,
    IOptions<OutboxOptions> opts,
    ILogger<OutboxProcessor> logger)
{
    // Unique id per processor instance, used to claim rows so concurrent
    // instances (or concurrent polls) don't dispatch the same message twice.
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    private const int PurgeBatchSize = 1000;

    public async Task ProcessAsync(CancellationToken ct = default)
    {
        await using var scope = sp.CreateAsyncScope();
        var ssp = scope.ServiceProvider;
        var options = opts.Value;

        // Resolve ALL registered DbContexts so that outbox rows from every
        // module context are drained. In a modular monolith, each module's
        // ModuleDbContext maps its own outbox_messages table.
        var contexts = ssp.GetServices<DbContext>().ToList();
        if (contexts.Count == 0) return;

        foreach (var db in contexts)
        {
            try
            {
                await ProcessContextAsync(db, ssp, options, ct);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("not included in the model"))
            {
                // This DbContext doesn't have an outbox table configured
                // (AddOutbox wasn't called for it). Skip silently.
            }
        }
    }

    private async Task ProcessContextAsync(
        DbContext db,
        IServiceProvider ssp,
        OutboxOptions options,
        CancellationToken ct)
    {
        var dispatcher = ssp.GetRequiredService<IOutboxDispatcher>();
        var now = DateTime.UtcNow;

        // Housekeeping FIRST: a quiet system (everything already dispatched)
        // exits this method at the empty-candidates short-circuit below, so a
        // purge placed at the end would rarely run once steady state is
        // reached and dead/dispatched rows would accumulate forever.
        await PurgeExpiredAsync(db, options, ct);

        // 1. Pick candidate ids: unprocessed, under the retry budget, not
        //    currently locked by another instance, and whose backoff (if any)
        //    has elapsed.
        var candidateIds = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null
                     && m.RetryCount < options.MaxRetries
                     && (m.LockedUntil == null || m.LockedUntil < now)
                     && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(options.BatchSize)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (candidateIds.Count == 0) return;

        // 2. Atomically claim those rows for this instance. The WHERE re-check
        //    on LockedUntil is evaluated server-side, so two instances that
        //    both picked the same candidates cannot both win — the UPDATEs
        //    serialize per row and the second affects zero rows. This is the
        //    provider-agnostic equivalent of SELECT ... FOR UPDATE SKIP LOCKED.
        //    LockUntil is computed fresh here (not from the stale `now`) so the
        //    full LockTimeoutSec is available from the moment of claiming.
        var lockUntil = DateTime.UtcNow.AddSeconds(options.LockTimeoutSec);
        await db.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id)
                     && m.ProcessedAt == null
                     && (m.LockedUntil == null || m.LockedUntil < DateTime.UtcNow))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.LockedBy, _instanceId)
                      .SetProperty(m => m.LockedUntil, lockUntil),
                ct);

        // 3. Load only the rows this instance claimed in THIS batch (scoped to
        //    candidateIds). Without this scope, rows locked by this instance in
        //    a prior cycle that crashed before SaveChanges would also be
        //    returned, causing duplicate dispatch.
        var messages = await db.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id)
                     && m.LockedBy == _instanceId
                     && m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        // 4. Dispatch each. Dispatch is an irreversible side effect, so we mark
        //    ProcessedAt only AFTER success. If the process crashes between a
        //    successful dispatch and SaveChanges, the LockTimeout will expire
        //    the claim and the message is redelivered (at-least-once) —
        //    consumers MUST dedup via the inbox.
        var currentTenant = ssp.GetService<ICurrentTenant>();
        var correlation = ssp.GetService<ICorrelationContext>();
        foreach (var message in messages)
        {
            // Restore the tenant context captured when the outbox row was
            // written so downstream handlers (query filters, etc.) see the
            // correct tenant. A row written in the host context (TenantId ==
            // Guid.Empty) is dispatched under an explicit host scope
            // (Change(null)); without it, fail-closed tenant filters would hide
            // the host's own data from the handler.
            IDisposable? tenantScope = currentTenant is null
                ? null
                : currentTenant.Change(message.TenantId == Guid.Empty
                    ? null
                    : new TenantInfo(message.TenantId, message.TenantId.ToString("N")));

            // Restore the originating correlation id so it flows into the
            // in-process handlers and onto the broker envelope for cross-service
            // tracing.
            IDisposable? correlationScope =
                correlation is not null && !string.IsNullOrEmpty(message.CorrelationId)
                    ? correlation.BeginScope(message.CorrelationId)
                    : null;

            try
            {
                await dispatcher.DispatchAsync(message, ct);
                message.ProcessedAt = DateTime.UtcNow;
                message.LockedBy = null;
                message.LockedUntil = null;
                logger.LogDebug("Outbox dispatched {Id} ({Type})",
                    message.Id, message.MessageType);
            }
            catch (Exception ex)
            {
                message.RetryCount += 1;
                message.Error = ex.Message;
                message.LockedBy = null;
                message.LockedUntil = null;
                // Exponential backoff so a transient broker outage doesn't
                // hot-loop the batch. Capped at one hour. Use fresh UtcNow so
                // the backoff starts from the actual failure time.
                message.NextAttemptAt = DateTime.UtcNow.AddSeconds(
                    Math.Min(options.InitialBackoffSec
                              * Math.Pow(2, message.RetryCount), 3600));

                if (message.RetryCount >= options.MaxRetries)
                    logger.LogError(ex,
                        "Outbox message {Id} ({Type}) dead-lettered after {N} attempts.",
                        message.Id, message.MessageType, message.RetryCount);
                else
                    logger.LogWarning(ex,
                        "Outbox dispatch failed for {Id} (attempt {N}); next attempt at {Next}.",
                        message.Id, message.RetryCount, message.NextAttemptAt);
            }
            finally
            {
                correlationScope?.Dispose();
                tenantScope?.Dispose();
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bounded housekeeping delete of rows past their retention window:
    /// successfully dispatched rows (by ProcessedAt) and dead-lettered rows
    /// (RetryCount exhausted) older than PurgeAfterDays. Capped per poll so
    /// cleanup never monopolises a cycle and table growth stays bounded even
    /// under sustained failure storms.
    /// </summary>
    private static async Task PurgeExpiredAsync(
        DbContext db, OutboxOptions options, CancellationToken ct)
    {
        if (options.PurgeAfterDays <= 0)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-options.PurgeAfterDays);

        var expiredIds = await db.Set<OutboxMessage>()
            .Where(m => (m.ProcessedAt != null && m.ProcessedAt < cutoff)
                     || (m.ProcessedAt == null
                         && m.RetryCount >= options.MaxRetries
                         && m.CreatedAt < cutoff))
            .OrderBy(m => m.Id)
            .Take(PurgeBatchSize)
            .Select(m => m.Id)
            .ToListAsync(ct);
        if (expiredIds.Count == 0)
            return;

        await db.Set<OutboxMessage>()
            .Where(m => expiredIds.Contains(m.Id))
            .ExecuteDeleteAsync(ct);
    }
}
