using Modulus.Authorization.Organization;

namespace Modulus.Testing.Authorization;

/// <summary>What an org-placement subject supplies: the hierarchy and placement stores it is built over, and how to reset and fill them.</summary>
public interface IOrgPlacementConformanceSubject
{
    /// <summary>The unit hierarchy of this company.</summary>
    IOrgHierarchy Hierarchy { get; }

    /// <summary>The placements of this company's people.</summary>
    IOrgPlacementStore Placements { get; }

    /// <summary>Empties the hierarchy and placements so the next case starts clean.</summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>Adds a unit under the given parents (none for a root).</summary>
    Task AddUnitAsync(Guid id, Guid[] parents, CancellationToken ct = default);

    /// <summary>Places a user at a unit with a traversal mode.</summary>
    Task PlaceAsync(Guid userId, Guid orgUnitId, OrgScopeMode mode, CancellationToken ct = default);
}

/// <summary>
/// The org-placement truth table (TEST-1): what a placement reaches once the company has more than one level. A placement reaches
/// its own unit, the units beneath it (downward) or above it (upward) depending on its mode, never a sibling subtree, and several
/// placements union their scopes. Checked through the production <see cref="OrgScopeResolver"/>.
/// </summary>
public static class OrgPlacementConformance
{
    // root
    // ├── child
    // │   └── grandchild
    // └── sibling
    // other root (its own tree)
    private static readonly Guid s_root = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid s_child = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid s_grandchild = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid s_sibling = Guid.Parse("a0000000-0000-0000-0000-000000000004");
    private static readonly Guid s_otherRoot = Guid.Parse("a0000000-0000-0000-0000-000000000005");
    private static readonly Guid s_unknown = Guid.Parse("a0000000-0000-0000-0000-000000000006");
    private static readonly Guid s_person = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid s_other = Guid.Parse("b0000000-0000-0000-0000-000000000002");

    private static readonly Dictionary<Guid, string> s_names = new()
    {
        [s_root] = "root",
        [s_child] = "child",
        [s_grandchild] = "grandchild",
        [s_sibling] = "sibling",
        [s_otherRoot] = "other root",
        [s_unknown] = "unknown unit",
    };

    private delegate Task<string?> Check(IOrgPlacementConformanceSubject subject, CancellationToken ct);

    private sealed record Case(string Name, Check Run);

    private static readonly Case[] s_cases =
    [
        new("a unit-only placement reaches just its own unit", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_child, OrgScopeMode.UnitOnly, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_child], outOfScope: [s_root, s_grandchild, s_sibling, s_otherRoot]);
        }),
        new("a downward placement reaches every unit beneath it, several levels down", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_child, OrgScopeMode.UnitAndDescendants, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_child, s_grandchild], outOfScope: [s_root, s_sibling, s_otherRoot]);
        }),
        new("a downward placement does not reach up or across", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_grandchild, OrgScopeMode.UnitAndDescendants, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_grandchild], outOfScope: [s_child, s_root, s_sibling]);
        }),
        new("an upward placement reaches every unit above it, not siblings", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_grandchild, OrgScopeMode.UnitAndAncestors, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_grandchild, s_child, s_root], outOfScope: [s_sibling, s_otherRoot]);
        }),
        new("a placement never reaches another tree", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_root, OrgScopeMode.UnitAndDescendants, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_root, s_child, s_sibling], outOfScope: [s_otherRoot]);
        }),
        new("a placement at a unit the hierarchy does not know still covers that unit", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_unknown, OrgScopeMode.UnitOnly, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_unknown], outOfScope: [s_root, s_child]);
        }),
        new("several placements union their scopes", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_person, s_child, OrgScopeMode.UnitOnly, ct);
            await s.PlaceAsync(s_person, s_sibling, OrgScopeMode.UnitOnly, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [s_child, s_sibling], outOfScope: [s_grandchild, s_root, s_otherRoot]);
        }),
        new("a person with no placement has no organizational reach", async (s, ct) =>
        {
            await BuildAsync(s, ct);
            await s.PlaceAsync(s_other, s_root, OrgScopeMode.UnitAndDescendants, ct);
            return Scope(await ScopeAsync(s, s_person, ct), inScope: [], outOfScope: [s_root, s_child, s_grandchild, s_sibling, s_otherRoot]);
        }),
    ];

    /// <summary>Runs every org-placement case against <paramref name="subject"/>.</summary>
    public static async Task<AuthorizationConformanceReport> RunAsync(IOrgPlacementConformanceSubject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var results = new List<AuthorizationConformanceResult>(s_cases.Length);
        foreach (var conformanceCase in s_cases)
        {
            string? problem;
            try
            {
                problem = await conformanceCase.Run(subject, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                problem = $"threw {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(new AuthorizationConformanceResult(conformanceCase.Name, problem));
        }

        return new AuthorizationConformanceReport(results);
    }

    private static async Task BuildAsync(IOrgPlacementConformanceSubject s, CancellationToken ct)
    {
        await s.ResetAsync(ct);
        await s.AddUnitAsync(s_root, [], ct);
        await s.AddUnitAsync(s_child, [s_root], ct);
        await s.AddUnitAsync(s_grandchild, [s_child], ct);
        await s.AddUnitAsync(s_sibling, [s_root], ct);
        await s.AddUnitAsync(s_otherRoot, [], ct);
    }

    private static Task<IReadOnlySet<Guid>> ScopeAsync(IOrgPlacementConformanceSubject s, Guid user, CancellationToken ct)
        => Task.FromResult<IReadOnlySet<Guid>>(new OrgScopeResolver(s.Hierarchy, s.Placements).Resolve(user).Units);

    private static string? Scope(IReadOnlySet<Guid> units, Guid[] inScope, Guid[] outOfScope)
    {
        var missing = inScope.Where(u => !units.Contains(u)).Select(Name).ToList();
        var leaked = outOfScope.Where(units.Contains).Select(Name).ToList();
        if (missing.Count == 0 && leaked.Count == 0)
            return null;

        var problems = new List<string>();
        if (missing.Count > 0)
            problems.Add($"did not reach {string.Join(", ", missing)}");
        if (leaked.Count > 0)
            problems.Add($"reached {string.Join(", ", leaked)}");
        return string.Join("; ", problems);
    }

    private static string Name(Guid unit) => s_names.TryGetValue(unit, out var name) ? name : unit.ToString();
}
