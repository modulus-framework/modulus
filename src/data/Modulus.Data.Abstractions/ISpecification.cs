namespace Modulus.Data.Abstractions;

using System.Linq.Expressions;

/// <summary>
/// Encapsulates a query as an object.
/// Translated to EF Core LINQ, MongoDB filter, or ES query by each provider.
/// </summary>
public interface ISpecification<T>
{
    /// <summary>WHERE clause filter.</summary>
    Expression<Func<T, bool>>? Filter { get; }

    /// <summary>Include chains for eager-loading related entities (supports ThenInclude).</summary>
    List<IncludeChain<T>>? IncludeChains { get; }

    /// <summary>Ordered list of ORDER BY clauses (supports multiple and ThenBy).</summary>
    List<OrderByClause<T>>? OrderByClauses { get; }

    /// <summary>Number of records to skip (used with Take for paging).</summary>
    int? Skip { get; }

    /// <summary>Number of records to take (used with Skip for paging).</summary>
    int? Take { get; }

    /// <summary>
    /// When true, splits queries into multiple statements to avoid cartesian explosions
    /// (useful when combining multiple Include paths). Requires EF Core support.
    /// </summary>
    bool AsSplitQuery { get; }

    /// <summary>When true, ignores global query filters (soft-delete, multi-tenancy, etc.).</summary>
    bool IgnoreQueryFilters { get; }

    /// <summary>Optional tag to attach to the generated query for logging/debugging.</summary>
    string? Tag { get; }

    /// <summary>When false, entities are tracked by the DbContext (default true for specs).</summary>
    bool AsNoTracking { get; }

    /// <summary>
    /// Combines this filter with another using AND logic.
    /// Returns a new spec with (this.Filter AND other.Filter).
    /// </summary>
    ISpecification<T> And(Expression<Func<T, bool>> other);

    /// <summary>
    /// Combines this filter with another using OR logic.
    /// Returns a new spec with (this.Filter OR other.Filter).
    /// </summary>
    ISpecification<T> Or(Expression<Func<T, bool>> other);

    /// <summary>
    /// Negates this filter.
    /// Returns a new spec with NOT(this.Filter).
    /// </summary>
    ISpecification<T> Not();
}

/// <summary>Base implementation with protected setters and combinator support.</summary>
public abstract class Specification<T> : ISpecification<T>
{
    public Expression<Func<T, bool>>? Filter { get; protected set; }
    public List<IncludeChain<T>>? IncludeChains { get; protected set; }
    public List<OrderByClause<T>>? OrderByClauses { get; protected set; }
    public int? Skip { get; protected set; }
    public int? Take { get; protected set; }
    public bool AsSplitQuery { get; protected set; }
    public bool IgnoreQueryFilters { get; protected set; }
    public string? Tag { get; protected set; }
    public bool AsNoTracking { get; protected set; } = true;

    /// <summary>Adds an ORDER BY clause.</summary>
    protected void AddOrderBy(Expression<Func<T, object>> selector, bool descending = false)
    {
        OrderByClauses ??= [];
        OrderByClauses.Add(new OrderByClause<T>(selector, descending));
    }

    /// <summary>Adds a simple Include (no ThenInclude).</summary>
    protected void AddInclude(Expression<Func<T, object>> selector)
    {
        IncludeChains ??= [];
        IncludeChains.Add(new SimpleInclude<T, object>(
            Expression.Lambda<Func<T, object>>(selector.Body, selector.Parameters)));
    }

    public virtual ISpecification<T> And(Expression<Func<T, bool>> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Filter is null)
            Filter = other;
        else
            Filter = CombineWithAnd(Filter, other);
        return this;
    }

    public virtual ISpecification<T> Or(Expression<Func<T, bool>> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Filter is null)
            Filter = other;
        else
            Filter = CombineWithOr(Filter, other);
        return this;
    }

    public virtual ISpecification<T> Not()
    {
        if (Filter is not null)
            Filter = Expression.Lambda<Func<T, bool>>(
                Expression.Not(Filter.Body),
                Filter.Parameters);
        return this;
    }

    /// <summary>Combines two predicates with AND logic by parameter rebinding.</summary>
    private static Expression<Func<T, bool>> CombineWithAnd(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var leftBody = new ParameterRebinder(param).Visit(left.Body);
        var rightBody = new ParameterRebinder(param).Visit(right.Body);
        var combined = Expression.AndAlso(leftBody, rightBody);
        return Expression.Lambda<Func<T, bool>>(combined, param);
    }

    /// <summary>Combines two predicates with OR logic by parameter rebinding.</summary>
    private static Expression<Func<T, bool>> CombineWithOr(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var leftBody = new ParameterRebinder(param).Visit(left.Body);
        var rightBody = new ParameterRebinder(param).Visit(right.Body);
        var combined = Expression.OrElse(leftBody, rightBody);
        return Expression.Lambda<Func<T, bool>>(combined, param);
    }

    /// <summary>Rebinds expression parameters to a single target parameter.</summary>
    private sealed class ParameterRebinder(ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node.Type == target.Type ? target : base.VisitParameter(node);
    }
}
