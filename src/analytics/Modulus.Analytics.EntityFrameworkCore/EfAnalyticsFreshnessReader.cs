namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>Reads freshness from the delivery queue: what is waiting, since when, and what was delivered last, per dataset version.</summary>
internal sealed class EfAnalyticsFreshnessReader(ModulusAnalyticsDbContext db, IAnalyticsDatasetCatalog catalog, TimeProvider clock) : IAnalyticsFreshnessReader
{
    public async Task<IReadOnlyList<AnalyticsDatasetFreshness>> ReadAsync(CancellationToken cancellationToken)
    {
        var rows = await db.AnalyticsDeliveries.AsNoTracking()
            .GroupBy(d => new { d.Dataset, d.SchemaVersion })
            .Select(g => new
            {
                g.Key.Dataset,
                g.Key.SchemaVersion,
                Pending = g.Count(d => d.DeliveredAt == null && d.DeadLetteredAt == null),
                DeadLettered = g.Count(d => d.DeadLetteredAt != null),
                OldestPending = g.Where(d => d.DeliveredAt == null && d.DeadLetteredAt == null).Min(d => (DateTime?)d.CreatedAt),
                LastDelivered = g.Max(d => d.DeliveredAt),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var stats = rows.ToDictionary(
            r => (r.Dataset, r.SchemaVersion),
            r => new AnalyticsQueueStats(r.Pending, r.DeadLettered, r.OldestPending, r.LastDelivered));
        var now = clock.GetUtcNow().UtcDateTime;
        var result = new List<AnalyticsDatasetFreshness>();
        foreach (var dataset in catalog.All)
        {
            var hasStats = stats.TryGetValue((dataset.Name, dataset.Version), out var s);
            var isLatest = catalog.Latest(dataset.Name)?.Version == dataset.Version;

            // The latest version of every dataset is always reported. An older version only while it still has records to deal with.
            if (!isLatest && !(hasStats && (s!.Pending > 0 || s.DeadLettered > 0)))
            {
                continue;
            }

            result.Add(AnalyticsFreshness.Evaluate(dataset, s ?? new AnalyticsQueueStats(0, 0, null, null), now));
        }

        return result;
    }
}
