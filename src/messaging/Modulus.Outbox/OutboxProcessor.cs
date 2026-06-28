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
            await ProcessContextAsync(db, ssp, options, ct);
    }

    private async Task ProcessContextAsync(
        DbContext db,
        IServiceProvider ssp,
        OutboxOptions options,
        CancellationToken ct)
    {
        var dispatcher = ssp.GetRequiredService<IOutboxDispatcher>();
        var now = DateTime.UtcNow;
        var lockUntil = now.AddSeconds(options.LockTimeoutSec);

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
        await db.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id)
                     && m.ProcessedAt == null
                     && (m.LockedUntil == null || m.LockedUntil < now))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.LockedBy, _instanceId)
                      .SetProperty(m => m.LockedUntil, lockUntil),
                ct);

        // 3. Load the rows this instance now owns (fresh from the DB so the
        //    claim state is visible). Ignore any whose backoff hasn't elapsed.
        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.LockedBy == _instanceId && m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        // 4. Dispatch each. Dispatch is an irreversible side effect, so we mark
        //    ProcessedAt only AFTER success. If the process crashes between a
        //    successful dispatch and SaveChanges, the LockTimeout will expire
        //    the claim and the message is redelivered (at-least-once) —
        //    consumers MUST dedup via the inbox.
        var currentTenant = ssp.GetService<ICurrentTenant>();
        foreach (var message in messages)
        {
            // Restore the tenant context captured when the outbox row was
            // written so downstream handlers (query filters, etc.) see the
            // correct tenant. Host/empty-tenant messages dispatch unchanged.
            IDisposable? tenantScope = null;
            if (message.TenantId != Guid.Empty && currentTenant is not null)
            {
                tenantScope = currentTenant.Change(
                    new TenantInfo(message.TenantId, message.TenantId.ToString("N")));
            }

            try
            {
                await dispatcher.DispatchAsync(message, ct);
                message.ProcessedAt = now;
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
                // hot-loop the batch. Capped at one hour.
                message.NextAttemptAt = now.AddSeconds(
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
                tenantScope?.Dispose();
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
