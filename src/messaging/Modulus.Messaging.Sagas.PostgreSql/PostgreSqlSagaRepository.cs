using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace Modulus.Messaging.Sagas.PostgreSql;

/// <summary>
/// Stores saga instances as JSONB in PostgreSQL. Each write checks the version in the same statement (an INSERT that
/// conflicts, or an UPDATE / DELETE WHERE version = the one read), so two writers cannot both succeed on one version.
/// </summary>
public sealed partial class PostgreSqlSagaRepository<TSaga> : ISagaRepository<TSaga>
    where TSaga : SagaInstance
{
    private static readonly string SagaType = typeof(TSaga).FullName ?? typeof(TSaga).Name;

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _table;

    /// <summary>Creates a repository over one saga table.</summary>
    /// <param name="dataSource">The database that holds the table.</param>
    /// <param name="table">Table name, a plain SQL identifier. Create it with <see cref="SchemaScript"/>.</param>
    public PostgreSqlSagaRepository(NpgsqlDataSource dataSource, string table = "messaging_sagas")
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        if (!Identifier().IsMatch(table))
        {
            throw new ArgumentException("The table must be a plain SQL identifier (letters, digits, underscores).", nameof(table));
        }

        _dataSource = dataSource;
        _table = table;
    }

    /// <summary>The SQL that creates the saga table (one row per instance, keyed by saga type and correlation id).</summary>
    public static string SchemaScript(string table = "messaging_sagas")
    {
        if (!Identifier().IsMatch(table))
        {
            throw new ArgumentException("The table must be a plain SQL identifier (letters, digits, underscores).", nameof(table));
        }

        return $"""
            CREATE TABLE IF NOT EXISTS "{table}" (
                saga_type text NOT NULL,
                correlation_id uuid NOT NULL,
                version bigint NOT NULL,
                data jsonb NOT NULL,
                PRIMARY KEY (saga_type, correlation_id)
            );
            """;
    }

    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(
            $"SELECT data::text, version FROM \"{_table}\" WHERE saga_type = @saga_type AND correlation_id = @correlation_id");
        command.Parameters.AddWithValue("saga_type", SagaType);
        command.Parameters.AddWithValue("correlation_id", correlationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var saga = JsonSerializer.Deserialize<TSaga>(reader.GetString(0));
        if (saga is not null)
        {
            // The version column is authoritative: the JSON is written with the version it was saved under.
            saga.Version = reader.GetInt64(1);
        }

        return saga;
    }

    public async Task SaveAsync(TSaga saga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saga);
        var expected = saga.Version;
        var next = expected + 1;
        saga.Version = next;
        var data = JsonSerializer.Serialize(saga);

        if (expected == 0)
        {
            await using var insert = _dataSource.CreateCommand(
                $"INSERT INTO \"{_table}\" (saga_type, correlation_id, version, data) VALUES (@saga_type, @correlation_id, @version, @data) ON CONFLICT DO NOTHING");
            Parameters(insert, saga.CorrelationId, next, data);
            if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                saga.Version = expected;
                throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
            }

            return;
        }

        await using var update = _dataSource.CreateCommand(
            $"UPDATE \"{_table}\" SET version = @version, data = @data WHERE saga_type = @saga_type AND correlation_id = @correlation_id AND version = @expected");
        Parameters(update, saga.CorrelationId, next, data);
        update.Parameters.AddWithValue("expected", expected);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            saga.Version = expected;
            throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
        }
    }

    public async Task DeleteAsync(TSaga saga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saga);
        if (saga.Version == 0)
        {
            // Never stored: a saga that initiates and completes in one step leaves nothing behind.
            return;
        }

        await using var command = _dataSource.CreateCommand(
            $"DELETE FROM \"{_table}\" WHERE saga_type = @saga_type AND correlation_id = @correlation_id AND version = @version");
        command.Parameters.AddWithValue("saga_type", SagaType);
        command.Parameters.AddWithValue("correlation_id", saga.CorrelationId);
        command.Parameters.AddWithValue("version", saga.Version);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
        }
    }

    private static void Parameters(NpgsqlCommand command, Guid correlationId, long version, string data)
    {
        command.Parameters.AddWithValue("saga_type", SagaType);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = data });
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
