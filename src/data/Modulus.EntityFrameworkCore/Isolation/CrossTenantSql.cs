namespace Modulus.EntityFrameworkCore.Isolation;

/// <summary>
/// The explicit opt-in for SQL that may read or write across tenants while a tenant is in scope: raw SQL
/// (<c>FromSql</c>, <c>SqlQuery</c>, <c>ExecuteSql</c>) or an <c>IgnoreQueryFilters()</c> query that touches a
/// table of an <c>IHasTenantId</c> entity. Without it, <see cref="TenantSqlGuardInterceptor"/> rejects them.
/// </summary>
/// <example>
/// <code>
/// using (CrossTenantSql.Allow("Nightly reconciliation reads every company's open invoices"))
///     rows = await db.Invoices.FromSql($"select * from inv_invoices where status = 0").ToListAsync(ct);
/// </code>
/// </example>
/// <remarks>
/// The scope flows with the async context (like <c>ICurrentTenant.Change</c>), so it covers the awaited calls
/// inside the <c>using</c> block only. Prefer the host context (<c>currentTenant.Change(null)</c>) for genuine
/// all-tenant work; this opt-in is for a reviewed statement that must run inside a tenant scope. On PostgreSQL
/// and SQL Server with row-level security on, the database still limits the statement to the session's tenant.
/// </remarks>
public static class CrossTenantSql
{
    private static readonly AsyncLocal<string?> s_reason = new();

    /// <summary>The reason of the innermost open scope, or null when none is open.</summary>
    public static string? CurrentReason => s_reason.Value;

    /// <summary>Allows cross-tenant SQL until the returned scope is disposed.</summary>
    /// <param name="reason">Why the statement must cross tenants; logged with every statement it allows.</param>
    public static IDisposable Allow(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var previous = s_reason.Value;
        s_reason.Value = reason;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            s_reason.Value = previous;
        }
    }
}

/// <summary>
/// Thrown when SQL that bypasses the tenant query filter touches a tenant table while a tenant (or no tenant)
/// is in scope, without a <see cref="CrossTenantSql.Allow"/> scope.
/// </summary>
public sealed class CrossTenantSqlException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="tables">The tenant tables the statement references.</param>
    /// <param name="origin">What produced the statement (raw SQL, an unfiltered query).</param>
    public CrossTenantSqlException(IReadOnlyList<string> tables, string origin)
        : base($"{origin} touches tenant table(s) {string.Join(", ", tables)} while a tenant is in scope. "
            + "Use the filtered LINQ query, the host context (ICurrentTenant.Change(null)), or wrap a reviewed "
            + "statement in CrossTenantSql.Allow(reason).")
    {
        Tables = tables;
        Origin = origin;
    }

    /// <summary>The tenant tables the statement references.</summary>
    public IReadOnlyList<string> Tables { get; }

    /// <summary>What produced the statement.</summary>
    public string Origin { get; }
}
