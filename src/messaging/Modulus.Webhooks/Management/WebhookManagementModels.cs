namespace Modulus.Webhooks;

/// <summary>Creates or replaces a subscription.</summary>
/// <param name="Url">The https URL deliveries are POSTed to.</param>
/// <param name="EventTypes">Event names, <c>prefix.*</c> patterns or <c>*</c>.</param>
/// <param name="Description">A note for the people managing it.</param>
/// <param name="IsEnabled">On create: defaults to true. On update: enabling clears the automatic-disable reason.</param>
/// <param name="Secret">On create only: a <c>whsec_</c> secret to use instead of a generated one.</param>
public sealed record WebhookSubscriptionRequest(
    string? Url,
    IReadOnlyList<string>? EventTypes,
    string? Description = null,
    bool? IsEnabled = null,
    string? Secret = null);

/// <summary>A subscription. <see cref="Secret"/> is only returned on create and on a secret rotation.</summary>
public sealed record WebhookSubscriptionResponse(
    Guid Id,
    string Url,
    IReadOnlyList<string> EventTypes,
    string? Description,
    bool IsEnabled,
    string? DisabledReason,
    DateTime? FailingSince,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Secret = null);

/// <summary>A delivery; <see cref="Payload"/> is filled when one delivery is fetched.</summary>
public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid SubscriptionId,
    string MessageId,
    Guid EventId,
    string EventType,
    WebhookDeliveryStatus Status,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    DateTime? NextAttemptAt,
    DateTime? LastAttemptAt,
    int? LastStatusCode,
    string? LastError,
    string? Payload = null);

/// <summary>An event subscriptions can choose.</summary>
public sealed record WebhookEventTypeResponse(string Name, string? Description);

/// <summary>A page of results.</summary>
public sealed record WebhookPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
