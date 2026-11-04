namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Adds queue rows to a context without saving, so a caller can save them together with other changes (a backfill page and its
/// cursor). A record revision already queued, or repeated within the call, is skipped.
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
        var seen = new HashSet<(Guid Tenant, string Dataset, Guid Event, long Revision)>();
        var added = 0;
        // One fixed order for everyone: writers that stage overlapping records then take the unique-index locks in the same order and cannot deadlock.
        foreach (var record in records.OrderBy(r => r.TenantId).ThenBy(r => r.Dataset, StringComparer.Ordinal).ThenBy(r => r.EventId).ThenBy(r => r.Revision))
        {
            if (!catalog.TryGet(record.Dataset, record.SchemaVersion, out var dataset))
            {
                throw new InvalidOperationException($"Dataset '{record.Dataset}.v{record.SchemaVersion}' is not registered.");
            }

            if (!seen.Add((record.TenantId, record.Dataset, record.EventId, record.Revision)))
            {
                continue;
            }

            var exists = await db.AnalyticsDeliveries.AsNoTracking()
                .AnyAsync(d => d.TenantId == record.TenantId && d.Dataset == record.Dataset && d.EventId == record.EventId && d.Revision == record.Revision, cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                continue;
            }

            db.AnalyticsDeliveries.Add(new AnalyticsDelivery
            {
                // Derived from the record's identity, not random: EF Core inserts a batch in primary-key order, so writers that stage overlapping records
                // then insert them in the same order, take their locks in the same order, and cannot deadlock.
                Id = AnalyticsRecordIds.Deterministic(
                    "analytics-delivery",
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{record.TenantId:N}|{record.Dataset}|{record.EventId:N}|{record.Revision}")),
                TenantId = record.TenantId,
                EventId = record.EventId,
                Revision = record.Revision,
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
