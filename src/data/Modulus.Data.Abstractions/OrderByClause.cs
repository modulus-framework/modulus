namespace Modulus.Data.Abstractions;

using System.Linq.Expressions;

/// <summary>
/// Represents an ORDER BY clause in a specification, tracking the selector and direction.
/// </summary>
public sealed record OrderByClause<T>(
    Expression<Func<T, object>> Selector,
    bool Descending = false);
