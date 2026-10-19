using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Dimensions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Expands assigned dimension values to the values placed beneath them, reading the placements in <c>ModulusDimensionValues</c> one
/// level at a time. The tenant filter of the table keeps the walk inside the current company. The walk stops at
/// <see cref="MaxValues"/> values; the rest are not granted, so a truncated expansion narrows access rather than widening it.
/// </summary>
public sealed class EfDimensionHierarchy(IDbContextFactory<AuthorizationStoreDbContextBase> factory) : IDimensionHierarchy
{
    /// <summary>The most values one expansion collects.</summary>
    public const int MaxValues = 10_000;

    /// <inheritdoc />
    public IReadOnlySet<Guid> Expand(IReadOnlySet<Guid> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
            return targets;

        var result = new HashSet<Guid>(targets);
        var frontier = targets.Select(id => (Guid?)id).ToList();

        using var db = factory.CreateDbContext();
        while (frontier.Count > 0 && result.Count < MaxValues)
        {
            var children = db.DimensionValues.AsNoTracking()
                .Where(v => v.ParentId != null && frontier.Contains(v.ParentId))
                .Select(v => v.Id)
                .ToList();

            frontier = [];
            foreach (var child in children)
            {
                if (result.Add(child))
                    frontier.Add(child);
            }
        }

        return result;
    }
}
