namespace Modulus.Authorization.Scopes;

using System.Linq.Expressions;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

/// <summary>The current caller as seen by a scope check: identity, own org scope, and assigned objects.</summary>
public sealed class ScopeSubject(
    ICurrentUser user,
    IOrgHierarchy hierarchy,
    IAssignmentStore assignments,
    ICurrentDataScope? dataScope = null,
    TimeProvider? clock = null) : IScopeSubject
{
    // With no org scoping configured the framework registers an "unrestricted" null scope so the org query filter is a
    // no-op. A grant that names the caller's own org scope must not read that as "everything": it needs placements to mean anything.
    private readonly ICurrentDataScope? _dataScope = dataScope is NullCurrentDataScope ? null : dataScope;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, IReadOnlySet<Guid>> _assigned = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<Guid>? _units;

    /// <inheritdoc />
    public Guid? UserId => user.IsAuthenticated ? user.UserId : null;

    /// <inheritdoc />
    public bool IsOrgUnrestricted => _dataScope?.IsUnrestricted ?? false;

    /// <inheritdoc />
    public IReadOnlySet<Guid> OrgUnits => _units ??= _dataScope?.OrgUnitIds.ToHashSet() ?? [];

    /// <inheritdoc />
    public IReadOnlySet<Guid> OrgSubtree(Guid unit)
    {
        if (!hierarchy.Contains(unit))
            return new HashSet<Guid>();

        var subtree = new HashSet<Guid>(hierarchy.Descendants(unit)) { unit };
        return subtree;
    }

    /// <inheritdoc />
    public IReadOnlySet<Guid> AssignedTargets(string assignmentType)
    {
        if (UserId is not { } id)
            return new HashSet<Guid>();

        if (!_assigned.TryGetValue(assignmentType, out var targets))
            _assigned[assignmentType] = targets = assignments.TargetsFor(id, assignmentType, _clock.GetUtcNow());
        return targets;
    }
}

/// <summary>
/// The single place a scope becomes behavior: as a query filter for lists, searches, counts, exports and dashboards, and as a
/// check on one loaded record. Both read the same <see cref="ResolvedScope"/> and the same <see cref="ScopeMap"/>, so a record
/// a list hides cannot be reached by id, and the reverse (TST-003).
/// </summary>
public interface IScopeEnforcer
{
    /// <summary>What <paramref name="permission"/> covers for the current caller.</summary>
    ResolvedScope Describe(string permission);

    /// <summary>
    /// Restricts <paramref name="source"/> to the records <paramref name="permission"/> covers: no scope gives an empty query,
    /// a company-wide one leaves it as is. Fail-closed: an alternative that needs a key the type's <see cref="ScopeMap"/> lacks matches nothing.
    /// </summary>
    IQueryable<T> Apply<T>(IQueryable<T> source, string permission) where T : class;

    /// <summary>Whether <paramref name="record"/> is covered by <paramref name="permission"/> for the current caller.</summary>
    bool IsInScope(object record, string permission);
}

/// <summary>Default <see cref="IScopeEnforcer"/>.</summary>
public sealed class ScopeEnforcer(
    IPermissionScopeResolver resolver,
    IScopeMapRegistry maps,
    IScopeSubject subject) : IScopeEnforcer
{
    /// <inheritdoc />
    public ResolvedScope Describe(string permission) => resolver.Resolve(permission);

    /// <inheritdoc />
    public IQueryable<T> Apply<T>(IQueryable<T> source, string permission) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var scope = resolver.Resolve(permission);
        if (scope.IsNone)
            return source.Where(_ => false);
        if (scope.IsTenantWide)
            return source;

        var map = maps.Find(typeof(T)) as ScopeMap<T>;
        var row = Expression.Parameter(typeof(T), "e");
        Expression? any = null;
        foreach (var clause in scope.AnyOf)
        {
            Expression? all = null;
            foreach (var constraint in clause.Constraints)
            {
                var term = Term(constraint, map, row);
                all = all is null ? term : Expression.AndAlso(all, term);
            }

            any = any is null ? all! : Expression.OrElse(any, all!);
        }

        return source.Where(Expression.Lambda<Func<T, bool>>(any!, row));
    }

    /// <inheritdoc />
    public bool IsInScope(object record, string permission)
    {
        ArgumentNullException.ThrowIfNull(record);
        var scope = resolver.Resolve(permission);
        if (scope.IsNone)
            return false;
        if (scope.IsTenantWide)
            return true;

        return maps.Find(record.GetType()) is { } map && ScopeEvaluator.Allows(scope, map.FactsOf(record), subject);
    }

    private Expression Term<T>(PermissionScope constraint, ScopeMap<T>? map, ParameterExpression row) where T : class
    {
        var never = Expression.Constant(false);
        switch (constraint.Kind)
        {
            case ScopeKind.Tenant:
                return Expression.Constant(true);

            case ScopeKind.Own:
                if (map?.Owner is not { } owner || subject.UserId is not { } me)
                    return never;
                return Expression.Equal(Inline(owner, row), Expression.Constant((Guid?)me, typeof(Guid?)));

            case ScopeKind.OrgUnit:
                if (constraint.OrgUnitId is null && subject.IsOrgUnrestricted)
                    return Expression.Constant(true);
                if (map?.OrgUnit is not { } unit)
                    return never;
                var units = constraint.OrgUnitId is { } named ? subject.OrgSubtree(named) : subject.OrgUnits;
                return Contains(units, Inline(unit, row));

            case ScopeKind.Assigned:
                if (map is null || !map.AccessKeys.TryGetValue(constraint.Value!, out var key) || subject.UserId is null)
                    return never;
                return Contains(subject.AssignedTargets(constraint.Value!), Inline(key, row));

            default:
                return never;
        }
    }

    // List<Guid?>.Contains(selector): a parameter EF turns into IN (...). A null key never matches.
    private static Expression Contains(IReadOnlySet<Guid> ids, Expression selected)
    {
        var list = ids.Select(id => (Guid?)id).ToList();
        var holder = Expression.Property(Expression.Constant(new Holder(list)), nameof(Holder.Ids));
        return Expression.Call(holder, typeof(List<Guid?>).GetMethod(nameof(List<Guid?>.Contains))!, selected);
    }

    private static Expression Inline<T>(Expression<Func<T, Guid?>> selector, ParameterExpression row)
        => new Replace(selector.Parameters[0], row).Visit(selector.Body)!;

    private sealed class Holder(List<Guid?> ids)
    {
        public List<Guid?> Ids { get; } = ids;
    }

    private sealed class Replace(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
