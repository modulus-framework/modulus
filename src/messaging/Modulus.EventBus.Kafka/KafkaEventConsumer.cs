namespace Modulus.EventBus.Kafka;

using System.Text.Json;
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

    public KafkaEventConsumer(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventConsumer> logger,
        IServiceScopeFactory scopeFactory,
        IIntegrationEventRegistry registry)
    {
        _opts = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _registry = registry;
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
            // Force manual commits regardless of config. Auto-commit would
            // advance the offset before a handler finishes, silently dropping
            // failed messages. We commit explicitly after successful dispatch.
            EnableAutoCommit = false,
            AutoCommitIntervalMs = _opts.AutoCommitIntervalMs,
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

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(ct);

                if (result?.Message?.Value is null)
                {
                    consumer.Commit(result);
                    continue;
                }

                var envelope = JsonSerializer
                    .Deserialize<IntegrationEventEnvelope>(result.Message.Value);

                if (envelope is null)
                {
                    consumer.Commit(result);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<IntegrationEventDispatcher>();

                var handled = await dispatcher.DispatchAsync(envelope, ct);

                if (!handled)
                    _logger.LogDebug(
                        "No handler for routing key '{RoutingKey}' from topic '{Topic}'",
                        envelope.RoutingKey, result.Topic);

                // Success — commit the offset so Kafka advances past this
                // message. We never commit on failure so the broker redelivers.
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
                // Dispatch failed — do NOT commit so Kafka redelivers the
                // message. Log and continue the loop; the broker re-sends.
                _logger.LogError(ex,
                    "Error processing Kafka message; not committing — message will be redelivered.");
            }
        }

        consumer.Close();
    }

    private static AutoOffsetReset ParseAutoOffsetReset(string value) =>
        value.ToLowerInvariant() switch
        {
            "earliest" => AutoOffsetReset.Earliest,
            "latest" => AutoOffsetReset.Latest,
            _ => AutoOffsetReset.Earliest,
        };
}
