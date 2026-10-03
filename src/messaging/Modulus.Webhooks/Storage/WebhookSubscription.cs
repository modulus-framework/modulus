namespace Modulus.Webhooks;

/// <summary>
/// An endpoint that receives the webhook events it subscribed to. Owned by one tenant (<see cref="Guid.Empty"/> = the
/// host); every query of the management API is filtered by it.
/// </summary>
public sealed class WebhookSubscription
{
    /// <summary>The subscription id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The owning tenant; <see cref="Guid.Empty"/> for the host.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The absolute URL deliveries are POSTed to.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>A note for the people managing it.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The event names it receives: an exact name (<c>catalog.product-created.v1</c>), a prefix ending in <c>.*</c>
    /// (<c>catalog.*</c>) or <c>*</c> for every event.
    /// </summary>
    public List<string> EventTypes { get; set; } = [];

    /// <summary>The signing secret, Data Protection-encrypted (never the plain <c>whsec_</c> value).</summary>
    public string ProtectedSecret { get; set; } = string.Empty;

    /// <summary>The secret replaced by the last rotation, encrypted; it keeps signing until <see cref="PreviousSecretExpiresAt"/>.</summary>
    public string? ProtectedPreviousSecret { get; set; }

    /// <summary>When the previous secret stops signing.</summary>
    public DateTime? PreviousSecretExpiresAt { get; set; }

    /// <summary>Whether deliveries are sent. A disabled subscription receives no new events.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Why it was disabled automatically (<c>410 Gone</c>, failing too long); null when enabled or disabled by hand.</summary>
    public string? DisabledReason { get; set; }

    /// <summary>When the endpoint started failing every attempt; null while it succeeds.</summary>
    public DateTime? FailingSince { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last change (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Whether <paramref name="eventType"/> matches one of <see cref="EventTypes"/>.</summary>
    public bool Matches(string eventType) => WebhookEventFilter.Matches(EventTypes, eventType);
}

/// <summary>One event on its way to one subscription: the outbox of webhooks.</summary>
public sealed class WebhookDelivery
{
    /// <summary>The delivery id; the <c>webhook-id</c> header is <c>msg_</c> followed by it, the same on every attempt.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The subscription it goes to.</summary>
    public Guid SubscriptionId { get; init; }

    /// <summary>The subscription's tenant.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The integration event's id; with <see cref="SubscriptionId"/> it is unique, so a redelivered event is recorded once.</summary>
    public Guid EventId { get; init; }

    /// <summary>The event name (the body's <c>type</c>).</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>The request body: <c>{"type", "timestamp", "data"}</c>.</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>The correlation id of the operation that raised the event.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>When it was recorded (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the endpoint accepted it (a 2xx answer).</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>When it was given up on (retries used up, subscription gone or disabled).</summary>
    public DateTime? DeadLetteredAt { get; set; }

    /// <summary>Attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>The earliest time of the next attempt; null = as soon as possible.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>The worker instance holding the claim.</summary>
    public string? LockedBy { get; set; }

    /// <summary>When the claim expires.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>When the last attempt was made.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>The HTTP status of the last attempt; null when no response was received.</summary>
    public int? LastStatusCode { get; set; }

    /// <summary>What went wrong on the last failed attempt (truncated).</summary>
    public string? LastError { get; set; }

    /// <summary>Pending, delivered or failed.</summary>
    public WebhookDeliveryStatus Status => DeliveredAt is not null
        ? WebhookDeliveryStatus.Delivered
        : DeadLetteredAt is not null ? WebhookDeliveryStatus.Failed : WebhookDeliveryStatus.Pending;

    /// <summary>The <c>webhook-id</c> header value.</summary>
    public string MessageId => "msg_" + Id.ToString("N");
}

/// <summary>Where a delivery stands.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WebhookDeliveryStatus>))]
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting for its first attempt or a retry.</summary>
    Pending,

    /// <summary>Accepted by the endpoint.</summary>
    Delivered,

    /// <summary>Given up on (dead-lettered); a retry from the management API sends it again.</summary>
    Failed,
}
