namespace Modulus.Messaging;

/// <summary>
/// Retry, redelivery and timeout settings. Set them on the bus (defaults for every consumer) or on one consumer;
/// the narrowest scope that sets a value wins (RULE-14). Unset values mean "off": a failing message goes to
/// <c>_error</c> on the first failure (FR-C3).
/// </summary>
public sealed class ConsumerOptions
{
    /// <summary>Immediate or delayed retries within one delivery attempt chain.</summary>
    public RetryPolicy? Retry { get; set; }

    /// <summary>Delayed redeliveries after retries are exhausted, each with its own delay.</summary>
    public RedeliveryPolicy? Redelivery { get; set; }

    /// <summary>Upper bound for one consume attempt. Expiry counts as a failure.</summary>
    public TimeSpan? ConsumeTimeout { get; set; }

    /// <summary>Caps deliveries per period to the endpoint (FR-15).</summary>
    public RateLimit? RateLimit { get; set; }

    /// <summary>Pauses the endpoint when too many recent messages failed (FR-15).</summary>
    public KillSwitchOptions? KillSwitch { get; set; }

    /// <summary>
    /// Time-to-live of messages to this consumer, counted from when they are sent. An older message is moved to
    /// <c>_skipped</c> unprocessed (FR-13). A <see cref="MessageTimeToLiveAttribute"/> on the message type overrides it.
    /// </summary>
    public TimeSpan? TimeToLive { get; set; }

    /// <summary>Delivery priority of messages to this consumer; higher is taken first where the transport supports it (FR-13).</summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Handle messages strictly in send order per partition (FR-14): by <see cref="PartitionKeyAttribute"/>, or the
    /// endpoint as one partition. A retried message keeps its place. Priority does not apply to an ordered consumer,
    /// because it would let a later message of the same partition overtake an earlier one.
    /// </summary>
    public bool? Ordered { get; set; }

    internal void Validate()
    {
        RateLimit?.Validate();
        KillSwitch?.Validate();
        if (TimeToLive is { } ttl && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeToLive), "Must be positive.");
        }
    }

    internal ConsumerOptions Inherit(ConsumerOptions defaults) => new()
    {
        Retry = Retry ?? defaults.Retry,
        Redelivery = Redelivery ?? defaults.Redelivery,
        ConsumeTimeout = ConsumeTimeout ?? defaults.ConsumeTimeout,
        RateLimit = RateLimit ?? defaults.RateLimit,
        KillSwitch = KillSwitch ?? defaults.KillSwitch,
        TimeToLive = TimeToLive ?? defaults.TimeToLive,
        Priority = Priority ?? defaults.Priority,
        Ordered = Ordered ?? defaults.Ordered,
    };
}
