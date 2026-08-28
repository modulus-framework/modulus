namespace Modulus.EventBus.RabbitMQ;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using global::RabbitMQ.Client;

/// <summary>
/// Health check for RabbitMQ broker connectivity. Opens a connection to the broker
/// and verifies it can communicate. Returns Unhealthy if the broker is unreachable
/// or unresponsive.
/// </summary>
public sealed class RabbitMqHealthCheck(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqHealthCheck> logger) : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = options.Value.HostName,
                Port = options.Value.Port,
                UserName = options.Value.UserName,
                Password = options.Value.Password,
                VirtualHost = options.Value.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(10),
            };

            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

            await channel.ExchangeDeclareAsync(
                exchange: options.Value.ExchangeName,
                type: options.Value.ExchangeType,
                durable: options.Value.Durable,
                autoDelete: options.Value.AutoDelete,
                cancellationToken: ct);

            sw.Stop();
            return new ModuleHealthResult(
                ModuleName: "Modulus.EventBus.RabbitMQ",
                Status: HealthStatus.Healthy,
                Description: $"Connected to RabbitMQ at {options.Value.HostName}:{options.Value.Port}",
                CheckDuration: sw.Elapsed,
                Data: new Dictionary<string, object>
                {
                    ["server_version"] = connection.ServerProperties?["version"]?.ToString() ?? "unknown",
                    ["virtual_host"] = options.Value.VirtualHost,
                });
        }
        catch (Exception ex)
        {
            sw.Stop();
            var message = ex switch
            {
                TimeoutException => "Broker did not respond within 5 seconds",
                _ => ex.GetType().Name.Contains("Unreachable")
                    ? $"Broker unreachable: {options.Value.HostName}:{options.Value.Port}"
                    : $"Error: {ex.Message}",
            };
            logger.LogWarning(ex, "RabbitMQ health check failed: {Message}", message);
            return Unhealthy(message, sw.Elapsed);
        }
    }

    private static ModuleHealthResult Unhealthy(string message, TimeSpan duration)
        => new(
            ModuleName: "Modulus.EventBus.RabbitMQ",
            Status: HealthStatus.Unhealthy,
            Description: message,
            CheckDuration: duration);
}
