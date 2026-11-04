namespace Modulus.Analytics.EntityFrameworkCore;

using Modulus.Core.Abstractions;

/// <summary>
/// Runs the data-quality rules over the warehouse: for each company and each profilable sink it profiles the company's rows and evaluates the dataset's
/// rules against the profile. It reads counts only, never record values, and needs no source: the warehouse alone is judged.
/// </summary>
public sealed class AnalyticsQualityChecker(
    IAnalyticsDatasetCatalog catalog,
    IEnumerable<IAnalyticsSink> sinks,
    IAnalyticsTenantDirectory directory,
    IAnalyticsEntitlement entitlement,
    TimeProvider clock)
{
    /// <summary>Checks one dataset version.</summary>
    /// <param name="dataset">The dataset name.</param>
    /// <param name="version">The dataset version.</param>
    /// <param name="tenantIds">The companies to check, or <see langword="null"/> for every active company.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="InvalidOperationException">The dataset is not registered, no sink can be profiled, or a company id is empty.</exception>
    public async Task<AnalyticsQualityReport> CheckAsync(string dataset, int version, IReadOnlyCollection<Guid>? tenantIds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataset);
        if (!catalog.TryGet(dataset, version, out var descriptor))
        {
            throw new InvalidOperationException($"Dataset '{dataset}.v{version}' is not registered.");
        }

        var profilable = sinks.OfType<IAnalyticsProfilableSink>().ToList();
        if (profilable.Count == 0)
        {
            throw new InvalidOperationException("No registered sink can be profiled, so there is nothing to check.");
        }

        var tenants = tenantIds is null
            ? await directory.ListTenantIdsAsync(cancellationToken).ConfigureAwait(false)
            : [.. tenantIds.Distinct()];
        if (tenants.Any(t => t == Guid.Empty))
        {
            throw new InvalidOperationException("A quality check needs real companies: the empty tenant id is not allowed.");
        }

        var notAfter = clock.GetUtcNow() + AnalyticsQualityChecks.ClockSkew;
        var results = new List<AnalyticsQualityResult>();
        var errors = new List<string>();
        foreach (var tenant in tenants)
        {
            if (!entitlement.IsEntitled(tenant, descriptor))
            {
                errors.Add($"Tenant {tenant}: not entitled to dataset '{dataset}' (it needs the '{descriptor.RequiredFeature}' feature), so it was not checked.");
                continue;
            }

            foreach (var sink in profilable)
            {
                try
                {
                    var profile = await sink.ProfileAsync(descriptor, tenant, notAfter, cancellationToken).ConfigureAwait(false);
                    var findings = AnalyticsQuality.Evaluate(descriptor, profile);
                    foreach (var finding in findings)
                    {
                        AnalyticsMetrics.QualityFindings.Add(1, AnalyticsMetrics.Tags(sink.Name, dataset, finding.Check));
                    }

                    results.Add(new AnalyticsQualityResult(dataset, version, tenant, sink.Name, profile.RowCount, findings));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"Tenant {tenant}, sink {sink.Name}: could not profile the sink: {ex.Message}");
                }
            }
        }

        return new AnalyticsQualityReport(results, errors);
    }
}
