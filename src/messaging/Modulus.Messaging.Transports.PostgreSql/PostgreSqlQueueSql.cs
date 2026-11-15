namespace Modulus.Messaging.Transports.PostgreSql;

/// <summary>The statements of the queue table. Table and schema names are validated identifiers, so they are safe to inline.</summary>
internal static class PostgreSqlQueueSql
{
    public static string CreateSchema(string schema, string table)
    {
        var qualified = $"\"{schema}\".\"{table}\"";
        var sequence = $"\"{schema}\".\"{table}_seq\"";
        return $"""
            CREATE SEQUENCE IF NOT EXISTS {sequence};
            CREATE TABLE IF NOT EXISTS {qualified} (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                queue text NOT NULL,
                message_id uuid NOT NULL,
                message_type text NOT NULL,
                consumer_key text NULL,
                body text NOT NULL,
                headers jsonb NOT NULL,
                visible_at timestamptz NOT NULL DEFAULT now(),
                locked_until timestamptz NULL,
                lock_token uuid NULL,
                delivery_count integer NOT NULL DEFAULT 0,
                enqueued_at timestamptz NOT NULL DEFAULT now(),
                schedule_id uuid NULL,
                priority integer NOT NULL DEFAULT 0,
                partition_key text NULL,
                sequence bigint NOT NULL DEFAULT nextval('{sequence}')
            );
            CREATE INDEX IF NOT EXISTS "{table}_ready_idx" ON {qualified} (queue, visible_at, id);
            CREATE INDEX IF NOT EXISTS "{table}_schedule_idx" ON {qualified} (schedule_id) WHERE schedule_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "{table}_partition_idx" ON {qualified} (queue, partition_key, sequence) WHERE partition_key IS NOT NULL;
            """;
    }

    /// <summary>
    /// Claims up to @batch rows of @queue that are visible and unlocked. A partitioned row (partition_key set) is claimable
    /// only when no earlier row of its partition exists (FR-14): one row per partition at a time, so competing hosts
    /// never run two messages of one partition together, and a retried row (which keeps its sequence) holds back the
    /// rows sent after it. Locked rows are skipped, so competing hosts never share a row.
    /// </summary>
    public static string Claim(string table) => $"""
        WITH claimed AS (
            SELECT q.id FROM {table} AS q
            WHERE q.queue = @queue
              AND q.visible_at <= now()
              AND (q.locked_until IS NULL OR q.locked_until < now())
              AND (q.partition_key IS NULL OR NOT EXISTS (
                    SELECT 1 FROM {table} AS p
                    WHERE p.queue = q.queue
                      AND p.partition_key = q.partition_key
                      AND p.sequence < q.sequence))
            ORDER BY q.priority DESC, q.id
            LIMIT @batch
            FOR UPDATE OF q SKIP LOCKED
        )
        UPDATE {table} AS q
        SET lock_token = @token,
            locked_until = now() + (@lease_ms * interval '1 millisecond'),
            delivery_count = q.delivery_count + 1
        FROM claimed
        WHERE q.id = claimed.id
        RETURNING q.id, q.lock_token, q.message_id, q.message_type, q.consumer_key, q.body, q.headers::text, q.priority, q.sequence
        """;

    /// <summary>
    /// Inserts a row. A delayed redelivery becomes visible after @delay_ms from the database clock; a scheduled row
    /// (<paramref name="scheduled"/>) becomes visible at @visible_at and carries @schedule_id for cancellation. A scheduled
    /// row has no partition (its send time is not its order), so it never holds back other messages. @sequence keeps a
    /// redelivered row's place; a new row takes the next value.
    /// </summary>
    public static string Enqueue(string table, string sequence, bool scheduled) => scheduled
        ? $"""
        INSERT INTO {table} (queue, message_id, message_type, consumer_key, body, headers, visible_at, schedule_id, priority, partition_key, sequence)
        VALUES (@queue, @message_id, @message_type, @consumer_key, @body, @headers::jsonb, @visible_at, @schedule_id, @priority, NULL,
                COALESCE(@sequence, nextval('{sequence}')))
        """
        : $"""
        INSERT INTO {table} (queue, message_id, message_type, consumer_key, body, headers, visible_at, priority, partition_key, sequence)
        VALUES (@queue, @message_id, @message_type, @consumer_key, @body, @headers::jsonb,
                now() + (@delay_ms * interval '1 millisecond'), @priority, @partition_key,
                COALESCE(@sequence, nextval('{sequence}')))
        """;

    /// <summary>Deletes the rows of a schedule that no host has claimed; a claimed row (lock_token set) is already on its way to a consumer.</summary>
    public static string CancelScheduled(string table) =>
        $"DELETE FROM {table} WHERE schedule_id = @schedule_id AND lock_token IS NULL";

    public static string Delete(string table) => $"DELETE FROM {table} WHERE id = @id AND lock_token = @token";

    public static string Release(string table) =>
        $"UPDATE {table} SET lock_token = NULL, locked_until = NULL WHERE id = @id AND lock_token = @token";

    public static string Park(string table) => $"""
        UPDATE {table}
        SET queue = @error_queue,
            lock_token = NULL,
            locked_until = NULL,
            visible_at = now(),
            headers = headers || @error_headers::jsonb
        WHERE id = @id AND lock_token = @token
        """;
}
