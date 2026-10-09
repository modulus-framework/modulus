namespace Modulus.Authorization.Scopes;

using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Exports and bulk actions are the usual way around a scoped list: <c>GET /orders</c> filters, <c>POST /orders/export</c> or
/// <c>POST /orders/approve-all</c> forgets to. These helpers put them behind the same scope as the list and the single record.
/// </summary>
public static class ScopeEnforcerExtensions
{
    /// <summary>
    /// Restricts <paramref name="source"/> for an export: the caller must hold both <paramref name="exportPermission"/> and
    /// <paramref name="readPermission"/>, and receives only the records <b>both</b> cover, so exporting never reaches further than reading.
    /// </summary>
    /// <exception cref="ForbiddenException">The caller lacks either permission.</exception>
    public static IQueryable<T> ForExport<T>(
        this IScopeEnforcer scopes, IQueryable<T> source, string exportPermission, string readPermission)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPermission);
        ArgumentException.ThrowIfNullOrWhiteSpace(readPermission);

        if (scopes.Describe(exportPermission).IsNone)
            throw new ForbiddenException(exportPermission);
        if (scopes.Describe(readPermission).IsNone)
            throw new ForbiddenException(readPermission);

        return scopes.Apply(scopes.Apply(source, readPermission), exportPermission);
    }

    /// <summary>The records of <paramref name="records"/> that <paramref name="permission"/> does not cover for the caller.</summary>
    public static IReadOnlyList<T> OutOfScope<T>(this IScopeEnforcer scopes, IEnumerable<T> records, string permission)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return [.. records.Where(r => !scopes.IsInScope(r, permission))];
    }

    /// <summary>
    /// Refuses a bulk action unless <b>every</b> record is covered by <paramref name="permission"/>: all or nothing, so one
    /// foreign record in a batch cannot be acted on, and a partial result does not reveal which ids exist.
    /// </summary>
    /// <exception cref="ForbiddenException">At least one record is outside the caller's scope.</exception>
    public static void EnsureAllInScope<T>(this IScopeEnforcer scopes, IEnumerable<T> records, string permission)
        where T : class
    {
        if (scopes.OutOfScope(records, permission).Count > 0)
            throw new ForbiddenException(permission);
    }
}
