namespace Modulus.EntityFrameworkCore.Isolation;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// A module context's connection, opened for commands EF Core does not execute (Dapper, read models, bulk copy).
/// Pass <see cref="Transaction"/> to every command so it joins the unit of work's transaction, and dispose the lease
/// to release the connection (it is reference-counted with EF's own use, so the context's open state is restored).
/// </summary>
public sealed class TenantConnection : IAsyncDisposable, IDisposable
{
    private readonly DatabaseFacade _database;
    private bool _disposed;

    internal TenantConnection(DatabaseFacade database, DbConnection connection, DbTransaction? transaction)
    {
        _database = database;
        Connection = connection;
        Transaction = transaction;
    }

    /// <summary>The open connection, whose session carries the ambient tenant when the context uses row-level security.</summary>
    public DbConnection Connection { get; }

    /// <summary>The context's current transaction, or null; pass it to each command (Dapper: <c>transaction:</c>).</summary>
    public DbTransaction? Transaction { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _database.CloseConnectionAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _database.CloseConnection();
    }
}

/// <summary>Opens a module context's connection for SQL that EF Core does not execute, without losing tenant isolation.</summary>
public static class TenantConnectionExtensions
{
    /// <summary>
    /// Opens <paramref name="context"/>'s connection for Dapper or another ADO.NET client. The raw-SQL guard and the
    /// per-command session refresh only see commands EF executes, so this is the one entry point for everything else:
    /// <list type="bullet">
    /// <item><b>Row-level security</b> (<c>AddPostgreSqlRowLevelSecurity</c> / <c>AddSqlServerRowLevelSecurity</c>): the
    /// ambient tenant is written into the session before the connection is returned, even when the connection was
    /// already open for another tenant, so the database limits every statement. Re-open after a tenant switch.</item>
    /// <item><b>Database per tenant</b>: the connection already points at the tenant's database.</item>
    /// <item><b>Shared tables with the query filter only</b> (no RLS, for example MySQL or SQLite): nothing below EF would
    /// keep companies apart, so inside a tenant (or with no tenant) the call throws <see cref="CrossTenantSqlException"/>
    /// unless a reviewed <see cref="CrossTenantSql.Allow"/> scope is open. The host context is allowed.</item>
    /// </list>
    /// Refusals and opt-ins are recorded in the security audit like the raw-SQL guard's.
    /// </summary>
    /// <param name="context">The module context.</param>
    /// <param name="ct">Cancellation.</param>
    /// <example>
    /// <code>
    /// await using var lease = await db.OpenTenantConnectionAsync(ct);
    /// var rows = await lease.Connection.QueryAsync&lt;InvoiceRow&gt;(sql, transaction: lease.Transaction);
    /// </code>
    /// </example>
    public static async Task<TenantConnection> OpenTenantConnectionAsync(this ModuleDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isolation = context.ContextServices.GetServices<ModuleDbContextIsolation>()
            .FirstOrDefault(i => i.ContextType == context.GetType());
        var session = context.TenantSession;
        if (!session.IsHost && (isolation is null || isolation.Mode == TenantIsolationMode.QueryFilter))
            CheckFilterOnlyAccess(context);

        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        try
        {
            if (isolation?.SessionInterceptor is { } interceptor)
                await interceptor.EnsureSessionAsync(connection, transaction, context, ct).ConfigureAwait(false);
        }
        catch
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            throw;
        }

        return new TenantConnection(context.Database, connection, transaction);
    }

    private static void CheckFilterOnlyAccess(ModuleDbContext context)
    {
        var tables = context.Model.GetEntityTypes()
            .Where(e => typeof(IHasTenantId).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tables.Count == 0)
            return;

        const string Origin = "A raw connection on a database shared without row-level security";
        var audit = context.ContextServices.GetService<ISecurityAuditLog>();
        if (CrossTenantSql.CurrentReason is { } reason)
        {
            TenantSqlGuardInterceptor.Audit(audit, context, SecurityAuditOutcomes.Overridden, tables, Origin, reason);
            return;
        }

        TenantSqlGuardInterceptor.Audit(audit, context, SecurityAuditOutcomes.Denied, tables, Origin, reason: null);
        throw new CrossTenantSqlException(tables, Origin);
    }
}
