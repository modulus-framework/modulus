namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// One delivery cycle, modelled on the webhook processor: pick due rows, claim them atomically (a conditional <c>UPDATE</c>, so
/// replicas never deliver the same row at the same time), hand each dataset version's rows to <see cref="AnalyticsDispatcher"/>, then
/// record the outcome: delivered, retry later on the schedule, or dead-lettered. Delivery is at least once, and a failed sink makes
/// the whole batch retry, so sinks must tolerate the same record twice (the conformance suite checks that).
/// </summary>
internal sealed class AnalyticsDeliveryProcessor(
    ModulusAnalyticsDbContext db,
    AnalyticsDispatcher dispatcher,
    IAnalyticsDatasetCatalog catalog,
    IOptions<AnalyticsDeliveryOptions> options,
    TimeProvider clock,
    ILogger<AnalyticsDeliveryProcessor> logger)
{
    private const int MaxErrorLength = 2000;
    private const int PurgeBatchSize = 1000;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>Runs one cycle; returns the number of rows it picked up.</summary>
    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        await PurgeAsync(settings, ct).ConfigureAwait(false);

        if (!dispatcher.HasSinks)
        {
            // With no sink every record would count as delivered and be lost; leave them queued.
            logger.LogWarning("Analytics delivery is idle: no analytics sink is registered, so queued records wait.");
            return 0;
        }

        var now = Now();
        var candidates = await db.AnalyticsDeliveries
            .Where(d => d.DeliveredAt == null && d.DeadLetteredAt == null
                     && (d.LockedUntil == null || d.LockedUntil < now)
                     && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(Math.Max(1, settings.BatchSize))
            .Select(d => d.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var claimedAt = Now();
        var lockUntil = claimedAt + settings.LockDuration;
        try
        {
            await db.AnalyticsDeliveries
                .Where(d => candidates.Contains(d.Id) && d.DeliveredAt == null && d.DeadLetteredAt == null
                         && (d.LockedUntil == null || d.LockedUntil < claimedAt))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LockedBy, _instanceId).SetProperty(d => d.LockedUntil, lockUntil), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (AnalyticsDbRaces.IsRace(ex))
        {
            // Another worker is claiming overlapping rows and the database chose this claim as the deadlock victim. Skip the cycle.
            return 0;
        }

        var claimed = await db.AnalyticsDeliveries
            .Where(d => candidates.Contains(d.Id) && d.LockedBy == _instanceId && d.DeliveredAt == null && d.DeadLetteredAt == null)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        if (claimed.Count == 0)
        {
            return 0;
        }

        foreach (var group in claimed.GroupBy(d => (d.Dataset, d.SchemaVersion)))
        {
            await DeliverGroupAsync(group.Key.Dataset, group.Key.SchemaVersion, group.ToList(), settings, ct).ConfigureAwait(false);
        }

        // Not `ct`: the sinks already have the data; losing the bookkeeping to a shutdown would resend it.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return claimed.Count;
    }

    private async Task DeliverGroupAsync(
        string dataset, int version, List<AnalyticsDelivery> rows, AnalyticsDeliveryOptions settings, CancellationToken ct)
    {
        var finishedAt = Now();
        if (!catalog.TryGet(dataset, version, out var descriptor))
        {
            foreach (var row in rows)
            {
                DeadLetter(row, $"Dataset '{dataset}.v{version}' is not registered.", finishedAt);
            }

            return;
        }

        // A newer revision of the same record makes an older one pointless (and delivering it later could overwrite newer data), so the older
        // one is marked done without being sent.
        var eventIds = rows.Select(r => r.EventId).Distinct().ToList();
        var newest = (await db.AnalyticsDeliveries.AsNoTracking()
            .Where(d => d.Dataset == dataset && d.DeadLetteredAt == null && eventIds.Contains(d.EventId))
            .Select(d => new { d.TenantId, d.EventId, d.Revision })
            .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(d => (d.TenantId, d.EventId))
            .ToDictionary(g => g.Key, g => g.Max(d => d.Revision));
        var current = new List<AnalyticsDelivery>(rows.Count);
        foreach (var row in rows)
        {
            if (newest.TryGetValue((row.TenantId, row.EventId), out var top) && top > row.Revision)
            {
                row.DeliveredAt = finishedAt;
                row.LastAttemptAt = finishedAt;
                row.LastError = "Superseded by a newer revision.";
                Release(row);
            }
            else
            {
                current.Add(row);
            }
        }

        rows = current;
        var records = new List<AnalyticsRecord>(rows.Count);
        var byEventId = new Dictionary<(Guid Tenant, Guid Event), AnalyticsDelivery>();
        foreach (var row in rows)
        {
            try
            {
                records.Add(new AnalyticsRecord(
                    row.EventId, row.Dataset, row.SchemaVersion, row.TenantId,
                    new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)),
                    AnalyticsValueSerializer.Deserialize(descriptor, row.Payload),
                    row.Revision));
                byEventId[(row.TenantId, row.EventId)] = row;
            }
            catch (InvalidOperationException ex)
            {
                DeadLetter(row, ex.Message, finishedAt);
            }
        }

        if (records.Count == 0)
        {
            return;
        }

        AnalyticsDispatchResult result;
        try
        {
            result = await dispatcher.DispatchAsync(records, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            foreach (var row in byEventId.Values)
            {
                Release(row);
            }

            return;
        }

        finishedAt = Now();
        foreach (var rejection in result.Rejected)
        {
            if (byEventId.Remove((rejection.Record.TenantId, rejection.Record.EventId), out var row))
            {
                DeadLetter(row, rejection.Reason, finishedAt);
            }
        }

        var failures = result.Outcomes.Where(o => !o.Succeeded).ToList();
        foreach (var row in byEventId.Values)
        {
            if (failures.Count == 0)
            {
                row.DeliveredAt = finishedAt;
                row.LastAttemptAt = finishedAt;
                row.LastError = null;
                Release(row);
            }
            else
            {
                var error = string.Join("; ", failures.Select(f => $"{f.Sink}: {f.Error}"));
                if (failures.Any(f => !f.IsTransient))
                {
                    DeadLetter(row, error, finishedAt);
                }
                else
                {
                    Retry(row, error, settings, finishedAt);
                }
            }
        }
    }

    private void Retry(AnalyticsDelivery row, string error, AnalyticsDeliveryOptions settings, DateTime at)
    {
        row.AttemptCount++;
        row.LastAttemptAt = at;
        row.LastError = Truncate(error);
        if (row.AttemptCount > settings.RetrySchedule.Length)
        {
            DeadLetter(row, error, at);
            return;
        }

        row.NextAttemptAt = at + settings.RetrySchedule[row.AttemptCount - 1];
        Release(row);
    }

    private void DeadLetter(AnalyticsDelivery row, string error, DateTime at)
    {
        row.DeadLetteredAt = at;
        AnalyticsMetrics.DeadLettered.Add(1, AnalyticsMetrics.Tags(row.Dataset));
        row.LastAttemptAt = at;
        row.LastError = Truncate(error);
        Release(row);
        logger.LogWarning("Analytics record {EventId} for {Dataset} was dead-lettered: {Error}", row.EventId, row.Dataset, row.LastError);
    }

    private static void Release(AnalyticsDelivery row)
    {
        row.LockedBy = null;
        row.LockedUntil = null;
    }

    private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    private async Task PurgeAsync(AnalyticsDeliveryOptions settings, CancellationToken ct)
    {
        var cutoff = Now() - settings.PurgeDeliveredAfter;
        int purged;
        do
        {
            var ids = await db.AnalyticsDeliveries
                .Where(d => d.DeliveredAt != null && d.DeliveredAt < cutoff)
                .OrderBy(d => d.DeliveredAt)
                .Take(PurgeBatchSize)
                .Select(d => d.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            purged = ids.Count == 0
                ? 0
                : await db.AnalyticsDeliveries.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }
        while (purged == PurgeBatchSize);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
