namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The durable <see cref="IAnalyticsQueue"/>. It writes rows through the context it is given, so inside a unit of work the caller
/// commits them with the rest. A record already queued (same tenant, dataset and event id) is skipped.
/// </summary>
internal sealed class EfAnalyticsQueue(ModulusAnalyticsDbContext db, IAnalyticsDatasetCatalog catalog, TimeProvider clock) : IAnalyticsQueue
{
    public async Task EnqueueAsync(IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        var added = await AnalyticsDeliveryStaging.StageAsync(db, catalog, clock, records, cancellationToken).ConfigureAwait(false);
        if (added == 0)
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent handler of the same event may have won the unique index; then its rows stand.
            db.ChangeTracker.Clear();
            var ids = records.Select(r => r.EventId).ToList();
            var stored = await db.AnalyticsDeliveries.AsNoTracking().CountAsync(d => ids.Contains(d.EventId), cancellationToken).ConfigureAwait(false);
            if (stored < records.Select(r => (r.TenantId, r.Dataset, r.EventId)).Distinct().Count())
            {
                throw;
            }
        }
    }
}
