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

    /// <summary>
    /// W3C trace context (<c>traceparent</c>) of the span that sent the message. The consumer's span continues that
    /// trace. Absent when no trace was active or no listener was attached.
    /// </summary>
    public const string TraceParent = "mod-traceparent";

    /// <summary>
    /// Tenant the message was sent for (FR-21). Set from <see cref="MessageTenant.Current"/> when the message is created;
    /// a consumer runs with it, and a consumer that requires a tenant parks a message without one.
    /// </summary>
    public const string TenantId = "mod-tenant-id";

    /// <summary>
    /// How many times the transport has delivered this copy, counting this one (1 on the first delivery). A transport
    /// that tracks deliveries sets it when it hands the message over; the bus also counts its own redeliveries (D3).
    /// </summary>
    public const string DeliveryCount = "mod-delivery-count";

    /// <summary>
    /// How many times the bus has redelivered this message after a failed attempt (FR-08). Set by the bus; a replay from
    /// <c>_error</c> resets it (FR-09).
    /// </summary>
    public const string RedeliveryCount = "mod-redelivery-count";

    /// <summary>
    /// The CLR type name of a sealed message's body (D6). The envelope's body is then an <see cref="EncryptedPayload"/>, and
    /// the bus opens it as this type before a consumer sees it.
    /// </summary>
    public const string EncryptedType = "mod-encrypted-type";

    /// <summary>The request id a reply names (FR-06). Set by the requester; the responder copies nothing, it sends the id back in the reply.</summary>
    public const string RequestId = "mod-request-id";

    /// <summary>The endpoint a request's reply goes to (FR-06). Set by the requester.</summary>
    public const string ReplyTo = "mod-reply-to";
}
