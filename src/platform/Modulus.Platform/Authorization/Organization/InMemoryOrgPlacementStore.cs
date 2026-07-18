namespace Modulus.Authorization.Organization;

using System.Collections.Concurrent;

/// <summary>
/// The default <see cref="IOrgPlacementStore"/>: holds user placements in memory.
/// Seed it at startup via
/// <see cref="Extensions.AuthorizationExtensions.AddOrganization"/>; placements may
/// also be added or removed at runtime (assignments change as people move). Empty
/// by default, so an unseeded store is fail-closed — every user is scoped to
/// nothing.
/// </summary>
public sealed class InMemoryOrgPlacementStore : IOrgPlacementStore
{
    // userId → orgUnitId → placement. A ConcurrentDictionary per user gives
    // lock-free reads during runtime mutation. Re-placing at the same unit
    // overwrites the traversal mode.
    private readonly ConcurrentDictionary<Guid,
        ConcurrentDictionary<Guid, OrgPlacement>> _byUser = new();

    /// <summary>
    /// Places a user at a unit with a traversal mode (defaulting to unit +
    /// descendants, the common enterprise model).
    /// </summary>
    public InMemoryOrgPlacementStore Place(
        Guid userId,
        Guid orgUnitId,
        OrgScopeMode mode = OrgScopeMode.UnitAndDescendants)
    {
        var bucket = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, OrgPlacement>());
        bucket[orgUnitId] = new OrgPlacement(userId, orgUnitId, mode);
        return this;
    }

    /// <summary>Removes a user's placement at a unit (no-op if it was never set).</summary>
    public InMemoryOrgPlacementStore Remove(Guid userId, Guid orgUnitId)
    {
        if (_byUser.TryGetValue(userId, out var bucket))
            bucket.TryRemove(orgUnitId, out _);
        return this;
    }

    public IReadOnlyCollection<OrgPlacement> GetPlacements(Guid userId)
        => _byUser.TryGetValue(userId, out var bucket) ? bucket.Values.ToArray() : [];
}
