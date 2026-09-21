namespace Modulus.EventBus.Kafka;

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Events;
using Modulus.Events.Abstractions;

/// <summary>
/// Background service that consumes integration events from Kafka topics and
/// dispatches them to registered handlers via <see cref="IntegrationEventDispatcher"/>.
/// </summary>
internal sealed class KafkaEventConsumer : BackgroundService
{
    private readonly KafkaOptions _opts;
    private readonly ILogger<KafkaEventConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventRegistry _registry;
    private readonly IMessageSerializer _serializer;

    public KafkaEventConsumer(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventConsumer> logger,
        IServiceScopeFactory scopeFactory,
        IIntegrationEventRegistry registry,
        IMessageSerializer serializer)
    {
        _opts = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _serializer = serializer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _opts.BootstrapServers,
            GroupId = _opts.GroupId,
            AutoOffsetReset = ParseAutoOffsetReset(_opts.AutoOffsetReset),
            // Force manual commits. Auto-commit would advance the offset
            // before a handler finishes, silently dropping failed messages.
            // We commit explicitly after successful dispatch.
            EnableAutoCommit = false,
        };

        KafkaEventBus.ApplySecurity(config, _opts);

        var topics = _registry.GetRoutingKeys()
            .Select(key => $"{_opts.TopicPrefix}.{key}")
            .ToList();

        if (topics.Count == 0)
        {
            _logger.LogWarning(
                "Kafka consumer not started — no integration events registered");
            return;
        }

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) =>
                _logger.LogError("Kafka error: {Reason} ({Code})", e.Reason, e.Code))
            .Build();

        consumer.Subscribe(topics);

        _logger.LogInformation(
            "Kafka consumer started — group '{Group}', {TopicCount} topic(s): {Topics}",
            _opts.GroupId, topics.Count, string.Join(", ", topics));

        // Bounded per-message redelivery bookkeeping: keyed by the exact
        // position of the failing message so retries across partitions or
        // rebalances never bleed into unrelated messages.
        var failureAttempts =
            new Dictionary<(string Topic, int Partition, long Offset), int>();

        while (!ct.IsCancellationRequested)
        {
            // Reset per iteration: exceptions thrown by a later Consume() must
            // never be attributed to the previous message's offset.
            ConsumeResult<string, string>? result = null;

            try
            {
                result = consumer.Consume(ct);

                if (result?.Message?.Value is null)
                {
                    if (result is not null)
                        consumer.Commit(result);
                    continue;
                }

                // Deserialise with the shared IMessageSerializer so the
                // envelope is read with the same options the producer wrote
                // it with (camelCase + string enums). Raw JsonSerializer
                // defaults are case-sensitive and would yield an
                // all-defaults envelope.
                var envelope = (IntegrationEventEnvelope?)_serializer
                    .Deserialize(result.Message.Value, typeof(IntegrationEventEnvelope));

                if (envelope is null)
                {
                    // Malformed / undeserialisable message — poison. Produce to DLQ
                    // (when enabled) then commit past; redelivery cannot fix it.
                    _logger.LogError(
                        "Failed to deserialise Kafka envelope from {Topic}[{Partition}]@{Offset}; routing to DLQ",
                        result.Topic, result.Partition.Value, result.Offset.Value);
                    await DeadLetterAsync(result.Topic, result, result.Message.Value, "deserialization-failed", ct);
                    consumer.Commit(result);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<IntegrationEventDispatcher>();

                // Restore tenant/correlation carried on the envelope around
                // handler invocation — otherwise handlers run in host scope
                // where tenant query filters match everything and writes stamp
                // TenantId as empty.
                bool handled;
                using (EnvelopeAmbientScope.Restore(envelope, scope.ServiceProvider))
                    handled = await dispatcher.DispatchAsync(envelope, ct);

                if (!handled)
                {
                    // No handler registered — produce to DLQ (when enabled) then
                    // commit past; do not silently drop.
                    _logger.LogWarning(
                        "No handler for routing key '{RoutingKey}' from topic '{Topic}' [{Partition}]@{Offset}; routing to DLQ",
                        envelope.RoutingKey, result.Topic, result.Partition.Value, result.Offset.Value);
                    await DeadLetterAsync(result.Topic, result, result.Message.Value, "unhandled-routing-key", ct);
                }

                failureAttempts.Remove(
                    (result.Topic, result.Partition.Value, result.Offset.Value));

                // Success — commit the offset so Kafka advances past this message.
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error; continuing");
            }
            catch (Exception ex)
            {
                if (result is null)
                    throw;

                await RedeliverOrParkAsync(consumer, result, failureAttempts, ex, ct);
            }
        }

        consumer.Close();
    }

    /// <summary>
    /// Handles a failed dispatch by seeking back to the failed offset (so the
    /// broker genuinely redelivers) up to <see cref="KafkaOptions.MaxDeliveryAttempts"/>
    /// times with capped exponential back-off, then committing past the
    /// poisoned message instead of blocking the partition forever.
    /// </summary>
    /// <remarks>
    /// Merely skipping the commit does NOT cause redelivery: the next
    /// successful <c>Commit(result)</c> implicitly advances past every earlier
    /// uncommitted offset, silently dropping the failed event. Only a seek
    /// makes the next <c>Consume()</c> return the failed message again. A seek
    /// failure escapes to the host: with default settings that stops the host,
    /// and a restart rejoins the group and resumes from the last committed
    /// offset — i.e. the failed message itself.
    /// </remarks>
    private async Task RedeliverOrParkAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        Dictionary<(string Topic, int Partition, long Offset), int> failureAttempts,
        Exception failure,
        CancellationToken ct)
    {
        // Transient inbox contention: seek back without consuming retry budget.
        if (IsDeferral(failure))
        {
            _logger.LogDebug(failure,
                "Kafka message {Topic}[{Partition}]@{Offset} deferred (inbox contention); seeking for redelivery",
                result.Topic, result.Partition.Value, result.Offset.Value);
            consumer.Seek(new TopicPartitionOffset(result.TopicPartition, result.Offset));
            await Task.Delay(Math.Min(1000, _opts.RedeliveryMaxBackoffMs), ct);
            return;
        }

        var key = (result.Topic, result.Partition.Value, result.Offset.Value);
        var attempt = failureAttempts.TryGetValue(key, out var seen) ? seen + 1 : 1;
        failureAttempts[key] = attempt;

        // Bound the map with oldest-entry eviction (failure storms must not grow it).
        if (failureAttempts.Count > Math.Max(1000, _opts.MaxTrackedFailures))
        {
            using var enumerator = failureAttempts.Keys.GetEnumerator();
            if (enumerator.MoveNext())
                failureAttempts.Remove(enumerator.Current);
        }

        if (attempt >= Math.Max(1, _opts.MaxDeliveryAttempts))
        {
            failureAttempts.Remove(key);
            _logger.LogError(failure,
                "Kafka message {Topic}[{Partition}]@{Offset} failed after {Attempts} delivery attempts; routing to DLQ",
                result.Topic, result.Partition.Value, result.Offset.Value, attempt);
            var produced = await DeadLetterAsync(result.Topic, result, result.Message.Value,
                $"max-attempts:{attempt}:{failure.GetType().Name}:{failure.Message}", ct);
            if (!produced)
            {
                // DLQ produce failed — do NOT commit; seek back so the message
                // is retried and the DLQ write re-attempted.
                consumer.Seek(new TopicPartitionOffset(result.TopicPartition, result.Offset));
                return;
            }
            try
            {
                consumer.Commit(result);
            }
            catch (KafkaException commitEx)
            {
                _logger.LogError(commitEx,
                    "Could not commit past poisoned Kafka message {Topic}[{Partition}]@{Offset}; the partition will be reprocessed from the last committed offset",
                    result.Topic, result.Partition.Value, result.Offset.Value);
            }
            return;
        }

        consumer.Seek(new TopicPartitionOffset(result.TopicPartition, result.Offset));

        var backoffMs = Math.Min(
            _opts.RedeliveryMaxBackoffMs,
            100 * (int)Math.Pow(2, Math.Min(attempt - 1, 5)));
        await Task.Delay(backoffMs, ct);
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value) =>
        value.ToLowerInvariant() switch
        {
            "earliest" => AutoOffsetReset.Earliest,
            "latest" => AutoOffsetReset.Latest,
            _ => AutoOffsetReset.Earliest,
        };

    private static bool IsDeferral(Exception ex) =>
        ex.GetType().FullName == "Modulus.Inbox.Abstractions.InboxDeferralException" ||
        (ex.InnerException is not null &&
         ex.InnerException.GetType().FullName == "Modulus.Inbox.Abstractions.InboxDeferralException");

    /// <summary>
    /// Best-effort produce of the original payload to the DLQ topic with source
    /// headers. Returns false when disabled or the produce fails (caller must
    /// not commit past the message then).
    /// </summary>
    private async Task<bool> DeadLetterAsync(
        string sourceTopic,
        ConsumeResult<string, string> result,
        string? payload,
        string reason,
        CancellationToken ct)
    {
        if (!_opts.EnableDlq)
            return true;

        try
        {
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = _opts.BootstrapServers,
                Acks = ParseAcks(_opts.Acks),
                MessageSendMaxRetries = Math.Max(1, _opts.MessageSendMaxRetries),
            };
            KafkaEventBus.ApplySecurity(producerConfig, _opts);

            using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
            await producer.ProduceAsync(
                sourceTopic + _opts.DeadLetterTopicSuffix,
                new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = payload ?? result.Message.Value,
                    Headers = new Headers
                    {
                        { "mod-dlq-source-topic", System.Text.Encoding.UTF8.GetBytes(sourceTopic) },
                        { "mod-dlq-partition", BitConverter.GetBytes(result.Partition.Value) },
                        { "mod-dlq-offset", BitConverter.GetBytes(result.Offset.Value) },
                        { "mod-dlq-reason", System.Text.Encoding.UTF8.GetBytes(reason) },
                    },
                },
                ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to produce DLQ message for {Topic}[{Partition}]@{Offset}; will retry",
                result.Topic, result.Partition.Value, result.Offset.Value);
            return false;
        }
    }

    private static Acks ParseAcks(string value) =>
        value.ToLowerInvariant() switch
        {
            "all" or "-1" => Acks.All,
            "1" or "leader" => Acks.Leader,
            "0" or "none" => Acks.None,
            _ => Acks.All,
        };
}
