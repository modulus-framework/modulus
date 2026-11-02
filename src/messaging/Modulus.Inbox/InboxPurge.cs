namespace Modulus.Inbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Inbox.Abstractions;

/// <summary>
/// Housekeeping for the EF Core inbox: deletes processed rows older than <see cref="InboxOptions.ProcessedRetentionDays"/>.
/// Rows in <see cref="InboxStatus.Pending"/>, <see cref="InboxStatus.Processing"/> or <see cref="InboxStatus.Failed"/> are never
/// touched, so an event that is still being retried keeps its dedup record. The host pass runs every context in the host
/// context; a context whose database is kept per tenant is also purged in each active tenant's own database.
/// </summary>
internal static class InboxPurge
{
    /// <summary>Rows deleted per statement, so a large backlog never holds one long delete.</summary>
    internal const int BatchSize = 1000;

    /// <summary>Statements per context per pass; the rest waits for the next pass so a backlog cannot monopolise the database.</summary>
    internal const int MaxBatchesPerPass = 100;

    /// <summary>Runs one purge pass over every inbox context and returns the number of rows deleted.</summary>
    internal static async Task<int> RunAsync(IServiceProvider sp, InboxOptions options, ILogger logger, CancellationToken ct)
    {
        if (options.ProcessedRetentionDays <= 0)
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-options.ProcessedRetentionDays);
        var deleted = 0;

        await using var scope = sp.CreateAsyncScope();
        var ssp = scope.ServiceProvider;

        var contexts = ssp.GetServices<DbContext>()
            .GroupBy(db => db.GetType())
            .Select(g => g.First())
            .Where(db => db.Model.FindEntityType(typeof(InboxMessage)) is not null)
            .ToList();

        using (ssp.GetService<ICurrentTenant>()?.Change(null))
        {
            foreach (var db in contexts)
            {
                try
                {
                    deleted += await PurgeContextAsync(db, cutoff, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Inbox purge failed for context {Context}; continuing with the other contexts.", db.GetType().Name);
                }
            }
        }

        var perTenantTypes = ssp.GetServices<TenantDatabaseRoute>().Select(r => r.ContextType).ToHashSet();
        if (perTenantTypes.Count == 0)
            return deleted;

        var directory = ssp.GetService<ITenantDirectory>();
        if (directory is null)
        {
            logger.LogError("Inbox rows are kept per tenant, but no ITenantDirectory is registered; they are not purged. Call AddMultiTenancy.");
            return deleted;
        }

        foreach (var tenantId in await directory.ListActiveTenantIdsAsync(ct))
        {
            try
            {
                await using var tenantScope = sp.CreateAsyncScope();
                var tsp = tenantScope.ServiceProvider;
                using var entered = tsp.EnterTenant(await tsp.VerifyTenantAsync(tenantId, ct));
                foreach (var contextType in perTenantTypes)
                {
                    var db = (DbContext)tsp.GetRequiredService(contextType);
                    if (db.Model.FindEntityType(typeof(InboxMessage)) is not null)
                        deleted += await PurgeContextAsync(db, cutoff, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Inbox purge failed in tenant {TenantId}; continuing with the other tenants.", tenantId);
            }
        }

        return deleted;
    }

    private static async Task<int> PurgeContextAsync(DbContext db, DateTime cutoff, CancellationToken ct)
    {
        var total = 0;
        for (var batch = 0; batch < MaxBatchesPerPass; batch++)
        {
            var removed = await db.Set<InboxMessage>()
                .Where(m => m.Status == InboxStatus.Processed && m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);
            total += removed;
            if (removed < BatchSize)
                break;
        }

        return total;
    }
}
