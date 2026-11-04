namespace Modulus.Analytics;

/// <summary>
/// Decides whether a tenant may export data to a dataset: plan gating. A dataset names the feature it needs
/// (<see cref="AnalyticsDatasetDescriptor.RequiredFeature"/>); the implementation says whether the tenant's plan has it.
/// </summary>
/// <remarks>
/// The default allows everything. <c>AddModulusAnalyticsStore</c> replaces it with one that asks the framework's feature entitlements.
/// Entitlement is checked when records are captured and when a backfill or incremental run is started or read; records already queued are still delivered.
/// </remarks>
public interface IAnalyticsEntitlement
{
    /// <summary>Whether <paramref name="tenantId"/> may export to <paramref name="dataset"/>.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="dataset">The dataset.</param>
    bool IsEntitled(Guid tenantId, AnalyticsDatasetDescriptor dataset);
}

internal sealed class AllowAllAnalyticsEntitlement : IAnalyticsEntitlement
{
    public bool IsEntitled(Guid tenantId, AnalyticsDatasetDescriptor dataset) => true;
}
