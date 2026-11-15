using Npgsql;

namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>
/// The scope's PostgreSQL unit of work. It is also the consume attempt's unit of work (<see cref="IConsumeUnitOfWork"/>):
/// the bus begins it before the consumer, commits it after a success and rolls it back after a failure, so the inbox
/// record, the consumer's writes and its outgoing messages commit or roll back together (RULE-06).
/// </summary>
public sealed class NpgsqlUnitOfWork(NpgsqlDataSource dataSource) : INpgsqlUnitOfWork, IConsumeUnitOfWork, IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    /// <inheritdoc/>
    public NpgsqlTransaction? Transaction => _transaction;

    /// <inheritdoc/>
    public NpgsqlConnection? Connection => _connection;

    /// <inheritdoc/>
    public async Task<NpgsqlTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open in this scope.");
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
        return _transaction;
    }

    /// <inheritdoc/>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException("No transaction is open in this scope.");
        try
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
    }

    Task IConsumeUnitOfWork.Begin(CancellationToken cancellationToken) => BeginAsync(cancellationToken);

    Task IConsumeUnitOfWork.Commit(CancellationToken cancellationToken) => CommitAsync(cancellationToken);

    Task IConsumeUnitOfWork.Rollback(CancellationToken cancellationToken) => RollbackAsync(cancellationToken);

    private bool _disposed;

    /// <summary>An open transaction that was never committed is rolled back.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReleaseAsync()
    {
        var transaction = _transaction;
        var connection = _connection;
        _transaction = null;
        _connection = null;
        if (transaction is not null)
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
