namespace Modulus.EntityFrameworkCore.Isolation;

using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Pushes the ambient tenant into the database session so row-level security policies can enforce company
/// isolation below EF Core (raw SQL, <c>IgnoreQueryFilters</c>, a forgotten filter). One subclass per provider
/// supplies the statement (<c>set_config</c> on PostgreSQL, <c>sp_set_session_context</c> on SQL Server).
/// </summary>
/// <remarks>
/// <para>
/// The session is written when a connection opens (a pooled physical connection never keeps the previous
/// tenant), and again before any command when the ambient tenant changed since (a <c>Change(...)</c> scope
/// inside one open connection) or a rollback may have reverted it (PostgreSQL rolls back <c>set_config</c>).
/// </para>
/// <para>
/// Only <see cref="ModuleDbContext"/> instances are handled. Connections used outside EF Core
/// (<c>Database.GetDbConnection()</c> handed to Dapper) carry whatever the last EF command or open wrote, so
/// open them through the context (<c>Database.OpenConnectionAsync()</c>) before use.
/// </para>
/// </remarks>
public abstract class TenantSessionInterceptor : DbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbConnection, StrongBox<TenantSession>> _applied = new();

    /// <summary>True when this interceptor knows how to write the session of <paramref name="connection"/>.</summary>
    /// <param name="connection">The connection about to be used.</param>
    protected abstract bool Supports(DbConnection connection);

    /// <summary>Sets the text and parameters of a command that writes <paramref name="session"/> into the session.</summary>
    /// <param name="command">A new command on the connection, already enlisted in the current transaction.</param>
    /// <param name="session">The tenant to write.</param>
    protected abstract void Configure(DbCommand command, TenantSession session);

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        _applied.Remove(connection);
        if (Resolve(connection, eventData.Context) is { } session)
            Apply(connection, transaction: null, session);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _applied.Remove(connection);
        if (Resolve(connection, eventData.Context) is { } session)
            await ApplyAsync(connection, transaction: null, session, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
        => _applied.Remove(connection);

    /// <inheritdoc />
    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        _applied.Remove(connection);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => Forget(eventData.Context);

    /// <inheritdoc />
    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Refresh(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Refresh(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Refresh(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private void Refresh(DbCommand command, CommandEventData eventData)
    {
        if (command.Connection is { } connection && Stale(connection, eventData) is { } session)
            Apply(connection, command.Transaction, session);
    }

    private async Task RefreshAsync(DbCommand command, CommandEventData eventData, CancellationToken ct)
    {
        if (command.Connection is { } connection && Stale(connection, eventData) is { } session)
            await ApplyAsync(connection, command.Transaction, session, ct).ConfigureAwait(false);
    }

    private TenantSession? Stale(DbConnection connection, CommandEventData eventData)
    {
        if (Resolve(connection, eventData.Context) is not { } session)
            return null;
        return _applied.TryGetValue(connection, out var box) && box.Value == session ? null : session;
    }

    private TenantSession? Resolve(DbConnection connection, Microsoft.EntityFrameworkCore.DbContext? context)
        => context is ModuleDbContext module && Supports(connection) ? module.TenantSession : null;

    // DbTransaction.Connection is null once a SqlTransaction completes, so go through the context.
    private void Forget(Microsoft.EntityFrameworkCore.DbContext? context)
    {
        if (context is not null)
            _applied.Remove(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(context.Database));
    }

    private void Apply(DbConnection connection, DbTransaction? transaction, TenantSession session)
    {
        using var command = Create(connection, transaction, session);
        command.ExecuteNonQuery();
        _applied.AddOrUpdate(connection, new StrongBox<TenantSession>(session));
    }

    private async Task ApplyAsync(DbConnection connection, DbTransaction? transaction, TenantSession session, CancellationToken ct)
    {
        await using var command = Create(connection, transaction, session);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _applied.AddOrUpdate(connection, new StrongBox<TenantSession>(session));
    }

    private DbCommand Create(DbConnection connection, DbTransaction? transaction, TenantSession session)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        Configure(command, session);
        return command;
    }
}
