namespace Modulus.Data.Abstractions;

using System.Linq.Expressions;

/// <summary>
/// Represents a chain of Include/ThenInclude paths for eager-loading related entities.
/// Example: order => order.Customer.Orders (Include(o => o.Customer).ThenInclude(c => c.Orders)).
/// </summary>
public abstract class IncludeChain<T>
{
    /// <summary>The top-level Include selector from T.</summary>
    public abstract Expression<Func<T, object>> IncludeExpression { get; }

    /// <summary>Optional ThenInclude chain off this Include.</summary>
    public abstract IncludeChain<object>? ThenInclude { get; }
}

/// <summary>Represents a single-level Include (no ThenInclude).</summary>
internal sealed class SimpleInclude<T, TIncluded>(
    Expression<Func<T, TIncluded>> selector)
    : IncludeChain<T>
{
    public override Expression<Func<T, object>> IncludeExpression
        => Expression.Lambda<Func<T, object>>(
            Expression.Convert(selector.Body, typeof(object)),
            selector.Parameters);

    public override IncludeChain<object>? ThenInclude => null;
}

/// <summary>Represents an Include with one or more ThenInclude chains.</summary>
internal sealed class NestedInclude<T, TIncluded>(
    Expression<Func<T, TIncluded>> selector,
    IncludeChain<TIncluded> thenInclude)
    : IncludeChain<T>
{
    public override Expression<Func<T, object>> IncludeExpression
        => Expression.Lambda<Func<T, object>>(
            Expression.Convert(selector.Body, typeof(object)),
            selector.Parameters);

    public override IncludeChain<object>? ThenInclude => thenInclude as IncludeChain<object>;
}
