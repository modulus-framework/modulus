namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The durable <see cref="IAnalyticsQueue"/>. It writes rows through the context it is given, so inside a unit of work the caller
/// commits them with the rest. A record revision already queued (same tenant, dataset, event id and revision) is skipped, and writers that collide retry.
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

        for (var attempt = 1; ; attempt++)
        {
            var added = await AnalyticsDeliveryStaging.StageAsync(db, catalog, clock, records, cancellationToken).ConfigureAwait(false);
            if (added == 0)
            {
                return;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (AnalyticsDbRaces.IsRace(ex) && attempt < MaxAttempts)
            {
                // Another writer queued the same record revision, or the database picked us as a deadlock victim. Stage again: rows the
                // other writer committed are skipped now.
                db.ChangeTracker.Clear();
                await AnalyticsDbRaces.BackoffAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private const int MaxAttempts = 5;
}
