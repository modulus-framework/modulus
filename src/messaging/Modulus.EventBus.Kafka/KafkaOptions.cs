namespace Modulus.EventBus.Kafka;

/// <summary>
/// Configuration for the Kafka event-bus provider.
/// Bind from <c>"EventBus:Kafka"</c> or configure via the
/// <c>AddKafkaEventBus</c> callback.
/// </summary>
public sealed class KafkaOptions
{
    /// <summary>Comma-separated broker addresses (e.g. <c>localhost:9092</c>).</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Consumer group id.  Each application instance should share a group id.</summary>
    public string GroupId { get; set; } = "modulus-consumer";

    /// <summary>Prefix applied to topic names.  Final topic = <c>{prefix}.{Type.FullName}</c>.</summary>
    public string TopicPrefix { get; set; } = "modulus";

    /// <summary>SASL mechanism: none, Plain, ScramSha256, ScramSha512.</summary>
    public string SaslMechanism { get; set; } = "Plain";

    /// <summary>Security protocol: Plaintext, Ssl, SaslPlaintext, SaslSsl.</summary>
    public string SecurityProtocol { get; set; } = "Plaintext";

    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string? SslCaLocation { get; set; }

    /// <summary>Auto-offset-reset for new consumer groups: earliest, latest, error.</summary>
    public string AutoOffsetReset { get; set; } = "Earliest";

    /// <summary>Enable auto-commit of consumer offsets.</summary>
    public bool EnableAutoCommit { get; set; } = true;

    /// <summary>Auto-commit interval in milliseconds.</summary>
    public int AutoCommitIntervalMs { get; set; } = 5000;

    /// <summary>Number of retries for transient produce failures.</summary>
    public int MessageSendMaxRetries { get; set; } = 3;

    /// <summary>Acks: all = -1, leader = 1, none = 0.</summary>
    public string Acks { get; set; } = "all";
}
