namespace Modulus.Authorization.Organization;

/// <summary>
/// The default <see cref="IOrgHierarchy"/>: holds the org tree/DAG in memory and
/// memoises transitive descendant/ancestor closures. The hierarchy is
/// runtime-mutable — reorganizations (move/merge/split) are a supported operation,
/// not a data-repair emergency (blueprint §8) — and any structural change clears
/// the closure cache. Empty by default, so a hierarchy that is never seeded yields
/// no descendants/ancestors (fail-closed).
/// </summary>
public sealed class InMemoryOrgHierarchy : IOrgHierarchy
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<Guid>> _parents = [];
    private readonly Dictionary<Guid, HashSet<Guid>> _children = [];

    // Closure memoisation, guarded by _gate and cleared on any structural mutation.
    private readonly Dictionary<Guid, IReadOnlySet<Guid>> _descendantsCache = [];
    private readonly Dictionary<Guid, IReadOnlySet<Guid>> _ancestorsCache = [];

    /// <summary>
    /// Adds a unit and its parent edges (accumulating — additional parents extend a
    /// matrixed DAG). A root unit passes no parents. Missing parents are created.
    /// </summary>
    public InMemoryOrgHierarchy AddUnit(Guid id, params Guid[] parents)
    {
        ArgumentNullException.ThrowIfNull(parents);
        lock (_gate)
        {
            EnsureNode(id);
            foreach (var parent in parents)
                Link(id, parent, nameof(parents));
            InvalidateClosures();
        }
        return this;
    }

    /// <summary>
    /// Reparents a unit — the reorg primitive: its subtree's effective scope moves
    /// with it because placements reference the stable unit id, not a path. Replaces
    /// all of the unit's existing parents.
    /// </summary>
    public InMemoryOrgHierarchy MoveUnit(Guid id, params Guid[] newParents)
    {
        ArgumentNullException.ThrowIfNull(newParents);
        lock (_gate)
        {
            EnsureNode(id);
            foreach (var oldParent in _parents[id])
                _children[oldParent].Remove(id);
            _parents[id].Clear();
            foreach (var parent in newParents)
                Link(id, parent, nameof(newParents));
            InvalidateClosures();
        }
        return this;
    }

    public bool Contains(Guid orgUnitId)
    {
        lock (_gate)
            return _parents.ContainsKey(orgUnitId);
    }

    public IReadOnlySet<Guid> Descendants(Guid orgUnitId)
    {
        lock (_gate)
            return GetOrBuild(orgUnitId, _children, _descendantsCache);
    }

    public IReadOnlySet<Guid> Ancestors(Guid orgUnitId)
    {
        lock (_gate)
            return GetOrBuild(orgUnitId, _parents, _ancestorsCache);
    }

    private void Link(Guid id, Guid parent, string paramName)
    {
        if (parent == id)
            throw new ArgumentException("A unit cannot be its own parent.", paramName);
        EnsureNode(parent);
        _parents[id].Add(parent);
        _children[parent].Add(id);
    }

    private static IReadOnlySet<Guid> GetOrBuild(
        Guid start,
        Dictionary<Guid, HashSet<Guid>> edges,
        Dictionary<Guid, IReadOnlySet<Guid>> cache)
    {
        if (cache.TryGetValue(start, out var cached))
            return cached;

        // Breadth/depth-first walk of the edge map; the visited set both collects
        // the closure and guards against cycles in a mis-seeded DAG.
        var result = new HashSet<Guid>();
        if (edges.ContainsKey(start))
        {
            var pending = new Stack<Guid>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!edges.TryGetValue(node, out var next))
                    continue;
                foreach (var neighbour in next)
                {
                    if (neighbour != start && result.Add(neighbour))
                        pending.Push(neighbour);
                }
            }
        }

        cache[start] = result;
        return result;
    }

    private void EnsureNode(Guid id)
    {
        if (!_parents.ContainsKey(id))
            _parents[id] = [];
        if (!_children.ContainsKey(id))
            _children[id] = [];
    }

    private void InvalidateClosures()
    {
        _descendantsCache.Clear();
        _ancestorsCache.Clear();
    }
}
