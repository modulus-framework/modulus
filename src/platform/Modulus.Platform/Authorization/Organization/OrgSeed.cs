namespace Modulus.Authorization.Organization;

/// <summary>
/// Captures a startup seeding action for the in-memory org hierarchy, replayed when
/// the hierarchy singleton is built. Mirrors the deferred-registration pattern used
/// for permission grants (<see cref="Grants.IPermissionGrantSeed"/>), so units
/// declared across modules all land in the one hierarchy.
/// </summary>
public interface IOrgHierarchySeed
{
    void Apply(InMemoryOrgHierarchy hierarchy);
}

/// <summary>
/// Captures a startup seeding action for the in-memory placement store, replayed
/// when the store singleton is built.
/// </summary>
public interface IOrgPlacementSeed
{
    void Apply(InMemoryOrgPlacementStore placements);
}

internal sealed class OrgHierarchySeed(Action<InMemoryOrgHierarchy> configure)
    : IOrgHierarchySeed
{
    public void Apply(InMemoryOrgHierarchy hierarchy) => configure(hierarchy);
}

internal sealed class OrgPlacementSeed(Action<InMemoryOrgPlacementStore> configure)
    : IOrgPlacementSeed
{
    public void Apply(InMemoryOrgPlacementStore placements) => configure(placements);
}
