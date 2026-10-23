using System.Linq.Expressions;
using Modulus.Authorization.Grants;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Predicates over captured values, written as chains of scalar comparisons. <c>list.Contains(column)</c> makes EF send the list as one
/// collection parameter: PostgreSQL turns it into an array, but the MySQL provider cannot type it, and the query fails. A chain of
/// <c>column == value</c> comparisons is one query on every provider, with one scalar parameter per value.
/// </summary>
internal static class ScalarPredicates
{
    /// <summary>True when <paramref name="member"/> equals one of <paramref name="values"/>; false for an empty list.</summary>
    public static Expression<Func<T, bool>> In<T, TValue>(Expression<Func<T, TValue>> member, IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(values);

        Expression body = Expression.Constant(false);
        foreach (var value in values)
            body = Expression.OrElse(body, Expression.Equal(member.Body, Expression.Constant(value, typeof(TValue))));
        return Expression.Lambda<Func<T, bool>>(body, member.Parameters[0]);
    }

    /// <summary>
    /// The holder test of a grant row: a role whose lower-cased name is in <paramref name="lowerRoles"/>, or, when given, the user whose
    /// lower-cased key is <paramref name="lowerUser"/>. Pass the holder selector lower-cased (<c>g =&gt; g.Holder.ToLower()</c>), as the stores do.
    /// </summary>
    public static Expression<Func<T, bool>> Holder<T>(
        Expression<Func<T, GrantHolderType>> holderType,
        Expression<Func<T, string>> holder,
        IReadOnlyCollection<string> lowerRoles,
        string? lowerUser)
    {
        ArgumentNullException.ThrowIfNull(lowerRoles);
        var row = Expression.Parameter(typeof(T), "row");
        var type = new ReplaceParameter(holderType.Parameters[0], row).Visit(holderType.Body)!;
        var name = new ReplaceParameter(holder.Parameters[0], row).Visit(holder.Body)!;

        Expression roles = Expression.Constant(false);
        foreach (var role in lowerRoles)
            roles = Expression.OrElse(roles, Expression.Equal(name, Expression.Constant(role)));
        Expression body = Expression.AndAlso(Expression.Equal(type, Expression.Constant(GrantHolderType.Role)), roles);

        if (lowerUser is not null)
        {
            body = Expression.OrElse(body, Expression.AndAlso(
                Expression.Equal(type, Expression.Constant(GrantHolderType.User)),
                Expression.Equal(name, Expression.Constant(lowerUser))));
        }

        return Expression.Lambda<Func<T, bool>>(body, row);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
