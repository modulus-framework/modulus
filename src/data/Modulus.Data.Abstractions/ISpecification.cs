namespace Modulus.Data.Abstractions;

using System.Linq.Expressions;

/// <summary>
/// Encapsulates a query as an object.
/// Translated to EF Core LINQ, MongoDB filter, or ES query by each provider.
/// </summary>
public interface ISpecification<T>
{
    Expression<Func<T, bool>>? Filter { get; }
    List<Expression<Func<T, object>>> Includes { get; }
    Expression<Func<T, object>>? OrderBy { get; }
    Expression<Func<T, object>>? OrderByDesc { get; }
    int? Skip { get; }
    int? Take { get; }
    bool AsNoTracking { get; }
}

/// <summary>Base implementation with protected setters.</summary>
public abstract class Specification<T> : ISpecification<T>
{
    public Expression<Func<T, bool>>? Filter { get; protected set; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public Expression<Func<T, object>>? OrderBy { get; protected set; }
    public Expression<Func<T, object>>? OrderByDesc { get; protected set; }
    public int? Skip { get; protected set; }
    public int? Take { get; protected set; }
    public bool AsNoTracking { get; protected set; } = true;
}
