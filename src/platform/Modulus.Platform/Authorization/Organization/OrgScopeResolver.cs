namespace Modulus.Authorization.Organization;

/// <summary>
/// The default <see cref="IOrgScopeResolver"/>. Resolves against an
/// <see cref="IOrgPlacementStore"/> and the <see cref="IOrgHierarchy"/> closure.
/// Stateless and thread-safe — registered as a singleton.
/// </summary>
public sealed class OrgScopeResolver(
    IOrgHierarchy hierarchy,
    IOrgPlacementStore placements) : IOrgScopeResolver
{
    public OrgScope Resolve(Guid? userId)
    {
        if (userId is not { } id)
            return OrgScope.None; // anonymous → no scope (fail-closed)

        var held = placements.GetPlacements(id);
        if (held.Count == 0)
            return OrgScope.None; // no placement → no scope (fail-closed)

        var units = new HashSet<Guid>();
        foreach (var placement in held)
        {
            // The placement's own unit is always in scope, even if it is not (yet)
            // in the hierarchy — the placement is explicit authorization data.
            units.Add(placement.OrgUnitId);

            switch (placement.Mode)
            {
                case OrgScopeMode.UnitAndDescendants:
                    units.UnionWith(hierarchy.Descendants(placement.OrgUnitId));
                    break;
                case OrgScopeMode.UnitAndAncestors:
                    units.UnionWith(hierarchy.Ancestors(placement.OrgUnitId));
                    break;
                case OrgScopeMode.UnitOnly:
                default:
                    break;
            }
        }

        return new OrgScope(units);
    }
}
