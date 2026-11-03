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

        var now = clock.GetUtcNow().UtcDateTime;
        var added = new List<AnalyticsDelivery>();
        foreach (var record in records)
        {
            if (!catalog.TryGet(record.Dataset, record.SchemaVersion, out var dataset))
            {
                throw new InvalidOperationException($"Dataset '{record.Dataset}.v{record.SchemaVersion}' is not registered.");
            }

            var exists = await db.AnalyticsDeliveries.AsNoTracking()
                .AnyAsync(d => d.TenantId == record.TenantId && d.Dataset == record.Dataset && d.EventId == record.EventId, cancellationToken)
                .ConfigureAwait(false);
            if (exists || added.Any(d => d.TenantId == record.TenantId && d.Dataset == record.Dataset && d.EventId == record.EventId))
            {
                continue;
            }

            added.Add(new AnalyticsDelivery
            {
                TenantId = record.TenantId,
                EventId = record.EventId,
                Dataset = record.Dataset,
                SchemaVersion = record.SchemaVersion,
                OccurredAt = record.OccurredAt.UtcDateTime,
                Payload = AnalyticsValueSerializer.Serialize(dataset, record.Values),
                CreatedAt = now,
            });
        }

        if (added.Count == 0)
        {
            return;
        }

        db.AnalyticsDeliveries.AddRange(added);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent handler of the same event may have won the unique index; then its rows stand.
            db.ChangeTracker.Clear();
            var ids = added.Select(d => d.EventId).ToList();
            var stored = await db.AnalyticsDeliveries.AsNoTracking().CountAsync(d => ids.Contains(d.EventId), cancellationToken).ConfigureAwait(false);
            if (stored < added.Count)
            {
                throw;
            }
        }
    }
}
