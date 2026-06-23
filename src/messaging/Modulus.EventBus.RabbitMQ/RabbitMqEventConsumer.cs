namespace Modulus.EventBus.RabbitMQ;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Events;
using Modulus.Events.Abstractions;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;

/// <summary>
/// Background service that consumes integration events from a RabbitMQ queue,
/// deserialises the <see cref="IntegrationEventEnvelope"/>, and dispatches
/// to the registered handlers via <see cref="IntegrationEventDispatcher"/>.
/// </summary>
internal sealed class RabbitMqEventConsumer : BackgroundService
{
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqEventConsumer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventRegistry _registry;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqEventConsumer(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventConsumer> logger,
        IServiceScopeFactory scopeFactory,
        IIntegrationEventRegistry registry)
    {
        _opts         = options.Value;
        _logger       = logger;
        _scopeFactory = scopeFactory;
        _registry     = registry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RabbitMQ consumer crashed; reconnecting in {Delay} ms",
                    _opts.ReconnectDelayMs);
                await Task.Delay(_opts.ReconnectDelayMs, stoppingToken);
            }
        }

        await CleanupAsync();
    }

    private async Task ConnectAndConsumeAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName    = _opts.HostName,
            Port        = _opts.Port,
            UserName    = _opts.UserName,
            Password    = _opts.Password,
            VirtualHost = _opts.VirtualHost,
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel    = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(
            _opts.ExchangeName, _opts.ExchangeType,
            durable: _opts.Durable,
            autoDelete: _opts.AutoDelete,
            cancellationToken: ct);

        await _channel.QueueDeclareAsync(
            _opts.QueueName,
            durable: _opts.Durable,
            exclusive: false,
            autoDelete: _opts.AutoDelete,
            cancellationToken: ct);

        foreach (var routingKey in _registry.GetRoutingKeys())
        {
            await _channel.QueueBindAsync(
                _opts.QueueName, _opts.ExchangeName,
                routingKey: routingKey,
                cancellationToken: ct);
        }

        await _channel.BasicQosAsync(0, (ushort)_opts.PrefetchCount, false, ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceived;

        await _channel.BasicConsumeAsync(
            _opts.QueueName, autoAck: _opts.AutoAck, consumer, ct);

        _logger.LogInformation(
            "RabbitMQ consumer started — exchange '{Exchange}', queue '{Queue}', {BindingCount} binding(s)",
            _opts.ExchangeName, _opts.QueueName,
            _registry.GetRoutingKeys().Count);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs ea)
    {
        var channel = (IChannel)((AsyncEventingBasicConsumer)sender).Channel;

        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(json);

            if (envelope is null)
            {
                _logger.LogWarning("Received null envelope from RabbitMQ; acking");
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider
                .GetRequiredService<IntegrationEventDispatcher>();

            var handled = await dispatcher.DispatchAsync(envelope);

            if (!handled)
                _logger.LogDebug(
                    "No handler for routing key '{RoutingKey}'; acking",
                    envelope.RoutingKey);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing RabbitMQ message; nacking (requeue={Requeue})",
                true);
            await channel.BasicNackAsync(
                ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task CleanupAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
