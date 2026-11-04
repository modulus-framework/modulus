namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Features;

/// <summary>
/// Plan gating through the framework's feature entitlements: a dataset that names a required feature is open only to tenants whose plan (or override)
/// includes it. A dataset with no required feature, or an app with no entitlement store, is open to everyone.
/// </summary>
internal sealed class FeatureAnalyticsEntitlement(IServiceProvider services) : IAnalyticsEntitlement
{
    public bool IsEntitled(Guid tenantId, AnalyticsDatasetDescriptor dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.RequiredFeature is not { } feature)
        {
            return true;
        }

        return services.GetService<IFeatureEntitlementResolver>() is not { } resolver || resolver.IsEnabled(tenantId, feature);
    }
}
