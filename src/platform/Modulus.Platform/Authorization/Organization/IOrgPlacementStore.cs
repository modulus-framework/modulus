namespace Modulus.Authorization.Organization;

/// <summary>
/// Stores users' organizational placements (blueprint §8). Sits behind the
/// synchronous <see cref="IOrgScopeResolver"/>, so lookups are synchronous.
/// A user with no placements resolves to no organizational scope (fail-closed).
/// </summary>
public interface IOrgPlacementStore
{
    /// <summary>Every placement held by the given user (empty if none).</summary>
    IReadOnlyCollection<OrgPlacement> GetPlacements(Guid userId);
}
