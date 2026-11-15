namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>SQL of the Npgsql outbox table. The table name is checked by <see cref="NpgsqlOutboxOptions"/> before it gets here.</summary>
internal static class NpgsqlOutboxSql
{
    public static string Schema(string table) => $"""
        CREATE TABLE IF NOT EXISTS {table} (
            id bigserial PRIMARY KEY,
            message_id uuid NOT NULL,
            endpoint text NOT NULL,
            consumer_key text NULL,
            message_type text NOT NULL,
            body text NOT NULL,
            headers text NOT NULL,
            enqueued_at timestamptz NOT NULL DEFAULT now(),
            deliver_at timestamptz NULL,
            schedule_token uuid NULL,
            lock_token uuid NULL,
            locked_until timestamptz NULL,
            parked boolean NOT NULL DEFAULT false
        );
        CREATE INDEX IF NOT EXISTS {table}_ready_idx ON {table} (enqueued_at, id) WHERE NOT parked;
        CREATE INDEX IF NOT EXISTS {table}_schedule_idx ON {table} (schedule_token) WHERE schedule_token IS NOT NULL;
        """;

    public static string Insert(string table) =>
        $"""
        INSERT INTO {table} (message_id, endpoint, consumer_key, message_type, body, headers, deliver_at, schedule_token)
        VALUES (@message_id, @endpoint, @consumer_key, @message_type, @body, @headers, @deliver_at, @schedule_token)
        """;

    public static string CancelScheduled(string table) =>
        $"DELETE FROM {table} WHERE schedule_token = @schedule_token AND lock_token IS NULL";

    /// <summary>Claims up to @batch ready rows under a lease, skipping rows another dispatcher holds (FOR UPDATE SKIP LOCKED).</summary>
    public static string Claim(string table) => $"""
        WITH claimed AS (
            SELECT id FROM {table}
            WHERE NOT parked
              AND (locked_until IS NULL OR locked_until < now())
              AND (deliver_at IS NULL OR deliver_at <= now())
            ORDER BY enqueued_at, id
            LIMIT @batch
            FOR UPDATE SKIP LOCKED)
        UPDATE {table} AS o
        SET lock_token = @token,
            locked_until = now() + (@lease_ms * interval '1 millisecond')
        FROM claimed
        WHERE o.id = claimed.id
        RETURNING o.id, o.message_id, o.endpoint, o.consumer_key, o.message_type, o.body, o.headers
        """;

    public static string Park(string table) =>
        $"UPDATE {table} SET parked = true, lock_token = NULL, locked_until = NULL WHERE id = @id AND lock_token = @token";

    public static string Delete(string table) =>
        $"DELETE FROM {table} WHERE id = @id AND lock_token = @token";
}
