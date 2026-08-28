namespace Modulus.EventBus.Kafka;

using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Health check for Kafka broker connectivity. Pings the broker with a lightweight
/// metadata fetch to verify the broker is reachable and healthy. Returns Unhealthy
/// if broker is unreachable or unresponsive within 5 seconds.
/// </summary>
public sealed class KafkaHealthCheck(
    IOptions<KafkaOptions> options,
    ILogger<KafkaHealthCheck> logger) : IModuleHealthCheck
{
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = options.Value.BootstrapServers,
                SecurityProtocol = Enum.Parse<SecurityProtocol>(
                    options.Value.SecurityProtocol, ignoreCase: true),
            };

            if (!string.IsNullOrWhiteSpace(options.Value.SaslUsername))
            {
                config.SaslMechanism = Enum.Parse<SaslMechanism>(
                    options.Value.SaslMechanism, ignoreCase: true);
                config.SaslUsername = options.Value.SaslUsername;
                config.SaslPassword = options.Value.SaslPassword;
            }

            if (!string.IsNullOrWhiteSpace(options.Value.SslCaLocation))
                config.SslCaLocation = options.Value.SslCaLocation;

            using var adminClient = new AdminClientBuilder(config).Build();

            // Lightweight metadata fetch (ping) with 5-second timeout
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));

            sw.Stop();
            var brokerCount = metadata.Brokers.Count;
            if (brokerCount == 0)
                return Unhealthy("No brokers available", sw.Elapsed);

            return new ModuleHealthResult(
                ModuleName: "Modulus.EventBus.Kafka",
                Status: HealthStatus.Healthy,
                Description: $"Connected to {brokerCount} Kafka broker(s)",
                CheckDuration: sw.Elapsed,
                Data: new Dictionary<string, object>
                {
                    ["brokers"] = brokerCount,
                    ["topics"] = metadata.Topics.Count,
                });
        }
        catch (Confluent.Kafka.KafkaException ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Kafka broker health check failed");
            return Unhealthy($"Broker unreachable: {ex.Message}", sw.Elapsed);
        }
        catch (TimeoutException)
        {
            sw.Stop();
            logger.LogWarning("Kafka broker health check timed out");
            return Unhealthy("Broker did not respond within 5 seconds", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Kafka health check error");
            return Unhealthy($"Error: {ex.Message}", sw.Elapsed);
        }
    }

    private static ModuleHealthResult Unhealthy(string message, TimeSpan duration)
        => new(
            ModuleName: "Modulus.EventBus.Kafka",
            Status: HealthStatus.Unhealthy,
            Description: message,
            CheckDuration: duration);
}
