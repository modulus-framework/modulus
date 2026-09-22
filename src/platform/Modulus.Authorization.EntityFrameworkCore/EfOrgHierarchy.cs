using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IOrgHierarchy"/>. The unit/edge tables are the
/// durable source of truth; closure queries are served from an in-memory
/// snapshot (an <see cref="InMemoryOrgHierarchy"/> rebuilt from the tables) so
/// per-request descendant/ancestor lookups never hit the database. The
/// snapshot is cached <b>per tenant</b> (auth blueprint gap — plan item B4):
/// a single shared snapshot would otherwise reflect whichever tenant happened
/// to be ambient on the thread that triggered a refresh, leaking that
/// tenant's graph into every other tenant's reads for up to
/// <see cref="CacheDuration"/>. The host's own cache entry (keyed by
/// <see cref="Guid.Empty"/>) legitimately holds the union of every tenant's
/// units, since the query filter bypasses entirely in host scope — same
/// "host sees everything" convention as the row-level filter. An unresolved
/// tenant (fail-closed) never touches the cache or the database at all: it
/// always resolves to an empty hierarchy. A local mutation
/// (<see cref="AddUnitAsync"/> / <see cref="MoveUnitAsync"/>) invalidates
/// only the current tenant's entry; other application instances converge
/// on structural changes without a distributed signal once that tenant's
/// entry expires — org structure changes are rare and a short convergence
/// window is acceptable; call <see cref="Invalidate"/> to force an
/// immediate reload of the current tenant's entry.
/// </summary>
public sealed class EfOrgHierarchy(
    IDbContextFactory<AuthorizationStoreDbContext> factory,
    TimeProvider time,
    ICurrentTenant currentTenant)
    : IOrgHierarchy
{
    private static readonly InMemoryOrgHierarchy Empty = new();

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, CachedSnapshot> _snapshots = new();

    private sealed record CachedSnapshot(InMemoryOrgHierarchy Hierarchy, DateTimeOffset LoadedAt);

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
            var edgeExists = await db.OrgUnitParents
                .Where(e => e.ChildId == id && e.ParentId == parent)
                .AnyAsync(ct);
            if (!edgeExists)
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

        // Delete + re-insert must be atomic: a mid-way failure would otherwise
        // leave the unit parentless (a root), silently widening or narrowing
        // every scope computed from it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await EnsureUnitAsync(db, id, ct);
            await db.OrgUnitParents.Where(e => e.ChildId == id).ExecuteDeleteAsync(ct);
            foreach (var parent in newParents)
            {
                await EnsureUnitAsync(db, parent, ct);
                db.OrgUnitParents.Add(new OrgUnitParentRow { ChildId = id, ParentId = parent });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        Invalidate();
    }

    /// <summary>Discards the current tenant's cached snapshot; its next query
    /// reloads from the database. Other tenants' cached entries are untouched.</summary>
    public void Invalidate()
    {
        if (!currentTenant.IsHost && currentTenant.TenantId is null)
            return;

        lock (_gate)
            _snapshots.TryRemove(CacheKey(), out _);
    }

    /// <summary>
    /// Host scope (<see cref="ICurrentTenant.IsHost"/>) and each resolved
    /// tenant get their own cache slot, keyed by tenant id (host uses
    /// <see cref="Guid.Empty"/> — no real tenant id is ever that value).
    /// </summary>
    private Guid CacheKey() => currentTenant.IsHost ? Guid.Empty : currentTenant.TenantId!.Value;

    private InMemoryOrgHierarchy Snapshot()
    {
        // Fail-closed: an unresolved tenant never touches the cache or the
        // database, and never shares another tenant's (or the host's) entry.
        if (!currentTenant.IsHost && currentTenant.TenantId is null)
            return Empty;

        var key = CacheKey();

        // Fast path: a fresh snapshot serves without touching the database or
        // contending on the gate.
        lock (_gate)
        {
            if (_snapshots.TryGetValue(key, out var cached)
                && time.GetUtcNow() - cached.LoadedAt < CacheDuration)
                return cached.Hierarchy;
        }

        // Refresh OUTSIDE the gate: concurrent callers may all reload (the
        // rebuild is idempotent), but no thread ever holds the lock across
        // DbContext creation and table reads. This context reads the SAME
        // ambient ICurrentTenant that produced `key`, so its query filter
        // scopes the refresh to exactly that tenant (or bypasses entirely
        // for the host key).
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
            if (!_snapshots.TryGetValue(key, out var existing) || now - existing.LoadedAt >= CacheDuration)
            {
                var fresh = new CachedSnapshot(snapshot, now);
                _snapshots[key] = fresh;
                return fresh.Hierarchy;
            }

            return existing.Hierarchy;
        }
    }

    private static async Task EnsureUnitAsync(
        AuthorizationStoreDbContext db, Guid id, CancellationToken ct)
    {
        var exists = await db.OrgUnits.Where(u => u.Id == id).AnyAsync(ct);
        if (!exists)
            db.OrgUnits.Add(new OrgUnitRow { Id = id });
    }
}
