using Microsoft.EntityFrameworkCore;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Composite roles: "Line Supervisor includes Operator" means a holder of the first role also holds everything granted to the
/// second (permissions, scoped grants and approval limits), through any number of levels. Kept per company; the grant and
/// approval-limit stores expand a principal's roles with it, so a change shows on the next request.
/// </summary>
public sealed class EfRoleInclusionStore(IDbContextFactory<AuthorizationStoreDbContext> factory)
{
    /// <summary>The deepest chain of inclusions allowed.</summary>
    public const int MaxDepth = 10;

    /// <summary>One inclusion as stored.</summary>
    /// <param name="Role">The including role.</param>
    /// <param name="Includes">The included role.</param>
    /// <param name="CreatedBy">The administrator who made it, or null.</param>
    /// <param name="CreatedAt">When it was made.</param>
    public sealed record Stored(string Role, string Includes, Guid? CreatedBy, DateTimeOffset CreatedAt);

    /// <summary>Every inclusion of the current company.</summary>
    public async Task<IReadOnlyCollection<Stored>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.RoleInclusions.AsNoTracking().ToListAsync(ct);
        return [.. rows.OrderBy(r => r.NormalizedRole, StringComparer.Ordinal).ThenBy(r => r.NormalizedIncludes, StringComparer.Ordinal)
            .Select(r => new Stored(r.Role, r.Includes, r.CreatedBy, r.CreatedAt))];
    }

    /// <summary>
    /// Makes <paramref name="role"/> include <paramref name="includes"/>. Returns false when it already does; throws
    /// <see cref="InvalidOperationException"/> when it would make a role include itself (directly or around a cycle) or nest too deep.
    /// </summary>
    public async Task<bool> AddAsync(string role, string includes, Guid? createdBy, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(includes);
        var normalizedRole = Normalize(role);
        var normalizedIncludes = Normalize(includes);
        if (normalizedRole == normalizedIncludes)
            throw new InvalidOperationException("A role cannot include itself.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var edges = await db.RoleInclusions.AsNoTracking().ToListAsync(ct);
        if (edges.Any(e => e.NormalizedRole == normalizedRole && e.NormalizedIncludes == normalizedIncludes))
            return false;

        // The new edge role -> includes closes a cycle when includes already reaches role.
        var map = ToMap(edges);
        if (Reach(map, normalizedIncludes).Contains(normalizedRole))
            throw new InvalidOperationException($"'{includes.Trim()}' already includes '{role.Trim()}', so this would be a cycle.");

        // The longest chain through the new edge: what sits above role, this edge, what sits below includes.
        var above = UpwardDepth(map, normalizedRole);
        var below = Depth(map, normalizedIncludes);
        if (above + 1 + below > MaxDepth)
            throw new InvalidOperationException($"Roles nest at most {MaxDepth} levels deep.");

        db.RoleInclusions.Add(new RoleInclusionRow
        {
            Role = role.Trim(),
            NormalizedRole = normalizedRole,
            Includes = includes.Trim(),
            NormalizedIncludes = normalizedIncludes,
            CreatedBy = createdBy,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Removes an inclusion; false when it did not exist.</summary>
    public async Task<bool> RemoveAsync(string role, string includes, CancellationToken ct = default)
    {
        var normalizedRole = Normalize(role);
        var normalizedIncludes = Normalize(includes);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.RoleInclusions
            .Where(r => r.NormalizedRole == normalizedRole && r.NormalizedIncludes == normalizedIncludes)
            .ExecuteDeleteAsync(ct) > 0;
    }

    /// <summary>The roles themselves plus every role they include, through any levels.</summary>
    public async Task<IReadOnlyCollection<string>> ExpandAsync(IEnumerable<string> roles, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return Expand(await db.RoleInclusions.AsNoTracking().ToListAsync(ct), roles);
    }

    /// <summary>Synchronous expansion over an open context, for the grant and approval stores (their contracts are synchronous).</summary>
    internal static IReadOnlyCollection<string> Expand(AuthorizationStoreDbContext db, IEnumerable<string> roles)
        => Expand(db.RoleInclusions.AsNoTracking().ToList(), roles);

    private static List<string> Expand(List<RoleInclusionRow> edges, IEnumerable<string> roles)
    {
        var given = roles.ToList();
        if (edges.Count == 0)
            return given;

        var display = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in edges)
        {
            display.TryAdd(e.NormalizedRole, e.Role);
            display.TryAdd(e.NormalizedIncludes, e.Includes);
        }

        var map = ToMap(edges);
        var result = new List<string>(given);
        var seen = new HashSet<string>(given.Select(Normalize), StringComparer.Ordinal);
        foreach (var role in given)
        {
            foreach (var included in Reach(map, Normalize(role)))
            {
                if (seen.Add(included))
                    result.Add(display.GetValueOrDefault(included, included));
            }
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> ToMap(IEnumerable<RoleInclusionRow> edges)
        => edges.GroupBy(e => e.NormalizedRole)
            .ToDictionary(g => g.Key, g => g.Select(e => e.NormalizedIncludes).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

    private static HashSet<string> Reach(Dictionary<string, HashSet<string>> map, string from)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            if (!map.TryGetValue(stack.Pop(), out var next))
                continue;
            foreach (var n in next)
            {
                if (seen.Add(n))
                    stack.Push(n);
            }
        }

        return seen;
    }

    // Edges below a role (the graph is acyclic: cycles are refused on write).
    private static int Depth(Dictionary<string, HashSet<string>> map, string role)
        => map.TryGetValue(role, out var next) && next.Count > 0 ? 1 + next.Max(n => Depth(map, n)) : 0;

    // Edges above a role.
    private static int UpwardDepth(Dictionary<string, HashSet<string>> map, string role)
    {
        var parents = map.Where(kv => kv.Value.Contains(role)).Select(kv => kv.Key).ToList();
        return parents.Count == 0 ? 0 : 1 + parents.Max(p => UpwardDepth(map, p));
    }

    private static string Normalize(string role) => role.Trim().ToUpperInvariant();
}
