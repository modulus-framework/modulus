using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Organization;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IOrgPlacementStore"/>: user↔unit placements as
/// durable rows. A reassignment (<see cref="PlaceAsync"/> on an existing
/// user/unit pair) updates the traversal mode in place. Empty ⇒ no
/// organizational scope (fail-closed).
/// </summary>
public sealed class EfOrgPlacementStore(
    IDbContextFactory<AuthorizationStoreDbContext> factory)
    : IOrgPlacementStore
{
    /// <inheritdoc />
    public IReadOnlyCollection<OrgPlacement> GetPlacements(Guid userId)
    {
        using var db = factory.CreateDbContext();
        return db.OrgPlacements.AsNoTracking()
            .Where(p => p.UserId == userId)
            .AsEnumerable()
            .Select(p => new OrgPlacement(p.UserId, p.OrgUnitId, p.Mode))
            .ToList();
    }

    /// <summary>Places (or re-places, updating the mode) a user at a unit.</summary>
    public async Task PlaceAsync(
        Guid userId, Guid orgUnitId,
        OrgScopeMode mode = OrgScopeMode.UnitAndDescendants,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.OrgPlacements.FindAsync([userId, orgUnitId], ct);
        if (existing is null)
            db.OrgPlacements.Add(new OrgPlacementRow
            {
                UserId = userId,
                OrgUnitId = orgUnitId,
                Mode = mode,
            });
        else
            existing.Mode = mode;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a user's placement at a unit (no-op if absent).</summary>
    public async Task RemoveAsync(Guid userId, Guid orgUnitId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.OrgPlacements
            .Where(p => p.UserId == userId && p.OrgUnitId == orgUnitId)
            .ExecuteDeleteAsync(ct);
    }
}
