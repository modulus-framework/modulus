using Modulus.Messaging.Outbox.Npgsql;
using Npgsql;
using NpgsqlTypes;

namespace Modulus.Messaging.Inbox.Npgsql;

/// <summary>
/// Records that a consumer handled a message, inside the consume attempt's transaction. A second record of the same
/// message and consumer does nothing (ON CONFLICT DO NOTHING): the insert waits for a concurrent attempt to commit or roll
/// back, so two consumers racing on one message handle it once (RULE-04).
/// </summary>
internal sealed class NpgsqlMessageInbox(INpgsqlUnitOfWork unitOfWork, string table) : IMessageInbox
{
    public async Task<bool> Claim(Guid messageId, string consumerKey, CancellationToken cancellationToken)
    {
        var (connection, transaction) = unitOfWork.Connection is { } c && unitOfWork.Transaction is { } t
            ? (c, t)
            : throw new InvalidOperationException("The inbox records a message inside the consume transaction; none is open.");
        await using var command = new NpgsqlCommand(NpgsqlInboxSql.Claim(table), connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("message_id", NpgsqlDbType.Uuid) { Value = messageId });
        command.Parameters.Add(new NpgsqlParameter("consumer_key", NpgsqlDbType.Text) { Value = consumerKey });
        command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text) { Value = (object?)MessageTenant.Current ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }
}

internal static class NpgsqlInboxSql
{
    public static string Schema(string table) => $"""
        CREATE TABLE IF NOT EXISTS {table} (
            message_id uuid NOT NULL,
            consumer_key text NOT NULL,
            received_at timestamptz NOT NULL DEFAULT now(),
            tenant_id text NULL,
            PRIMARY KEY (message_id, consumer_key)
        );
        CREATE INDEX IF NOT EXISTS {table}_received_idx ON {table} (received_at);
        CREATE INDEX IF NOT EXISTS {table}_tenant_idx ON {table} (tenant_id);
        """;

    public static string Claim(string table) =>
        $"INSERT INTO {table} (message_id, consumer_key, tenant_id) VALUES (@message_id, @consumer_key, @tenant_id) ON CONFLICT (message_id, consumer_key) DO NOTHING";

    public static string Purge(string table) =>
        $"DELETE FROM {table} WHERE received_at < now() - (@window_ms * interval '1 millisecond')";
}
