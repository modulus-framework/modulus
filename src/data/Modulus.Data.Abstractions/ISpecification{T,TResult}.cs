namespace Modulus.Data.Abstractions;

using System.Linq.Expressions;

/// <summary>
/// A specification that includes a server-side projection to a different type.
/// Allows querying and projecting in a single server-side operation without
/// materializing full entities.
/// </summary>
public interface ISpecification<T, TResult> : ISpecification<T>
    where T : class
{
    /// <summary>Server-side projection from T to TResult (executed in the query).</summary>
    Expression<Func<T, TResult>>? ProjectionExpression { get; }
}

/// <summary>Base implementation with projection support.</summary>
public abstract class Specification<T, TResult> : Specification<T>, ISpecification<T, TResult>
    where T : class
{
    public Expression<Func<T, TResult>>? ProjectionExpression { get; protected set; }
}
