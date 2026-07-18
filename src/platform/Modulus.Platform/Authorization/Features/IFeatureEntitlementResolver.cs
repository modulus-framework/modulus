namespace Modulus.Authorization.Features;

/// <summary>
/// Computes a tenant's <b>effective feature set</b> from the entitlement store — the
/// single place that decides "is this feature available to this tenant" for the feature
/// layer of the composition model (blueprint §5.11, §14). Resolution applies the
/// hierarchical defaults <i>plan → tenant override</i>, fail-closed:
/// <list type="bullet">
///   <item>An explicit tenant <see cref="IFeatureEntitlementStore.Override"/> wins — it
///     turns a feature on (a purchased add-on outside the plan) or off (a jurisdictional
///     block) regardless of plan.</item>
///   <item>Otherwise the feature is available iff the tenant's assigned plan bundles it.</item>
///   <item>No plan and no override ⇒ not available.</item>
/// </list>
/// </summary>
public interface IFeatureEntitlementResolver
{
    /// <summary>True when <paramref name="feature"/> is available to <paramref name="tenantId"/>.</summary>
    bool IsEnabled(Guid tenantId, string feature);

    /// <summary>
    /// The complete set of features available to <paramref name="tenantId"/> — the plan
    /// bundle adjusted by the tenant's overrides. Drives feature-aware menu/UI building
    /// (blueprint §5.10) without a probe per feature.
    /// </summary>
    IReadOnlySet<string> EnabledFeatures(Guid tenantId);
}

/// <summary>
/// Default resolver over an <see cref="IFeatureEntitlementStore"/>. Pure and stateless —
/// every decision is recomputed from the store, so runtime entitlement changes take
/// effect immediately.
/// </summary>
public sealed class FeatureEntitlementResolver(IFeatureEntitlementStore store)
    : IFeatureEntitlementResolver
{
    public bool IsEnabled(Guid tenantId, string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        // Tenant override wins over the plan (add-on on, or jurisdictional block off).
        if (store.Override(tenantId, feature) is bool overridden)
            return overridden;

        var plan = store.AssignedPlan(tenantId);
        return plan is not null && store.PlanFeatures(plan).Contains(feature);
    }

    public IReadOnlySet<string> EnabledFeatures(Guid tenantId)
    {
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var plan = store.AssignedPlan(tenantId);
        if (plan is not null)
            enabled.UnionWith(store.PlanFeatures(plan));

        // Apply overrides on top: force-on adds an add-on outside the plan, force-off removes.
        foreach (var (feature, on) in store.Overrides(tenantId))
        {
            if (on)
                enabled.Add(feature);
            else
                enabled.Remove(feature);
        }

        return enabled;
    }
}
