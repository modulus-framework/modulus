using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Modulus.Messaging.Outbox.Npgsql;

/// <summary>
/// Sends committed outbox rows. A round claims a batch under a lease (rows another dispatcher holds are skipped), sends
/// each to the transport and deletes it after its send. A crash between send and delete sends the message again, so
/// consumers must be idempotent (at-least-once, with the inbox for dedup).
/// </summary>
public static class NpgsqlOutboxDispatch
{
    /// <summary>Claims up to <see cref="NpgsqlOutboxOptions.BatchSize"/> rows, sends them and returns how many were sent.</summary>
    public static async Task<int> DispatchOnceAsync(
        NpgsqlDataSource dataSource,
        IMessageTransport transport,
        NpgsqlOutboxOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var table = options.Table;
        var token = Guid.NewGuid();

        var rows = new List<Row>();
        await using (var claim = dataSource.CreateCommand(NpgsqlOutboxSql.Claim(table)))
        {
            claim.Parameters.AddWithValue("batch", options.BatchSize);
            claim.Parameters.AddWithValue("token", token);
            claim.Parameters.AddWithValue("lease_ms", options.LeaseDuration.TotalMilliseconds);
            await using var reader = await claim.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new Row(
                    reader.GetInt64(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        var sent = 0;
        foreach (var row in rows)
        {
            if (!TryRebuild(row, out var envelope))
            {
                await ExecuteForRowAsync(dataSource, NpgsqlOutboxSql.Park(table), row.Id, token, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await transport.Send(envelope, cancellationToken).ConfigureAwait(false);
            await ExecuteForRowAsync(dataSource, NpgsqlOutboxSql.Delete(table), row.Id, token, cancellationToken).ConfigureAwait(false);
            sent++;
        }

        return sent;
    }

    private static async Task ExecuteForRowAsync(NpgsqlDataSource dataSource, string sql, long id, Guid token, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = id });
        command.Parameters.Add(new NpgsqlParameter("token", NpgsqlDbType.Uuid) { Value = token });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool TryRebuild(Row row, out MessageEnvelope envelope)
    {
        envelope = null!;
        if (Type.GetType(row.MessageType) is not { } type)
        {
            return false;
        }

        try
        {
            var message = JsonSerializer.Deserialize(row.Body, type, NpgsqlOutboxJson.Options);
            if (message is null)
            {
                return false;
            }

            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Headers, NpgsqlOutboxJson.Options) ?? [];
            envelope = new MessageEnvelope(row.MessageId, row.Endpoint, message, headers, row.ConsumerKey);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record Row(long Id, Guid MessageId, string Endpoint, string? ConsumerKey, string MessageType, string Body, string Headers);
}
