namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>What <see cref="AnalyticsBackfillManager.StartAsync"/> did.</summary>
/// <param name="Created">New runs.</param>
/// <param name="Restarted">Finished or failed runs that were reset to start again.</param>
/// <param name="Skipped">Tenants whose run already exists and was left alone (still running, or finished and no restart was asked for).</param>
/// <param name="NotEntitled">Tenants whose plan does not include the dataset's required feature. No run was created for them.</param>
public sealed record AnalyticsBackfillStartResult(int Created, int Restarted, int Skipped, int NotEntitled = 0);

/// <summary>Starts and lists backfill runs. The background worker does the reading.</summary>
public sealed class AnalyticsBackfillManager(
    ModulusAnalyticsDbContext db,
    IAnalyticsDatasetCatalog catalog,
    IEnumerable<IAnalyticsSource> sources,
    IAnalyticsTenantDirectory directory,
    IAnalyticsEntitlement entitlement,
    TimeProvider clock)
{
    /// <summary>
    /// Creates one run per tenant for a dataset version. A tenant that already has a run is skipped, unless <paramref name="restart"/> is
    /// set and that run has finished, failed or been cancelled; then it starts again from the beginning (a row whose revision is already queued is skipped, a changed row is queued again).
    /// </summary>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version.</param>
    /// <param name="tenantIds">The tenants to cover, or <see langword="null"/> for every active tenant.</param>
    /// <param name="restart">Whether to restart finished or failed runs.</param>
    /// <param name="requestedBy">Who asked, for the record.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="kind">A one-off backfill (the default), or an incremental run that keeps the dataset up to date. A tenant can have one of each.</param>
    /// <exception cref="InvalidOperationException">The dataset version or its source is not registered (an incremental run needs an incremental source), or a tenant id is empty.</exception>
    public async Task<AnalyticsBackfillStartResult> StartAsync(
        string dataset,
        int version,
        IReadOnlyCollection<Guid>? tenantIds,
        bool restart,
        string? requestedBy,
        CancellationToken cancellationToken,
        AnalyticsRunKind kind = AnalyticsRunKind.Backfill)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);

        if (!catalog.TryGet(dataset, version, out var descriptor))
        {
            throw new InvalidOperationException($"Dataset '{dataset}.v{version}' is not registered.");
        }

        if (!sources.Any(s => s.Dataset == dataset && s.Version == version))
        {
            throw new InvalidOperationException($"No analytics source is registered for '{dataset}.v{version}', so it cannot be backfilled.");
        }

        if (kind == AnalyticsRunKind.Incremental && !sources.OfType<IAnalyticsIncrementalSource>().Any(s => s.Dataset == dataset && s.Version == version))
        {
            throw new InvalidOperationException($"The source for '{dataset}.v{version}' cannot report changes, so it cannot be kept up to date incrementally.");
        }

        var tenants = tenantIds is null
            ? await directory.ListTenantIdsAsync(cancellationToken).ConfigureAwait(false)
            : [.. tenantIds.Distinct()];
        if (tenants.Any(t => t == Guid.Empty))
        {
            throw new InvalidOperationException("A backfill needs real tenants: the empty tenant id is not allowed.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.AnalyticsBackfillRuns
            .Where(r => r.Dataset == dataset && r.SchemaVersion == version && r.Kind == kind && tenants.Contains(r.TenantId))
            .ToDictionaryAsync(r => r.TenantId, cancellationToken).ConfigureAwait(false);

        int created = 0, restarted = 0, skipped = 0, notEntitled = 0;
        foreach (var tenant in tenants)
        {
            if (!entitlement.IsEntitled(tenant, descriptor))
            {
                notEntitled++;
            }
            else if (!existing.TryGetValue(tenant, out var run))
            {
                db.AnalyticsBackfillRuns.Add(new AnalyticsBackfillRun
                {
                    Dataset = dataset,
                    SchemaVersion = version,
                    TenantId = tenant,
                    Kind = kind,
                    Status = AnalyticsBackfillStatus.Pending,
                    CreatedAt = now,
                    UpdatedAt = now,
                    RequestedBy = requestedBy,
                });
                created++;
            }
            else if (restart && run.Status is AnalyticsBackfillStatus.Completed or AnalyticsBackfillStatus.Failed or AnalyticsBackfillStatus.Cancelled)
            {
                run.Status = AnalyticsBackfillStatus.Pending;
                run.Cursor = null;
                run.RowsQueued = 0;
                run.AttemptCount = 0;
                run.CompletedAt = null;
                run.NextAttemptAt = null;
                run.LockedBy = null;
                run.LockedUntil = null;
                run.LastError = null;
                run.RequestedBy = requestedBy;
                run.UpdatedAt = now;
                restarted++;
            }
            else
            {
                skipped++;
            }
        }

        if (created + restarted > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AnalyticsBackfillStartResult(created, restarted, skipped, notEntitled);
    }

    /// <summary>Stops a run that is waiting or running. Nothing more is read for it; restart it to begin again.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when the run was stopped; <see langword="false"/> when it does not exist or had already finished.</returns>
    public async Task<bool> CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stopped = await db.AnalyticsBackfillRuns
            .Where(r => r.Id == runId && (r.Status == AnalyticsBackfillStatus.Pending || r.Status == AnalyticsBackfillStatus.Running))
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, AnalyticsBackfillStatus.Cancelled)
                      .SetProperty(r => r.UpdatedAt, now)
                      .SetProperty(r => r.LockedBy, (string?)null)
                      .SetProperty(r => r.LockedUntil, (DateTime?)null),
                cancellationToken).ConfigureAwait(false);
        return stopped > 0;
    }

    /// <summary>Finds a run by id, or <see langword="null"/>.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<AnalyticsBackfillRun?> GetAsync(Guid runId, CancellationToken cancellationToken)
        => db.AnalyticsBackfillRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    /// <summary>Lists runs, newest first.</summary>
    /// <param name="dataset">Only this dataset, or <see langword="null"/> for all.</param>
    /// <param name="status">Only this state, or <see langword="null"/> for all.</param>
    /// <param name="take">The most runs to return. Defaults to 100.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="datasets">Only runs of these datasets, or <see langword="null"/> for all. Used to show a dataset-scoped operator only their own runs.</param>
    public async Task<IReadOnlyList<AnalyticsBackfillRun>> ListAsync(
        string? dataset = null,
        AnalyticsBackfillStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? datasets = null)
    {
        var query = db.AnalyticsBackfillRuns.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(dataset))
        {
            query = query.Where(r => r.Dataset == dataset);
        }

        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        if (datasets is not null)
        {
            query = query.Where(r => datasets.Contains(r.Dataset));
        }

        return await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Take(Math.Clamp(take, 1, 1000)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
