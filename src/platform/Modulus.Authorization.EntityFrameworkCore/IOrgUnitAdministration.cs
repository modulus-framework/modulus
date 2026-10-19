namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>Writes the organizational hierarchy (add and re-parent units). Implemented by the EF hierarchy store.</summary>
public interface IOrgUnitAdministration
{
    /// <summary>Adds a unit under the given parents (none for a root).</summary>
    Task AddUnitAsync(Guid id, Guid[] parents, CancellationToken ct = default);

    /// <summary>Replaces the parents of a unit.</summary>
    Task MoveUnitAsync(Guid id, Guid[] newParents, CancellationToken ct = default);
}
