namespace Modulus.Authorization.Features;

/// <summary>
/// The per-tenant entitlement state: which <b>plans</b> exist (named feature bundles),
/// which plan each tenant is on, and any per-tenant <b>override</b> that turns an
/// individual feature on or off regardless of plan (blueprint §14 — hierarchical defaults
/// <i>plan → tenant override</i>). It holds the raw entitlement data; the effective
/// yes/no for a (tenant, feature) is computed by <see cref="IFeatureEntitlementResolver"/>.
/// Runtime-mutable so a billing event (upgrade, add-on purchase, suspension) can change a
/// tenant's entitlements without a redeploy.
/// </summary>
public interface IFeatureEntitlementStore
{
    /// <summary>The features bundled into <paramref name="plan"/>, or empty if the plan is unknown.</summary>
    IReadOnlySet<string> PlanFeatures(string plan);

    /// <summary>The plan assigned to <paramref name="tenantId"/>, or <see langword="null"/> if none.</summary>
    string? AssignedPlan(Guid tenantId);

    /// <summary>
    /// The per-tenant override for <paramref name="feature"/>: <see langword="true"/>
    /// forces it on, <see langword="false"/> forces it off, <see langword="null"/> means
    /// defer to the plan.
    /// </summary>
    bool? Override(Guid tenantId, string feature);

    /// <summary>
    /// Every override configured for <paramref name="tenantId"/> (feature → on/off), so
    /// the resolver can fold force-on add-ons that lie outside the plan into the effective
    /// set. Empty when the tenant has no overrides.
    /// </summary>
    IReadOnlyDictionary<string, bool> Overrides(Guid tenantId);
}
