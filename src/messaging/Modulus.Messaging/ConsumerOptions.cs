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

    /// <summary>Stops the endpoint while its consumer keeps failing, and tests it with a trial message (optional, FR-15).</summary>
    public CircuitBreakerOptions? CircuitBreaker { get; set; }

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

    /// <summary>
    /// How many messages of the endpoint one host handles at once (FR-14 "under concurrency 16"). Unset means 1.
    /// Ordered messages still run in send order within their partition. Needs a transport that delivers
    /// concurrently (see <see cref="IConcurrentDeliveryTransport"/>); the bus refuses a higher value otherwise.
    /// </summary>
    public int? ConcurrentMessageLimit { get; set; }

    /// <summary>
    /// Only handle messages sent for a tenant (FR-21). A message without one is moved to <c>_skipped</c> with reason
    /// <c>no-tenant</c>, and no consumer runs. Unset means false.
    /// </summary>
    public bool? RequireTenant { get; set; }

    /// <summary>
    /// Most deliveries of one message this consumer accepts (D3). A delivery past the limit is parked to <c>_error</c>
    /// with reason <c>delivery-limit</c> without running the consumer, so a message that crashes its host on every
    /// attempt stops looping. Unset means <see cref="DefaultMaxDeliveryCount"/>.
    /// </summary>
    public int? MaxDeliveryCount { get; set; }

    /// <summary>
    /// Seal the body of every message to this consumer, not only the marked types (D6). Needs
    /// <see cref="IMessageEncryptionKeys"/>. Unset means false.
    /// </summary>
    public bool? EncryptPayload { get; set; }

    /// <summary>
    /// Only one host at a time receives this endpoint's messages, so they are handled in send order across hosts (D10,
    /// FR-14). Needs a transport that supports it (RabbitMQ). Unset means false. On RabbitMQ the queue argument is set when
    /// the queue is declared; a queue that already exists keeps its arguments until it is recreated.
    /// </summary>
    public bool? SingleActiveConsumer { get; set; }

    /// <summary>The delivery limit when none is set (RabbitMQ quorum queues use the same default).</summary>
    internal const int DefaultMaxDeliveryCount = 20;

    internal void Validate()
    {
        RateLimit?.Validate();
        KillSwitch?.Validate();
        CircuitBreaker?.Validate();
        if (TimeToLive is { } ttl && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeToLive), "Must be positive.");
        }

        if (MaxDeliveryCount is { } maxDeliveries && maxDeliveries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDeliveryCount), "Must be at least 1.");
        }

        if (ConcurrentMessageLimit is { } limit && limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ConcurrentMessageLimit), "Must be at least 1.");
        }
    }

    internal ConsumerOptions Inherit(ConsumerOptions defaults) => new()
    {
        Retry = Retry ?? defaults.Retry,
        Redelivery = Redelivery ?? defaults.Redelivery,
        ConsumeTimeout = ConsumeTimeout ?? defaults.ConsumeTimeout,
        RateLimit = RateLimit ?? defaults.RateLimit,
        KillSwitch = KillSwitch ?? defaults.KillSwitch,
        CircuitBreaker = CircuitBreaker ?? defaults.CircuitBreaker,
        TimeToLive = TimeToLive ?? defaults.TimeToLive,
        Priority = Priority ?? defaults.Priority,
        Ordered = Ordered ?? defaults.Ordered,
        ConcurrentMessageLimit = ConcurrentMessageLimit ?? defaults.ConcurrentMessageLimit,
        RequireTenant = RequireTenant ?? defaults.RequireTenant,
        MaxDeliveryCount = MaxDeliveryCount ?? defaults.MaxDeliveryCount,
        EncryptPayload = EncryptPayload ?? defaults.EncryptPayload,
        SingleActiveConsumer = SingleActiveConsumer ?? defaults.SingleActiveConsumer,
    };
}
