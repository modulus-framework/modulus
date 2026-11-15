using Npgsql;

namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>
/// The PostgreSQL transaction one scope shares: the application's writes, the outbox rows of its publishes and, in a
/// consumer, the inbox record. Begin it before the work, commit when it is done. Outgoing messages commit with it and
/// are sent only after the commit (RULE-01); a rollback sends nothing.
/// </summary>
public interface INpgsqlUnitOfWork
{
    /// <summary>The open transaction, or null before <see cref="BeginAsync"/>.</summary>
    NpgsqlTransaction? Transaction { get; }

    /// <summary>The connection the transaction runs on, or null before <see cref="BeginAsync"/>.</summary>
    NpgsqlConnection? Connection { get; }

    /// <summary>Opens a connection and starts the transaction. Throws when one is already open in this scope.</summary>
    Task<NpgsqlTransaction> BeginAsync(CancellationToken cancellationToken);

    /// <summary>Commits the transaction and releases the connection.</summary>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls the transaction back and releases the connection. Does nothing when none is open.</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}
