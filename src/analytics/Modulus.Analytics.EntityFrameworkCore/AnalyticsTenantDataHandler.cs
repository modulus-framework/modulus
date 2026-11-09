namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Tenancy.Lifecycle;

/// <summary>
/// Puts the analytics copies of a company under offboarding and erasure: its queued deliveries, its backfill and incremental runs, and its
/// rows in every warehouse table. Registered with <c>AddModulusAnalyticsTenantData</c> on the tenancy lifecycle builder.
/// </summary>
/// <remarks>
/// <para>
/// Export writes nothing. The warehouse copies are derived from the source modules' data, and each source module exports its own records, so
/// an archive of analytics copies would only duplicate them.
/// </para>
/// <para>
/// Every registered sink must implement <see cref="IAnalyticsErasableSink"/>. A sink that cannot delete one company's rows (for example a
/// lake of Parquet files, which has no listing) makes count and delete fail with a reason, because a company must not be reported as erased
/// while its copies remain. Such a lake is erased with its own tools (a table format's row-level delete) and is then left out of this handler.
/// </para>
/// <para>The delete is idempotent: every step removes what is left, so a resumed offboarding can call it again.</para>
/// </remarks>
public sealed class AnalyticsTenantDataHandler(
    ModulusAnalyticsDbContext db,
    IAnalyticsDatasetCatalog catalog,
    IEnumerable<IAnalyticsSink> sinks,
    ICurrentTenant currentTenant) : ITenantDataHandler
{
    /// <summary>The queued deliveries (pending, dead-lettered and recently delivered records).</summary>
    public const string QueueDataset = "analytics.queue";

    /// <summary>The backfill and incremental runs of the company.</summary>
    public const string BackfillDataset = "analytics.backfill";

    /// <summary>The company's rows in every warehouse table (every dataset version, in every erasable sink).</summary>
    public const string WarehouseDataset = "analytics.warehouse";

    // Upstream first: the runs that feed the queue, then the queue that feeds the warehouse, then the warehouse. Erasing in the other order
    // lets a run that is still reading queue rows after the queue was emptied, and those rows would then reach the erased warehouse.
    private static readonly IReadOnlyList<TenantDataset> Owned =
    [
        new(BackfillDataset, TenantDataCategory.Owned),
        new(QueueDataset, TenantDataCategory.Owned),
        new(WarehouseDataset, TenantDataCategory.Owned),
    ];

    /// <inheritdoc />
    public string Module => "Analytics";

    /// <inheritdoc />
    public IReadOnlyList<TenantDataset> Datasets => Owned;

    /// <inheritdoc />
    public Task ExportAsync(TenantDataset dataset, ITenantExportWriter writer, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<long> CountAsync(TenantDataset dataset, CancellationToken ct)
    {
        var tenant = TenantId();
        return dataset.Name switch
        {
            QueueDataset => await db.AnalyticsDeliveries.LongCountAsync(d => d.TenantId == tenant, ct),
            BackfillDataset => await db.AnalyticsBackfillRuns.LongCountAsync(r => r.TenantId == tenant, ct),
            WarehouseDataset => await WarehouseAsync(tenant, erase: false, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(dataset), dataset.Name, "Not an analytics dataset of this handler."),
        };
    }

    /// <inheritdoc />
    public async Task<long> DeleteAsync(TenantDataset dataset, CancellationToken ct)
    {
        var tenant = TenantId();
        return dataset.Name switch
        {
            QueueDataset => await db.AnalyticsDeliveries.Where(d => d.TenantId == tenant).ExecuteDeleteAsync(ct),
            BackfillDataset => await db.AnalyticsBackfillRuns.Where(r => r.TenantId == tenant).ExecuteDeleteAsync(ct),
            WarehouseDataset => await WarehouseAsync(tenant, erase: true, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(dataset), dataset.Name, "Not an analytics dataset of this handler."),
        };
    }

    private async Task<long> WarehouseAsync(Guid tenant, bool erase, CancellationToken ct)
    {
        var all = sinks.ToArray();
        var notErasable = all.Where(s => s is not IAnalyticsErasableSink).Select(s => s.Name).ToArray();
        if (notErasable.Length > 0)
        {
            throw new InvalidOperationException(
                $"The analytics sink(s) {string.Join(", ", notErasable)} cannot delete one company's records, so the company cannot be reported as erased. "
                + "Erase those records with the store's own tools and leave the sink out of the tenant-data handler, or use a sink that implements IAnalyticsErasableSink.");
        }

        long total = 0;
        foreach (var sink in all.OfType<IAnalyticsErasableSink>())
        {
            foreach (var dataset in catalog.All)
            {
                total += erase
                    ? await sink.EraseTenantAsync(dataset, tenant, ct)
                    : await sink.CountTenantAsync(dataset, tenant, ct);
            }
        }

        return total;
    }

    private Guid TenantId()
        => currentTenant.TenantId ?? throw new InvalidOperationException("The analytics tenant-data handler runs only inside a company's tenant context.");
}

/// <summary>Registers the analytics tenant-data handler with the tenancy lifecycle.</summary>
public static class AnalyticsTenantDataExtensions
{
    /// <summary>
    /// Adds the analytics datasets (queue, backfill runs and warehouse copies) to company offboarding and erasure, and an offboarding step that
    /// cancels the company's runs and parks its undelivered rows before anything is erased. Needs
    /// <c>AddModulusAnalyticsStore&lt;TContext&gt;()</c> and sinks that implement <see cref="IAnalyticsErasableSink"/>.
    /// </summary>
    /// <param name="builder">The lifecycle builder.</param>
    public static TenantLifecycleBuilder AddModulusAnalyticsTenantData(this TenantLifecycleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        WrapConsent(builder.Services);
        return builder.AddHandler<AnalyticsTenantDataHandler>().AddOffboardingStep<AnalyticsOffboardingStep>();
    }

    // Wraps the consent registered so far (the default allows everything), so call it after AddModulusAnalytics. Calling it twice wraps once.
    private static void WrapConsent(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(OffboardingAwareAnalyticsConsent)))
        {
            return;
        }

        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IAnalyticsConsent));
        if (inner is not null)
        {
            services.Remove(inner);
        }

        services.AddSingleton(sp => new OffboardingAwareAnalyticsConsent(
            inner is null ? new AllowAll() : (IAnalyticsConsent)Resolve(sp, inner), sp.GetRequiredService<IServiceScopeFactory>(), sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddSingleton<IAnalyticsConsent>(sp => sp.GetRequiredService<OffboardingAwareAnalyticsConsent>());
    }

    private static object Resolve(IServiceProvider sp, ServiceDescriptor d)
        => d.ImplementationInstance
            ?? d.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, d.ImplementationType!);

    private sealed class AllowAll : IAnalyticsConsent
    {
        public ValueTask<bool> IsGrantedAsync(Guid tenantId, string dataset, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
