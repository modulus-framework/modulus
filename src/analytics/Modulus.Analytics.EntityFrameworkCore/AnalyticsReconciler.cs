namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;

/// <summary>
/// Checks the warehouse against the operational store: for each tenant it counts the source's rows and each countable sink's distinct records,
/// and judges the difference (records still queued explain a gap as in flight). It reads only counts, never record values, and each tenant is
/// counted in that tenant's own context.
/// </summary>
public sealed class AnalyticsReconciler(
    ModulusAnalyticsDbContext db,
    IAnalyticsDatasetCatalog catalog,
    IEnumerable<IAnalyticsSource> sources,
    IEnumerable<IAnalyticsSink> sinks,
    IAnalyticsTenantDirectory directory,
    IServiceProvider services,
    ILogger<AnalyticsReconciler> logger)
{
    /// <summary>Reconciles one dataset version.</summary>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version.</param>
    /// <param name="tenantIds">The tenants to check, or <see langword="null"/> for every active tenant.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <exception cref="InvalidOperationException">The dataset is not registered, no source can count it, no sink can be counted, or a tenant id is empty.</exception>
    public async Task<AnalyticsReconciliationReport> ReconcileAsync(
        string dataset, int version, IReadOnlyCollection<Guid>? tenantIds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        if (!catalog.TryGet(dataset, version, out var descriptor))
        {
            throw new InvalidOperationException($"Dataset '{dataset}.v{version}' is not registered.");
        }

        var source = sources.OfType<IAnalyticsCountingSource>().FirstOrDefault(s => s.Dataset == dataset && s.Version == version)
            ?? throw new InvalidOperationException($"No source that can count rows is registered for '{dataset}.v{version}', so it cannot be reconciled.");
        var countable = sinks.OfType<IAnalyticsReconcilableSink>().ToList();
        if (countable.Count == 0)
        {
            throw new InvalidOperationException("No registered sink can be counted, so there is nothing to reconcile against.");
        }

        var tenants = tenantIds is null
            ? await directory.ListTenantIdsAsync(cancellationToken).ConfigureAwait(false)
            : [.. tenantIds.Distinct()];
        if (tenants.Any(t => t == Guid.Empty))
        {
            throw new InvalidOperationException("A reconciliation needs real tenants: the empty tenant id is not allowed.");
        }

        var results = new List<AnalyticsReconciliationResult>();
        var errors = new List<string>();
        foreach (var tenant in tenants)
        {
            long sourceCount;
            try
            {
                sourceCount = await CountSourceAsync(source, tenant, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Tenant {tenant}: could not count the source: {ex.Message}");
                continue;
            }

            var queue = await db.AnalyticsDeliveries.AsNoTracking()
                .Where(d => d.TenantId == tenant && d.Dataset == dataset && d.SchemaVersion == version)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Pending = g.Count(d => d.DeliveredAt == null && d.DeadLetteredAt == null),
                    Dead = g.Count(d => d.DeadLetteredAt != null),
                })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            foreach (var sink in countable)
            {
                try
                {
                    var sinkCount = await sink.CountAsync(descriptor, tenant, cancellationToken).ConfigureAwait(false);
                    var status = AnalyticsReconciliation.Judge(sourceCount, sinkCount, queue?.Pending ?? 0);
                    results.Add(new AnalyticsReconciliationResult(dataset, version, tenant, sink.Name, sourceCount, sinkCount, queue?.Pending ?? 0, queue?.Dead ?? 0, status));
                    if (status is AnalyticsReconciliationStatus.Missing or AnalyticsReconciliationStatus.Extra)
                    {
                        AnalyticsMetrics.ReconciliationMismatches.Add(1, AnalyticsMetrics.Tags(sink.Name, dataset, status.ToString()));
                        logger.LogWarning(
                            "Analytics reconciliation of {Dataset}.v{Version} for tenant {Tenant} on {Sink}: {Status} (source {Source}, sink {SinkCount}).",
                            dataset, version, tenant, sink.Name, status, sourceCount, sinkCount);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"Tenant {tenant}, sink {sink.Name}: could not count the sink: {ex.Message}");
                }
            }
        }

        return new AnalyticsReconciliationReport(results, errors);
    }

    // The tenant is entered in this method, in the frame that runs the count, and left when it returns.
    private async Task<long> CountSourceAsync(IAnalyticsCountingSource source, Guid tenantId, CancellationToken ct)
    {
        TenantInfo tenant;
        try
        {
            tenant = await services.VerifyTenantAsync(tenantId, ct).ConfigureAwait(false);
        }
        catch (TenantContextRejectedException)
        {
            throw new InvalidOperationException("the tenant is unknown or inactive");
        }

        using var scope = services.EnterTenant(tenant);
        return await source.CountAsync(tenantId, ct).ConfigureAwait(false);
    }
}
