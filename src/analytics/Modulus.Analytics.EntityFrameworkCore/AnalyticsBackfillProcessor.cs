namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>A source returned records that do not belong to the run. The run fails at once, because retrying cannot fix it.</summary>
internal sealed class AnalyticsBackfillSourceException(string message) : Exception(message);

/// <summary>
/// One backfill cycle: claim runs (a conditional <c>UPDATE</c>, so replicas never read the same run), then for each run enter its tenant,
/// read pages from the source and queue each page together with the new cursor in a single transaction. A worker that dies mid-run
/// loses nothing: the claim expires and another worker continues from the saved cursor.
/// </summary>
internal sealed class AnalyticsBackfillProcessor(
    ModulusAnalyticsDbContext db,
    IAnalyticsDatasetCatalog catalog,
    IEnumerable<IAnalyticsSource> sources,
    IServiceProvider services,
    IOptions<AnalyticsBackfillOptions> options,
    TimeProvider clock,
    ILogger<AnalyticsBackfillProcessor> logger)
{
    private const int MaxErrorLength = 2000;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>Runs one cycle; returns the number of runs it worked on.</summary>
    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        var now = Now();
        var candidates = await db.AnalyticsBackfillRuns
            .Where(r => (r.Status == AnalyticsBackfillStatus.Pending || r.Status == AnalyticsBackfillStatus.Running)
                     && (r.LockedUntil == null || r.LockedUntil < now)
                     && (r.NextAttemptAt == null || r.NextAttemptAt <= now))
            .OrderBy(r => r.CreatedAt)
            .Take(Math.Max(1, settings.RunsPerCycle))
            .Select(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var lockUntil = now + settings.LockDuration;
        await db.AnalyticsBackfillRuns
            .Where(r => candidates.Contains(r.Id)
                     && (r.Status == AnalyticsBackfillStatus.Pending || r.Status == AnalyticsBackfillStatus.Running)
                     && (r.LockedUntil == null || r.LockedUntil < now))
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.LockedBy, _instanceId)
                      .SetProperty(r => r.LockedUntil, (DateTime?)lockUntil)
                      .SetProperty(r => r.Status, AnalyticsBackfillStatus.Running),
                ct).ConfigureAwait(false);

        var claimed = await db.AnalyticsBackfillRuns.AsNoTracking()
            .Where(r => candidates.Contains(r.Id) && r.LockedBy == _instanceId && r.Status == AnalyticsBackfillStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Dataset, r.SchemaVersion, r.TenantId })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var run in claimed)
        {
            await ProcessRunAsync(run.Id, run.Dataset, run.SchemaVersion, run.TenantId, settings, ct).ConfigureAwait(false);
        }

        return claimed.Count;
    }

    private async Task ProcessRunAsync(Guid runId, string dataset, int version, Guid tenantId, AnalyticsBackfillOptions settings, CancellationToken ct)
    {
        var source = sources.FirstOrDefault(s => s.Dataset == dataset && s.Version == version);
        if (source is null || !catalog.TryGet(dataset, version, out _))
        {
            await FailAsync(runId, $"No analytics source or dataset is registered for '{dataset}.v{version}'.").ConfigureAwait(false);
            return;
        }

        TenantInfo tenant;
        try
        {
            tenant = await services.VerifyTenantAsync(tenantId, ct).ConfigureAwait(false);
        }
        catch (TenantContextRejectedException)
        {
            await FailAsync(runId, $"Tenant '{tenantId}' is unknown or inactive.").ConfigureAwait(false);
            return;
        }

        // The tenant is entered here, in the frame that runs the work, and left when this method returns.
        using var scope = services.EnterTenant(tenant);
        try
        {
            for (var page = 0; page < Math.Max(1, settings.PagesPerCycle); page++)
            {
                if (await ReadPageAsync(runId, dataset, version, tenantId, source, settings, ct).ConfigureAwait(false))
                {
                    return;
                }
            }

            await ReleaseAsync(runId).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseAsync(runId).ConfigureAwait(false);
        }
        catch (AnalyticsBackfillSourceException ex)
        {
            await FailAsync(runId, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RetryLaterAsync(runId, ex, settings).ConfigureAwait(false);
        }
    }

    /// <summary>Reads, queues and checkpoints one page. Returns true when the run is finished.</summary>
    private async Task<bool> ReadPageAsync(
        Guid runId, string dataset, int version, Guid tenantId, IAnalyticsSource source, AnalyticsBackfillOptions settings, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var run = await db.AnalyticsBackfillRuns.SingleAsync(r => r.Id == runId, ct).ConfigureAwait(false);
            var page = await source.ExtractAsync(new AnalyticsExtractRequest(tenantId, run.Cursor, Math.Max(1, settings.PageSize)), ct).ConfigureAwait(false);
            foreach (var record in page.Records)
            {
                if (record.TenantId != tenantId || record.Dataset != dataset || record.SchemaVersion != version)
                {
                    throw new AnalyticsBackfillSourceException(
                        $"The source for '{dataset}.v{version}' returned a record for '{record.Dataset}.v{record.SchemaVersion}' of tenant '{record.TenantId}' while reading tenant '{tenantId}'.");
                }
            }

            var staged = await AnalyticsDeliveryStaging.StageAsync(db, catalog, clock, page.Records, ct).ConfigureAwait(false);

            var now = Now();
            var done = page.NextCursor is null;
            run.Cursor = page.NextCursor;
            run.RowsQueued += page.Records.Count;
            run.AttemptCount = 0;
            run.NextAttemptAt = null;
            run.LastError = null;
            run.UpdatedAt = now;
            if (done)
            {
                run.Status = AnalyticsBackfillStatus.Completed;
                run.CompletedAt = now;
                run.LockedBy = null;
                run.LockedUntil = null;
            }
            else
            {
                run.LockedUntil = now + settings.LockDuration;
            }

            try
            {
                // The queued rows and the cursor commit together: a crash before this loses nothing and repeats nothing.
                await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                AnalyticsMetrics.BackfillRowsQueued.Add(staged, AnalyticsMetrics.Tags(dataset));
                if (done)
                {
                    logger.LogInformation("Analytics backfill of {Dataset}.v{Version} for tenant {Tenant} completed ({Rows} records queued).", dataset, version, tenantId, run.RowsQueued);
                }

                return done;
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                // A live event for the same record may have been queued meanwhile and won the unique index. Read the page again;
                // staging now skips that record.
            }
        }
    }

    private async Task ReleaseAsync(Guid runId)
    {
        db.ChangeTracker.Clear();
        await db.AnalyticsBackfillRuns.Where(r => r.Id == runId && r.LockedBy == _instanceId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LockedBy, (string?)null).SetProperty(r => r.LockedUntil, (DateTime?)null)).ConfigureAwait(false);
    }

    private async Task FailAsync(Guid runId, string error)
    {
        db.ChangeTracker.Clear();
        var now = Now();
        var message = Truncate(error);
        await db.AnalyticsBackfillRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, AnalyticsBackfillStatus.Failed)
                .SetProperty(r => r.LastError, message)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.LockedBy, (string?)null)
                .SetProperty(r => r.LockedUntil, (DateTime?)null)).ConfigureAwait(false);
        logger.LogWarning("Analytics backfill run {Run} failed: {Error}", runId, message);
    }

    private async Task RetryLaterAsync(Guid runId, Exception exception, AnalyticsBackfillOptions settings)
    {
        db.ChangeTracker.Clear();
        var attempts = await db.AnalyticsBackfillRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => r.AttemptCount).SingleAsync().ConfigureAwait(false) + 1;
        if (attempts >= Math.Max(1, settings.MaxAttempts))
        {
            await FailAsync(runId, $"Gave up after {attempts} failed cycles: {exception.Message}").ConfigureAwait(false);
            return;
        }

        var now = Now();
        var next = now + (settings.RetryDelay * attempts);
        var message = Truncate(exception.Message);
        await db.AnalyticsBackfillRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AttemptCount, attempts)
                .SetProperty(r => r.LastError, message)
                .SetProperty(r => r.UpdatedAt, now)
                .SetProperty(r => r.NextAttemptAt, (DateTime?)next)
                .SetProperty(r => r.LockedBy, (string?)null)
                .SetProperty(r => r.LockedUntil, (DateTime?)null)).ConfigureAwait(false);
        logger.LogWarning(exception, "Analytics backfill run {Run} failed (attempt {Attempt}); retrying after {Delay}.", runId, attempts, next - now);
    }

    private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
