namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Adds queue rows to a context without saving, so a caller can save them together with other changes (a backfill page and its
/// cursor). A record already queued, or repeated within the call, is skipped.
/// </summary>
internal static class AnalyticsDeliveryStaging
{
    internal static async Task<int> StageAsync(
        ModulusAnalyticsDbContext db,
        IAnalyticsDatasetCatalog catalog,
        TimeProvider clock,
        IReadOnlyList<AnalyticsRecord> records,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var seen = new HashSet<(Guid Tenant, string Dataset, Guid Event)>();
        var added = 0;
        foreach (var record in records)
        {
            if (!catalog.TryGet(record.Dataset, record.SchemaVersion, out var dataset))
            {
                throw new InvalidOperationException($"Dataset '{record.Dataset}.v{record.SchemaVersion}' is not registered.");
            }

            if (!seen.Add((record.TenantId, record.Dataset, record.EventId)))
            {
                continue;
            }

            var exists = await db.AnalyticsDeliveries.AsNoTracking()
                .AnyAsync(d => d.TenantId == record.TenantId && d.Dataset == record.Dataset && d.EventId == record.EventId, cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                continue;
            }

            db.AnalyticsDeliveries.Add(new AnalyticsDelivery
            {
                TenantId = record.TenantId,
                EventId = record.EventId,
                Dataset = record.Dataset,
                SchemaVersion = record.SchemaVersion,
                OccurredAt = record.OccurredAt.UtcDateTime,
                Payload = AnalyticsValueSerializer.Serialize(dataset, record.Values),
                CreatedAt = now,
            });
            added++;
        }

        return added;
    }
}
