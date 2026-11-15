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
public sealed class PostgreSqlTransport : IMessageTransport, IScheduledMessageTransport, IConcurrentDeliveryTransport, IAsyncDisposable
{
    private const string ErrorSuffix = "_error";
    private const string SkippedSuffix = "_skipped";

    private readonly PostgreSqlTransportOptions _options;
    private IReadOnlyDictionary<string, int> _limits = new Dictionary<string, int>(StringComparer.Ordinal);
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

    /// <inheritdoc />
    public void ConfigureConcurrency(IReadOnlyDictionary<string, int> limitsByEndpoint)
    {
        ArgumentNullException.ThrowIfNull(limitsByEndpoint);
        if (_started)
        {
            throw new InvalidOperationException("Concurrency must be configured before the PostgreSQL transport starts.");
        }

        _limits = limitsByEndpoint.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
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

        if (_options.ParkedRetention is { } retention)
        {
            _loops.Add(Task.Run(() => PurgeLoopAsync(retention, _stopping.Token), CancellationToken.None));
        }
    }

    /// <summary>Purges parked messages older than the retention now and then every <see cref="PostgreSqlTransportOptions.PurgeInterval"/>, until stopping.</summary>
    private async Task PurgeLoopAsync(TimeSpan retention, CancellationToken stopping)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    await PurgeParkedAsync(retention, stopping).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _options.PurgeFailed?.Invoke(ex);
                }

                await Task.Delay(_options.PurgeInterval, stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Stopping: the purge loop ends.
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

    /// <summary>
    /// The SQL that creates the queue table, its sequence and indexes, for a database administrator who applies the schema
    /// by hand (set <c>CreateSchema</c> to false then). It is the same script <c>StartAsync</c> runs.
    /// </summary>
    public static string SchemaScript(PostgreSqlTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return PostgreSqlQueueSql.CreateSchema(options.Schema, options.Table);
    }

    /// <summary>
    /// Deletes the parked messages (<c>_error</c> and <c>_skipped</c>) parked more than <paramref name="olderThan"/> ago and
    /// returns how many rows were removed (retention). Run it from a maintenance job; nothing purges on a timer.
    /// </summary>
    public async Task<int> PurgeParkedAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        if (olderThan < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(olderThan), "Must not be negative.");
        }

        await using var command = _dataSource.CreateCommand(PostgreSqlQueueSql.PurgeParked(_table));
        command.Parameters.AddWithValue("error_suffix", ErrorSuffix);
        command.Parameters.AddWithValue("skipped_suffix", SkippedSuffix);
        command.Parameters.AddWithValue("cutoff", DateTimeOffset.UtcNow - olderThan);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Claims batches for one endpoint and hands each row to a worker, at most <c>ConcurrentMessageLimit</c> at a time.
    /// Claiming goes on while the workers run: a partitioned row is claimable only when no earlier row of its partition
    /// exists (see <see cref="PostgreSqlQueueSql.Claim"/>), so the rows in flight never include two of one partition.
    /// The loop returns only after its workers have finished (or were aborted at the stop timeout).
    /// </summary>
    private async Task PollLoopAsync(string endpoint, CancellationToken stopping)
    {
        var limit = _limits.TryGetValue(endpoint, out var configured) ? configured : 1;
        using var slots = new SemaphoreSlim(limit, limit);
        var workers = new List<Task>();
        try
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
                    break;
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

                foreach (var item in batch)
                {
                    // Waits for a free worker. A claimed row keeps its lease while it waits.
                    await slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    workers.Add(HandleItemAsync(item, slots));
                }

                workers.RemoveAll(t => t.IsCompleted);
            }
        }
        finally
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    /// <summary>Runs one claimed row through the bus and settles it. Always releases its worker slot.</summary>
    private async Task HandleItemAsync(Claimed item, SemaphoreSlim slots)
    {
        try
        {
            if (_abort.IsCancellationRequested)
            {
                // Aborted at the stop timeout: give the row back right away.
                await ReleaseAsync(item).ConfigureAwait(false);
                return;
            }

            if (!PostgreSqlMessageCodec.TryDecode(
                    item.Queue, item.MessageId, item.MessageType, item.ConsumerKey, item.Body, item.Headers,
                    out var envelope, out var reason))
            {
                // Cannot be rebuilt: park the row in the error queue so an operator can read it.
                await ParkAsync(item, reason ?? "Message cannot be rebuilt.").ConfigureAwait(false);
                return;
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Database trouble while settling: the row keeps its lease and another host takes it when the lease expires
            // (at-least-once). Nothing may escape here, or the loop would stop waiting for its other workers.
            _ = exception;
        }
        finally
        {
            slots.Release();
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
