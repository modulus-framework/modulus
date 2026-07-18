namespace Modulus.Authorization.Governance;

/// <summary>
/// Captures a startup seeding action for the in-memory delegation store, replayed when
/// the store singleton is built. Mirrors the deferred-registration pattern used for
/// permission grants (<see cref="Grants.IPermissionGrantSeed"/>) and feature
/// entitlements, so baseline delegations declared across modules all land in the one
/// store.
/// </summary>
public interface IDelegationSeed
{
    void Apply(InMemoryDelegationStore store);
}

internal sealed class DelegationSeed(Action<InMemoryDelegationStore> configure)
    : IDelegationSeed
{
    public void Apply(InMemoryDelegationStore store) => configure(store);
}
