namespace Modulus.Authorization.Features;

/// <summary>
/// Captures a startup seeding action for the in-memory entitlement store, replayed when
/// the store singleton is built. Mirrors the deferred-registration pattern used for
/// permission grants (<see cref="Grants.IPermissionGrantSeed"/>) and the org model, so
/// plans and assignments declared across modules all land in the one store.
/// </summary>
public interface IFeatureEntitlementSeed
{
    void Apply(InMemoryFeatureEntitlementStore store);
}

internal sealed class FeatureEntitlementSeed(Action<InMemoryFeatureEntitlementStore> configure)
    : IFeatureEntitlementSeed
{
    public void Apply(InMemoryFeatureEntitlementStore store) => configure(store);
}
