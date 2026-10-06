namespace Modulus.Authorization.Scopes;

/// <summary>
/// All the constraints of one alternative: a record is covered when it satisfies <b>every</b> one. An empty clause covers the
/// whole company.
/// </summary>
public sealed record ScopeClause(IReadOnlyList<PermissionScope> Constraints)
{
    /// <summary>The clause that covers every record of the company.</summary>
    public static ScopeClause Everything { get; } = new([]);

    /// <inheritdoc />
    public bool Equals(ScopeClause? other)
        => other is not null && Constraints.Count == other.Constraints.Count
           && Constraints.All(c => other.Constraints.Contains(c));

    /// <inheritdoc />
    public override int GetHashCode() => Constraints.Aggregate(0, (hash, c) => hash ^ c.GetHashCode());
}

/// <summary>
/// What a caller may reach with one permission, as a union of <see cref="ScopeClause"/>s: a record is covered when it satisfies
/// <b>any</b> clause. Grants through several roles union (Own ∪ Assigned), and a restriction is a constraint added to every
/// clause (Tenant ∩ Department = Department). No clauses means the permission covers nothing.
/// </summary>
public sealed class ResolvedScope
{
    private ResolvedScope(IReadOnlyList<ScopeClause> anyOf) => AnyOf = anyOf;

    /// <summary>The alternatives; empty means nothing is covered.</summary>
    public IReadOnlyList<ScopeClause> AnyOf { get; }

    /// <summary>The permission covers nothing (not granted, denied or out of validity).</summary>
    public bool IsNone => AnyOf.Count == 0;

    /// <summary>The permission covers every record of the company, with no narrowing.</summary>
    public bool IsTenantWide => AnyOf.Any(c => c.Constraints.Count == 0);

    /// <summary>Covers nothing.</summary>
    public static ResolvedScope None { get; } = new([]);

    /// <summary>Covers every record of the company.</summary>
    public static ResolvedScope TenantWide { get; } = new([ScopeClause.Everything]);

    /// <summary>Builds a scope from alternatives, dropping duplicates and collapsing to <see cref="TenantWide"/> when one is unconstrained.</summary>
    public static ResolvedScope Of(IEnumerable<ScopeClause> clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        var distinct = clauses.Distinct().ToList();
        if (distinct.Count == 0)
            return None;
        return distinct.Any(c => c.Constraints.Count == 0) ? TenantWide : new ResolvedScope(distinct);
    }

    /// <summary>The same scope with <paramref name="restriction"/> added to every alternative.</summary>
    public ResolvedScope Restrict(PermissionScope restriction)
    {
        ArgumentNullException.ThrowIfNull(restriction);
        if (IsNone || restriction.Kind is ScopeKind.Tenant)
            return this;

        return Of(AnyOf.Select(clause => clause.Constraints.Contains(restriction)
            ? clause
            : new ScopeClause([.. clause.Constraints, restriction])));
    }

    /// <summary>The union of this and <paramref name="other"/>.</summary>
    public ResolvedScope Union(ResolvedScope other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Of(AnyOf.Concat(other.AnyOf));
    }

    /// <inheritdoc />
    public override string ToString()
        => IsNone ? "none" : string.Join(" | ", AnyOf.Select(c => c.Constraints.Count == 0 ? "tenant" : string.Join(" & ", c.Constraints)));
}
