namespace Modulus.EventBus.RabbitMQ;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqEventBus(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventBus> logger,
        IServiceScopeFactory scopeFactory)
    {
        _opts = options.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var connection = await EnsureConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        // Stable transport name (attribute or assembly-independent FullName).
        var routingKey = IntegrationEventNaming.GetName(typeof(TEvent));
        var (tenantId, correlationId) = ReadAmbientContext();

        var envelope = new IntegrationEventEnvelope
        {
            EventId = @event.EventId,
            OccurredAt = @event.OccurredAt,
            TypeName = routingKey,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(@event, typeof(TEvent)),
            TenantId = tenantId,
            CorrelationId = correlationId,
        };

        var body = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(envelope));

        await channel.BasicPublishAsync(
            exchange: _opts.ExchangeName,
            routingKey: routingKey,
            mandatory: true,
            body: body,
            cancellationToken: ct);

        _logger.LogDebug(
            "Published {EventType} ({EventId}) to exchange '{Exchange}' [{RoutingKey}]",
            typeof(TEvent).Name, envelope.EventId,
            _opts.ExchangeName, routingKey);
    }

    /// <summary>
    /// Reads the ambient tenant and correlation id so they travel on the wire.
    /// Both accessors are AsyncLocal-backed, so a fresh DI scope still observes
    /// the values the caller (e.g. the outbox processor) established — this lets
    /// the singleton bus read request/flow-scoped context without capturing a
    /// scoped service.
    /// </summary>
    private (Guid? TenantId, string? CorrelationId) ReadAmbientContext()
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var tenantId = sp.GetService<ICurrentTenant>()?.TenantId;
        var correlationId = sp.GetService<ICorrelationContext>()?.CorrelationId;
        return (tenantId, correlationId);
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

            if (_connection is not null)
                await _connection.DisposeAsync();

            var factory = new ConnectionFactory
            {
                HostName = _opts.HostName,
                Port = _opts.Port,
                UserName = _opts.UserName,
                Password = _opts.Password,
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
