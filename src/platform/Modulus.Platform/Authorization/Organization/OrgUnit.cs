namespace Modulus.Authorization.Organization;

/// <summary>
/// A node in the organizational hierarchy — a company, business unit, branch,
/// department, team, cost center, region, etc. Identified by an <see cref="Id"/>
/// that is <b>stable across reorganizations</b>: placements and grants reference
/// this id, never a path, so moving/merging/splitting units relocates effective
/// access without a data-repair emergency (blueprint §5.4, §8).
/// A unit may have more than one parent — the hierarchy is a DAG, supporting
/// matrixed (functional + geographic) organizations. A root unit has no parents.
/// </summary>
public sealed record OrgUnit(Guid Id, IReadOnlyCollection<Guid> ParentIds)
{
    /// <summary>
    /// Creates a unit with a single parent, or a root unit when
    /// <paramref name="parentId"/> is <c>null</c>.
    /// </summary>
    public static OrgUnit Create(Guid id, Guid? parentId = null)
        => new(id, parentId is { } p ? [p] : []);
}

/// <summary>
/// How far a placement's authority reaches through the hierarchy from its unit.
/// This is a property of the <see cref="OrgPlacement"/> (the grant), not a global
/// switch (blueprint §8) — the same role can be unit-only for one user and
/// unit-and-descendants for another.
/// </summary>
public enum OrgScopeMode
{
    /// <summary>Act strictly within the assigned unit.</summary>
    UnitOnly = 0,

    /// <summary>
    /// The unit and every unit beneath it — downward inheritance, the most common
    /// enterprise model (a regional manager over all their branches).
    /// </summary>
    UnitAndDescendants = 1,

    /// <summary>
    /// The unit and every unit above it — upward visibility, e.g. roll-up reporting.
    /// </summary>
    UnitAndAncestors = 2,
}

/// <summary>
/// A user↔unit assignment: the user is placed at <see cref="OrgUnitId"/> and acts
/// there with the given traversal <see cref="Mode"/>. A user may hold several
/// placements; their effective scope is the union of all of them (blueprint §8).
/// </summary>
public sealed record OrgPlacement(Guid UserId, Guid OrgUnitId, OrgScopeMode Mode);

/// <summary>
/// The set of organizational units a principal is entitled to act within, after
/// each of their placements is expanded by its traversal mode over the hierarchy
/// closure and the results are unioned. This is the input the data-scope layer
/// composes into a query predicate (<c>record.OrgUnitId ∈ scope</c>). An empty
/// scope is fail-closed — the principal is scoped to nothing.
/// </summary>
public sealed class OrgScope
{
    /// <summary>An empty scope — no organizational reach (the fail-closed default).</summary>
    public static readonly OrgScope None = new(new HashSet<Guid>());

    /// <summary>Creates a scope over the given units.</summary>
    /// <param name="units">The units in scope, already closed over traversal modes.</param>
    public OrgScope(IReadOnlySet<Guid> units)
        => Units = units ?? throw new ArgumentNullException(nameof(units));

    /// <summary>The org units in scope (already closed over the traversal modes).</summary>
    public IReadOnlySet<Guid> Units { get; }

    /// <summary>True when the principal is scoped to no unit at all.</summary>
    public bool IsEmpty => Units.Count == 0;

    /// <summary>Whether a given unit falls within this scope.</summary>
    public bool Includes(Guid orgUnitId) => Units.Contains(orgUnitId);
}
