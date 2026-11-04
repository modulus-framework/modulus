namespace Modulus.Analytics;

/// <summary>
/// A sink that can count and delete one company's records of a dataset version. Offboarding and erasure use it, so a company's copies
/// in the warehouse go with the company. A sink that cannot delete by tenant (for example a lake of files with no listing) does not
/// implement this, and the tenant-data handler then refuses to report the company as erased.
/// </summary>
public interface IAnalyticsErasableSink : IAnalyticsReconcilableSink
{
    /// <summary>
    /// Counts the distinct records the destination holds for <paramref name="tenantId"/> and the dataset version. A dataset version the
    /// destination has never created has no records, so the count is 0 rather than an error.
    /// </summary>
    /// <param name="dataset">The dataset version.</param>
    /// <param name="tenantId">The company.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <exception cref="AnalyticsSinkException">The count failed.</exception>
    Task<long> CountTenantAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the company's records of the dataset version and returns how many distinct records were removed. Idempotent: a second call
    /// removes nothing and returns 0. A dataset version the destination has never created has nothing to remove.
    /// </summary>
    /// <param name="dataset">The dataset version.</param>
    /// <param name="tenantId">The company.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <exception cref="AnalyticsSinkException">The delete failed.</exception>
    Task<long> EraseTenantAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken);
}
