namespace Modulus.Authorization.EntityFrameworkCore;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.DataQuality;

/// <summary>
/// Drains the <see cref="DataQualityFindingQueue"/> into the store in batches and purges expired findings on the configured
/// interval. A failed write is logged and its batch dropped, so a database outage never stops the service. Findings still
/// queued at shutdown are not written.
/// </summary>
public sealed class DataQualityFindingService(
    DataQualityFindingQueue queue,
    DataQualityFindingStore store,
    IOptions<DataQualityFindingOptions> options,
    ILogger<DataQualityFindingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(WriteLoopAsync(stoppingToken), PurgeLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        var batchSize = options.Value.BatchSize;
        var batch = new List<DataQualityFinding>(batchSize);
        while (await queue.Reader.WaitToReadAsync(ct))
        {
            while (batch.Count < batchSize && queue.Reader.TryRead(out var finding))
                batch.Add(finding);

            try
            {
                await store.AddAsync(batch, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not store {Count} data-quality findings; the batch is dropped", batch.Count);
            }

            batch.Clear();
        }
    }

    private async Task PurgeLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Value.PurgeInterval);
        do
        {
            try
            {
                var removed = await store.PurgeAsync(ct);
                if (removed > 0)
                    logger.LogInformation("Purged {Count} data-quality findings past their retention", removed);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not purge expired data-quality findings");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
