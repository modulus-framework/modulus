namespace Modulus.Authorization.Grants;

/// <summary>
/// Captures a startup seeding action for the in-memory grant store, replayed when
/// the store singleton is built. Mirrors the deferred-registration pattern used
/// for module permission declarations (<see cref="IPermissionRegistration"/>), so
/// grants declared across modules all land in the one store.
/// </summary>
public interface IPermissionGrantSeed
{
    void Apply(InMemoryPermissionGrantStore store);
}

internal sealed class PermissionGrantSeed(Action<InMemoryPermissionGrantStore> configure)
    : IPermissionGrantSeed
{
    public void Apply(InMemoryPermissionGrantStore store) => configure(store);
}
