namespace Modulus.Messaging;

/// <summary>
/// Header names the bus stamps on envelopes and the transports carry. Transports that store priority read
/// <see cref="Priority"/>; the bus enforces <see cref="ExpiresAt"/> at delivery (FR-13).
/// </summary>
public static class MessagingHeaders
{
    /// <summary>UTC time after which the message is not processed (ISO 8601, round-trip format).</summary>
    public const string ExpiresAt = "mod-expires-at";

    /// <summary>Delivery priority as an integer; higher values are taken first. Absent means 0.</summary>
    public const string Priority = "mod-priority";

    /// <summary>Why a message was moved to <c>_skipped</c> by the bus (for example <c>expired</c>).</summary>
    public const string SkipReason = "mod-skip-reason";

    /// <summary>
    /// Partition of a message to an ordered consumer (FR-14). Messages with the same value are handled in send order.
    /// Set by the bus; an ordered consumer without a <see cref="PartitionKeyAttribute"/> uses its endpoint name.
    /// </summary>
    public const string PartitionKey = "mod-partition-key";

    /// <summary>
    /// Position of a message in its partition, kept when the transport redelivers it, so a retry keeps its place
    /// ahead of the messages sent after it. Set by the transport when it claims the message.
    /// </summary>
    public const string Sequence = "mod-sequence";
}
