namespace Modulus.Analytics;

/// <summary>A sink that can profile one company's rows of a dataset version for the data-quality checks.</summary>
public interface IAnalyticsProfilableSink : IAnalyticsSink
{
    /// <summary>
    /// Counts the company's distinct records, the records timestamped after <paramref name="notAfter"/>, and the nulls of each nullable field that declares a
    /// <see cref="AnalyticsFieldDescriptor.MaxNullRate"/>. A dataset version never created has no rows, so the profile is empty rather than an error.
    /// </summary>
    /// <param name="dataset">The dataset version.</param>
    /// <param name="tenantId">The company.</param>
    /// <param name="notAfter">The latest occurrence time that is not a future record (the check time plus the clock skew).</param>
    /// <param name="cancellationToken">Cancels the profile.</param>
    /// <exception cref="AnalyticsSinkException">The profile failed.</exception>
    Task<AnalyticsWarehouseProfile> ProfileAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, DateTimeOffset notAfter, CancellationToken cancellationToken);
}
