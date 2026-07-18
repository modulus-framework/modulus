namespace Modulus.Authorization.Organization;

/// <summary>
/// The organizational hierarchy as first-class data with a cached transitive
/// closure for efficient descendant/ancestor queries (blueprint §8). DAG-aware:
/// a unit may have several parents, so both <see cref="Descendants"/> and
/// <see cref="Ancestors"/> return the full reachable set (matrix organizations).
/// Unknown units resolve to an empty set (fail-closed).
/// </summary>
public interface IOrgHierarchy
{
    /// <summary>Whether the hierarchy contains the given unit.</summary>
    bool Contains(Guid orgUnitId);

    /// <summary>
    /// Every unit reachable downward from <paramref name="orgUnitId"/> (its
    /// children, their children, …), excluding the unit itself.
    /// </summary>
    IReadOnlySet<Guid> Descendants(Guid orgUnitId);

    /// <summary>
    /// Every unit reachable upward from <paramref name="orgUnitId"/> (its parents,
    /// their parents, …), excluding the unit itself.
    /// </summary>
    IReadOnlySet<Guid> Ancestors(Guid orgUnitId);
}
