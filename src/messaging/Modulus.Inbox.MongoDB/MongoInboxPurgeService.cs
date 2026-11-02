namespace Modulus.Inbox.MongoDB;

using global::MongoDB.Driver;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Inbox.Abstractions;

/// <summary>
/// Housekeeping for the MongoDB inbox: deletes processed documents older than <see cref="InboxOptions.ProcessedRetentionDays"/>,
/// in batches, every <see cref="InboxOptions.PurgeIntervalMinutes"/>. Pending, processing and failed documents are never deleted,
/// so an event that is still being retried keeps its dedup record. The counterpart of the EF Core inbox purge.
/// </summary>
internal sealed class MongoInboxPurgeService(
    IMongoCollection<MongoInboxMessage> collection,
    IOptions<InboxOptions> opts,
    ILogger<MongoInboxPurgeService> logger)
    : BackgroundService
{
    /// <summary>Documents deleted per batch, so a large backlog never holds one long delete.</summary>
    internal const int BatchSize = 1000;

    /// <summary>Batches per pass; the rest waits for the next pass so a backlog cannot monopolise the cluster.</summary>
    internal const int MaxBatchesPerPass = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await PurgeAsync(stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Inbox purge removed {Count} processed documents.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbox purge pass failed; it is retried at the next interval.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, opts.Value.PurgeIntervalMinutes)), stoppingToken);
        }
    }

    /// <summary>Runs one purge pass and returns the number of documents deleted.</summary>
    internal async Task<long> PurgeAsync(CancellationToken ct)
    {
        var days = opts.Value.ProcessedRetentionDays;
        if (days <= 0)
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var expired = Builders<MongoInboxMessage>.Filter.Eq(x => x.Status, InboxStatus.Processed)
            & Builders<MongoInboxMessage>.Filter.Lt(x => x.ProcessedAt, cutoff);

        long total = 0;
        for (var batch = 0; batch < MaxBatchesPerPass; batch++)
        {
            var ids = await collection.Find(expired)
                .Project(x => x.Id)
                .Limit(BatchSize)
                .ToListAsync(ct);
            if (ids.Count == 0)
                break;

            // The expired filter is repeated on the delete, so a document that changed since the find is not removed by id alone.
            var result = await collection.DeleteManyAsync(
                expired & Builders<MongoInboxMessage>.Filter.In(x => x.Id, ids), ct);
            total += result.DeletedCount;
            if (ids.Count < BatchSize)
                break;
        }

        return total;
    }
}
