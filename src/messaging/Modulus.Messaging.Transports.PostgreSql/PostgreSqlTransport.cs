using System.Collections.Concurrent;
using Npgsql;
using NpgsqlTypes;

namespace Modulus.Messaging.Transports.PostgreSql;

/// <summary>
/// Delivers envelopes through one queue table. Each endpoint is a queue (the <c>queue</c> column); <c>name_error</c>
/// and <c>name_skipped</c> park failed and unhandled messages. A poll claims a batch with
/// <c>FOR UPDATE SKIP LOCKED</c> and a lease, so competing hosts never take the same row. A row is deleted only after
/// the bus has finished with it (RULE-02). A row whose host dies keeps its lease until it expires, then another host
/// receives it (at-least-once). Delayed redelivery is a row with a visibility time in the future.
/// </summary>
public sealed class PostgreSqlTransport : IMessageTransport, IScheduledMessageTransport, IAsyncDisposable
{
    private const string ErrorSuffix = "_error";
    private const string SkippedSuffix = "_skipped";

    private readonly PostgreSqlTransportOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _abort = new();
    private readonly ConcurrentBag<Task> _loops = [];
    private readonly string _table;
    private readonly string _sequence;
    private bool _started;
    private Func<MessageEnvelope, CancellationToken, Task>? _receive;

    public PostgreSqlTransport(PostgreSqlTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _table = $"\"{options.Schema}\".\"{options.Table}\"";
        _sequence = $"\"{options.Schema}\".\"{options.Table}_seq\"";
        _dataSource = NpgsqlDataSource.Create(options.ConnectionString);
    }

    public async Task Start(
        IReadOnlyCollection<string> endpoints,
        Func<MessageEnvelope, CancellationToken, Task> receive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(receive);
        if (_started)
        {
            throw new InvalidOperationException("The PostgreSQL transport is already started.");
        }

        _started = true;
        _receive = receive;
        if (_options.CreateSchema)
        {
            await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.CreateSchema(_options.Schema, _options.Table));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var endpoint in endpoints.Distinct(StringComparer.Ordinal))
        {
            _loops.Add(Task.Run(() => PollLoopAsync(endpoint, _stopping.Token), CancellationToken.None));
        }
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        // No new claims; a batch already claimed finishes, up to the stop timeout.
        await _stopping.CancelAsync().ConfigureAwait(false);
        var loops = Task.WhenAll(_loops.ToArray());
        var finished = await Task.WhenAny(loops, Task.Delay(_options.StopTimeout, cancellationToken)).ConfigureAwait(false);
        if (finished != loops)
        {
            // Stop timeout reached: abort the handlers. Their rows are released so another host takes them now.
            await _abort.CancelAsync().ConfigureAwait(false);
        }

        await loops.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Enqueue(envelope.Endpoint, envelope, TimeSpan.Zero, cancellationToken);
    }

    public Task Schedule(Guid scheduleToken, MessageEnvelope envelope, DateTimeOffset deliverAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Enqueue(envelope.Endpoint, envelope, TimeSpan.Zero, cancellationToken, scheduleToken, deliverAt);
    }

    public async Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.CancelScheduled(_table));
        command.Parameters.AddWithValue("schedule_id", scheduleToken);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Must not be negative.");
        }

        return Enqueue(envelope.Endpoint, envelope, delay, cancellationToken);
    }

    public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(exception);
        var error = new MessageEnvelope(
            envelope.MessageId,
            envelope.Endpoint,
            envelope.Message,
            WithHeaders(envelope.Headers,
                (PostgreSqlMessageCodec.ErrorTypeHeader, exception.GetType().FullName ?? exception.GetType().Name),
                (PostgreSqlMessageCodec.ErrorMessageHeader, exception.Message)),
            envelope.ConsumerKey);
        return Enqueue(envelope.Endpoint + ErrorSuffix, error, TimeSpan.Zero, cancellationToken);
    }

    public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return Enqueue(envelope.Endpoint + SkippedSuffix, envelope, TimeSpan.Zero, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Dispose();
        _abort.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task PollLoopAsync(string endpoint, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            IReadOnlyList<Claimed> batch;
            try
            {
                batch = await ClaimAsync(endpoint, stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The database is unreachable or restarting: keep polling; the bus has no other way to learn of it.
                await DelayQuietly(_options.PollInterval, stopping).ConfigureAwait(false);
                continue;
            }

            if (batch.Count == 0)
            {
                await DelayQuietly(_options.PollInterval, stopping).ConfigureAwait(false);
                continue;
            }

            await HandleBatchAsync(batch).ConfigureAwait(false);
        }
    }

    private async Task HandleBatchAsync(IReadOnlyList<Claimed> batch)
    {
        foreach (var item in batch)
        {
            if (_abort.IsCancellationRequested)
            {
                // Aborted at the stop timeout: give the rest of the batch back right away.
                await ReleaseAsync(item).ConfigureAwait(false);
                continue;
            }

            if (!PostgreSqlMessageCodec.TryDecode(
                    item.Queue, item.MessageId, item.MessageType, item.ConsumerKey, item.Body, item.Headers,
                    out var envelope, out var reason))
            {
                // Cannot be rebuilt: park the row in the error queue so an operator can read it.
                await ParkAsync(item, reason ?? "Message cannot be rebuilt.").ConfigureAwait(false);
                continue;
            }

            // The claim's sequence travels with the envelope, so a redelivery keeps the message's place in its partition (FR-14).
            var delivered = envelope! with
            {
                Headers = WithHeaders(envelope!.Headers, (MessagingHeaders.Sequence, item.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            };

            try
            {
                await _receive!(delivered, _abort.Token).ConfigureAwait(false);
                await DeleteAsync(item).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_abort.IsCancellationRequested)
            {
                await ReleaseAsync(item).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The bus reports consumer failures itself; anything escaping here is not for this message.
                // Release the row so it is delivered again rather than held until its lease expires.
                await ReleaseAsync(item).ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<Claimed>> ClaimAsync(string endpoint, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.Claim(_table));
        command.Parameters.AddWithValue("queue", endpoint);
        command.Parameters.AddWithValue("batch", _options.BatchSize);
        command.Parameters.AddWithValue("lease_ms", _options.LeaseDuration.TotalMilliseconds);
        command.Parameters.AddWithValue("token", Guid.NewGuid());

        var rows = new List<Claimed>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new Claimed(
                Id: reader.GetInt64(0),
                LockToken: reader.GetGuid(1),
                Queue: endpoint,
                MessageId: reader.GetGuid(2),
                MessageType: reader.GetString(3),
                ConsumerKey: await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                Body: reader.GetString(5),
                Headers: reader.GetString(6),
                Priority: reader.GetInt32(7),
                Sequence: reader.GetInt64(8)));
        }

        // The claim does not promise an order: handle the batch highest priority first, then in enqueue order (FR-13).
        rows.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Id.CompareTo(b.Id));
        return rows;
    }

    private async Task Enqueue(
        string queue,
        MessageEnvelope envelope,
        TimeSpan delay,
        CancellationToken cancellationToken,
        Guid? scheduleToken = null,
        DateTimeOffset? deliverAt = null)
    {
        var (messageType, consumerKey, body, headers) = PostgreSqlMessageCodec.Encode(envelope);
        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.Enqueue(_table, _sequence, scheduled: deliverAt is not null));
        command.Parameters.AddWithValue("queue", queue);
        command.Parameters.AddWithValue("message_id", envelope.MessageId);
        command.Parameters.AddWithValue("message_type", messageType);
        command.Parameters.AddWithValue("consumer_key", (object?)consumerKey ?? DBNull.Value);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("headers", NpgsqlDbType.Jsonb, headers);
        command.Parameters.AddWithValue("priority", PriorityOf(envelope));
        command.Parameters.AddWithValue("partition_key", (object?)PartitionKeyOf(envelope) ?? DBNull.Value);
        command.Parameters.AddWithValue("sequence", (object?)SequenceOf(envelope) ?? DBNull.Value);
        if (deliverAt is { } visibleAt)
        {
            command.Parameters.AddWithValue("visible_at", visibleAt);
            command.Parameters.AddWithValue("schedule_id", (object?)scheduleToken ?? DBNull.Value);
        }
        else
        {
            command.Parameters.AddWithValue("delay_ms", Math.Ceiling(delay.TotalMilliseconds));
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteAsync(Claimed item)
    {
        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.Delete(_table));
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.LockToken);
        // Zero rows means the lease expired and another host took the row: that message is handled twice (at-least-once).
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReleaseAsync(Claimed item)
    {
        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.Release(_table));
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.LockToken);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ParkAsync(Claimed item, string reason)
    {
        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.Park(_table));
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.LockToken);
        command.Parameters.AddWithValue("error_queue", item.Queue + ErrorSuffix);
        command.Parameters.AddWithValue("error_headers", NpgsqlDbType.Jsonb, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [PostgreSqlMessageCodec.ErrorMessageHeader] = reason,
        }));
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The partition the bus stamped on the envelope (FR-14); null for an unordered message.</summary>
    private static string? PartitionKeyOf(MessageEnvelope envelope) =>
        envelope.Headers.TryGetValue(MessagingHeaders.PartitionKey, out var key) ? key : null;

    /// <summary>The sequence a redelivered message kept (FR-14); null for a new message.</summary>
    private static long? SequenceOf(MessageEnvelope envelope) =>
        envelope.Headers.TryGetValue(MessagingHeaders.Sequence, out var text) && long.TryParse(text, out var sequence) ? sequence : null;

    /// <summary>The priority the bus stamped on the envelope (FR-13); 0 when absent or not a number.</summary>
    private static int PriorityOf(MessageEnvelope envelope) =>
        envelope.Headers.TryGetValue(MessagingHeaders.Priority, out var text) && int.TryParse(text, out var priority) ? priority : 0;

    private static Dictionary<string, string> WithHeaders(IReadOnlyDictionary<string, string> headers, params (string Key, string Value)[] extra)
    {
        var result = new Dictionary<string, string>(headers, StringComparer.Ordinal);
        foreach (var (key, value) in extra)
        {
            result[key] = value;
        }

        return result;
    }

    private static async Task DelayQuietly(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping: the loop checks the token and exits.
        }
    }

    private sealed record Claimed(
        long Id,
        Guid LockToken,
        string Queue,
        Guid MessageId,
        string MessageType,
        string? ConsumerKey,
        string Body,
        string Headers,
        int Priority,
        long Sequence);
}
