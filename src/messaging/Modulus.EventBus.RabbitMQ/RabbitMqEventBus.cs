namespace Modulus.EventBus.RabbitMQ;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;
using global::RabbitMQ.Client;

/// <summary>
/// <see cref="IModuleBus"/> implementation that publishes integration events
/// to a RabbitMQ topic exchange.  Each publish creates a short-lived channel
/// from a shared <see cref="IConnection"/> (channels are not thread-safe in
/// RabbitMQ.Client 7.x).
/// </summary>
internal sealed class RabbitMqEventBus : IModuleBus, IAsyncDisposable
{
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqEventBus(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventBus> logger)
    {
        _opts   = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var connection = await EnsureConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        var routingKey = typeof(TEvent).FullName!;

        var envelope = new IntegrationEventEnvelope
        {
            EventId    = @event.EventId,
            OccurredAt = @event.OccurredAt,
            TypeName   = typeof(TEvent).AssemblyQualifiedName!,
            RoutingKey = routingKey,
            Payload    = JsonSerializer.Serialize(@event, typeof(TEvent)),
        };

        var body = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(envelope));

        await channel.BasicPublishAsync(
            exchange:    _opts.ExchangeName,
            routingKey:  routingKey,
            mandatory:   true,
            body:        body,
            cancellationToken: ct);

        _logger.LogDebug(
            "Published {EventType} ({EventId}) to exchange '{Exchange}' [{RoutingKey}]",
            typeof(TEvent).Name, envelope.EventId,
            _opts.ExchangeName, routingKey);
    }

    private async Task<IConnection> EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return _connection;

        await _connectionGate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;

            _connection?.DisposeAsync().AsTask().Wait(ct);

            var factory = new ConnectionFactory
            {
                HostName    = _opts.HostName,
                Port        = _opts.Port,
                UserName    = _opts.UserName,
                Password    = _opts.Password,
                VirtualHost = _opts.VirtualHost,
            };

            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connectionGate.Dispose();
    }
}
