using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>Writes outgoing messages into the scope's transaction, so they commit or roll back with the caller's work.</summary>
internal sealed class NpgsqlMessageOutbox(INpgsqlUnitOfWork unitOfWork, string table) : IMessageOutbox
{
    public Task Add(MessageEnvelope envelope, CancellationToken cancellationToken) =>
        Insert(envelope, deliverAt: null, scheduleToken: null, cancellationToken);

    public Task AddScheduled(MessageEnvelope envelope, DateTimeOffset deliverAt, Guid scheduleToken, CancellationToken cancellationToken) =>
        Insert(envelope, deliverAt, scheduleToken, cancellationToken);

    public async Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken)
    {
        var (connection, transaction) = Current();
        await using var command = new NpgsqlCommand(NpgsqlOutboxSql.CancelScheduled(table), connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("schedule_token", NpgsqlDbType.Uuid) { Value = scheduleToken });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private async Task Insert(MessageEnvelope envelope, DateTimeOffset? deliverAt, Guid? scheduleToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var (connection, transaction) = Current();
        var type = envelope.Message.GetType();
        await using var command = new NpgsqlCommand(NpgsqlOutboxSql.Insert(table), connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("message_id", NpgsqlDbType.Uuid) { Value = envelope.MessageId });
        command.Parameters.Add(new NpgsqlParameter("endpoint", NpgsqlDbType.Text) { Value = envelope.Endpoint });
        command.Parameters.Add(new NpgsqlParameter("consumer_key", NpgsqlDbType.Text) { Value = (object?)envelope.ConsumerKey ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("message_type", NpgsqlDbType.Text) { Value = type.AssemblyQualifiedName ?? type.FullName ?? type.Name });
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Text) { Value = JsonSerializer.Serialize(envelope.Message, type, NpgsqlOutboxJson.Options) });
        command.Parameters.Add(new NpgsqlParameter("headers", NpgsqlDbType.Text)
        {
            Value = JsonSerializer.Serialize(new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal), NpgsqlOutboxJson.Options),
        });
        command.Parameters.Add(new NpgsqlParameter("deliver_at", NpgsqlDbType.TimestampTz) { Value = (object?)deliverAt?.UtcDateTime ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("schedule_token", NpgsqlDbType.Uuid) { Value = (object?)scheduleToken ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private (NpgsqlConnection Connection, NpgsqlTransaction Transaction) Current() =>
        unitOfWork.Connection is { } connection && unitOfWork.Transaction is { } transaction
            ? (connection, transaction)
            : throw new InvalidOperationException(
                "Begin a transaction on INpgsqlUnitOfWork before publishing or sending: the outbox row must commit with it.");
}

/// <summary>The JSON options outbox bodies and headers are written and read with.</summary>
internal static class NpgsqlOutboxJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
