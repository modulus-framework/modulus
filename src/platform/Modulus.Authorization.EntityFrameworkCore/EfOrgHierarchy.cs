using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Organization;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IOrgHierarchy"/>. The unit/edge tables are the
/// durable source of truth; closure queries are served from an in-memory
/// snapshot (an <see cref="InMemoryOrgHierarchy"/> rebuilt from the tables) so
/// per-request descendant/ancestor lookups never hit the database. The snapshot
/// is invalidated by local mutations (<see cref="AddUnitAsync"/> /
/// <see cref="MoveUnitAsync"/>) and expires after <see cref="CacheDuration"/>
/// so other application instances converge on structural changes without a
/// distributed signal — org structure changes are rare and a short convergence
/// window is acceptable; call <see cref="Invalidate"/> to force an immediate
/// reload.
/// </summary>
public sealed class EfOrgHierarchy(
    IDbContextFactory<AuthorizationStoreDbContext> factory,
    TimeProvider time)
    : IOrgHierarchy
{
    private readonly object _gate = new();
    private InMemoryOrgHierarchy? _snapshot;
    private DateTimeOffset _loadedAt;

    /// <summary>How long a loaded snapshot serves before it is refreshed.</summary>
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public bool Contains(Guid orgUnitId) => Snapshot().Contains(orgUnitId);

    /// <inheritdoc />
    public IReadOnlySet<Guid> Descendants(Guid orgUnitId) => Snapshot().Descendants(orgUnitId);

    /// <inheritdoc />
    public IReadOnlySet<Guid> Ancestors(Guid orgUnitId) => Snapshot().Ancestors(orgUnitId);

    /// <summary>
    /// Adds a unit and its parent edges (accumulating — additional parents extend
    /// a matrixed DAG). A root unit passes no parents. Missing parents are created.
    /// </summary>
    public async Task AddUnitAsync(Guid id, Guid[] parents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parents);
        if (Array.Exists(parents, p => p == id))
            throw new ArgumentException("A unit cannot be its own parent.", nameof(parents));

        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureUnitAsync(db, id, ct);
        foreach (var parent in parents)
        {
            await EnsureUnitAsync(db, parent, ct);
            if (await db.OrgUnitParents.FindAsync([id, parent], ct) is null)
                db.OrgUnitParents.Add(new OrgUnitParentRow { ChildId = id, ParentId = parent });
        }

        await db.SaveChangesAsync(ct);
        Invalidate();
    }

    /// <summary>
    /// Reparents a unit — the reorg primitive: its subtree's effective scope moves
    /// with it because placements reference the stable unit id, not a path.
    /// Replaces all of the unit's existing parents.
    /// </summary>
    public async Task MoveUnitAsync(Guid id, Guid[] newParents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newParents);
        if (Array.Exists(newParents, p => p == id))
            throw new ArgumentException("A unit cannot be its own parent.", nameof(newParents));

        await using var db = await factory.CreateDbContextAsync(ct);
        await EnsureUnitAsync(db, id, ct);
        await db.OrgUnitParents.Where(e => e.ChildId == id).ExecuteDeleteAsync(ct);
        foreach (var parent in newParents)
        {
            await EnsureUnitAsync(db, parent, ct);
            db.OrgUnitParents.Add(new OrgUnitParentRow { ChildId = id, ParentId = parent });
        }

        await db.SaveChangesAsync(ct);
        Invalidate();
    }

    /// <summary>Discards the cached snapshot; the next query reloads from the database.</summary>
    public void Invalidate()
    {
        lock (_gate)
            _snapshot = null;
    }

    private InMemoryOrgHierarchy Snapshot()
    {
        // Fast path: a fresh snapshot serves without touching the database or
        // contending on the gate.
        lock (_gate)
        {
            if (_snapshot is not null && time.GetUtcNow() - _loadedAt < CacheDuration)
                return _snapshot;
        }

        // Refresh OUTSIDE the gate: concurrent callers may all reload (the
        // rebuild is idempotent), but no thread ever holds the lock across
        // DbContext creation and table reads.
        using var db = factory.CreateDbContext();
        var edgesByChild = db.OrgUnitParents.AsNoTracking()
            .AsEnumerable()
            .GroupBy(e => e.ChildId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ParentId).ToArray());

        var snapshot = new InMemoryOrgHierarchy();
        foreach (var unitId in db.OrgUnits.AsNoTracking().Select(u => u.Id))
            snapshot.AddUnit(unitId,
                edgesByChild.TryGetValue(unitId, out var parents) ? parents : []);

        lock (_gate)
        {
            // Another caller may have published a fresher snapshot while this
            // one was loading; keep whichever load is newest.
            var now = time.GetUtcNow();
            if (_snapshot is null || now - _loadedAt >= CacheDuration)
            {
                _snapshot = snapshot;
                _loadedAt = now;
            }

            return _snapshot;
        }
    }

    private static async Task EnsureUnitAsync(
        AuthorizationStoreDbContext db, Guid id, CancellationToken ct)
    {
        if (await db.OrgUnits.FindAsync([id], ct) is null)
            db.OrgUnits.Add(new OrgUnitRow { Id = id });
    }
}
