namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on any entity to get automatic organizational row-scoping. EF Core
/// applies a global query filter alongside tenant isolation and soft-delete:
/// a row is visible only when its <see cref="OrgUnitId"/> falls within the current
/// principal's resolved organizational scope (their placements expanded by
/// traversal mode — see <c>Modulus.Authorization.Organization</c>), or when the
/// principal is unrestricted (<see cref="Modulus.Core.Abstractions.ICurrentDataScope"/>).
/// <para>
/// The <see cref="OrgUnitId"/> references a <b>stable</b> org-unit identity, never
/// a path, so reorganizations relocate effective access without touching row data
/// (blueprint §5.4, §8). List and single-item reads apply the <i>same</i> predicate,
/// because both flow through the global filter.
/// </para>
/// </summary>
public interface IHasOrgUnit
{
    /// <summary>The organizational unit that owns this row.</summary>
    Guid OrgUnitId { get; }
}
