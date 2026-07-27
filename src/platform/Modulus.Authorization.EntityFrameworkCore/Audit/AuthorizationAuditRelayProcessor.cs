namespace Modulus.Authorization.EntityFrameworkCore.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Outbox.Abstractions;

/// <summary>
/// Drains <see cref="AuthorizationStoreDbContext"/>'s own audit outbox table
/// and dispatches each row via the host's registered <see cref="IOutboxDispatcher"/>.
/// A dedicated processor (rather than folding into <c>Modulus.Outbox</c>'s
/// <c>OutboxProcessor</c>) because that processor only scans <see cref="DbContext"/>s
/// registered as bare <c>DbContext</c> — <see cref="AuthorizationStoreDbContext"/>
/// is deliberately registered only via <see cref="IDbContextFactory{TContext}"/>
/// so it stays out of the module transaction fan-out and migration loop; this
/// reaches it through the same factory instead. The claim/backoff logic mirrors
/// <c>OutboxProcessor</c> so audit rows get the same at-least-once,
/// exponential-backoff, dead-letter-after-max-retries guarantees.
/// </summary>
/// <remarks>
/// Requires <b>some</b> <see cref="IOutboxDispatcher"/> to be registered — either
/// because the host already called <c>Modulus.Outbox</c>'s <c>AddOutbox&lt;T&gt;</c>
/// for one of its own module contexts (which registers a default in-process
/// dispatcher), or because it registered its own. Audit rows are always
/// durably persisted by <c>EfAuthorizationAuditWriter</c> regardless; without a
/// dispatcher registered, <see cref="ProcessAsync"/> throws and the caller
/// (<see cref="AuthorizationAuditRelayService"/>) logs and retries next
/// interval — rows simply accumulate undelivered rather than being lost.
/// </remarks>
public sealed class AuthorizationAuditRelayProcessor(
    IServiceProvider sp,
    IOptions<AuthorizationAuditOptions> opts,
    ILogger<AuthorizationAuditRelayProcessor> logger)
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    public async Task ProcessAsync(CancellationToken ct = default)
    {
        await using var scope = sp.CreateAsyncScope();
        var ssp = scope.ServiceProvider;
        var options = opts.Value;

        var factory = ssp.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>();
        var dispatcher = ssp.GetRequiredService<IOutboxDispatcher>();
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;

        // 1. Pick candidates: unprocessed, under the retry budget, not currently
        //    locked by another instance, and whose backoff has elapsed.
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

        // 2. Atomically claim those rows for this instance (server-evaluated
        //    WHERE re-check on LockedUntil serializes concurrent claimants).
        var lockUntil = DateTime.UtcNow.AddSeconds(options.LockTimeoutSec);
        await db.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id)
                     && m.ProcessedAt == null
                     && (m.LockedUntil == null || m.LockedUntil < DateTime.UtcNow))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.LockedBy, _instanceId)
                      .SetProperty(m => m.LockedUntil, lockUntil),
                ct);

        // 3. Load only the rows this instance actually claimed in this batch.
        var messages = await db.Set<OutboxMessage>()
            .Where(m => candidateIds.Contains(m.Id)
                     && m.LockedBy == _instanceId
                     && m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        var currentTenant = ssp.GetService<ICurrentTenant>();
        var correlation = ssp.GetService<ICorrelationContext>();

        foreach (var message in messages)
        {
            IDisposable? tenantScope = currentTenant is null
                ? null
                : currentTenant.Change(message.TenantId == Guid.Empty
                    ? null
                    : new TenantInfo(message.TenantId, message.TenantId.ToString("N")));

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
                logger.LogDebug("Authorization audit event dispatched {Id} ({Type})",
                    message.Id, message.MessageType);
            }
            catch (Exception ex)
            {
                message.RetryCount += 1;
                message.Error = ex.Message;
                message.LockedBy = null;
                message.LockedUntil = null;
                message.NextAttemptAt = DateTime.UtcNow.AddSeconds(
                    Math.Min(options.InitialBackoffSec
                              * Math.Pow(2, message.RetryCount), 3600));

                if (message.RetryCount >= options.MaxRetries)
                    logger.LogError(ex,
                        "Authorization audit event {Id} ({Type}) dead-lettered after {N} attempts.",
                        message.Id, message.MessageType, message.RetryCount);
                else
                    logger.LogWarning(ex,
                        "Authorization audit dispatch failed for {Id} (attempt {N}); next attempt at {Next}.",
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
}
