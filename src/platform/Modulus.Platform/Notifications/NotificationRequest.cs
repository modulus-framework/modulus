namespace Modulus.Notifications;

/// <summary>A categorised notification to one person in the current company. Muted channels for the category are skipped.</summary>
public sealed record NotificationRequest(
    Guid RecipientUserId,
    string Category,
    string Title,
    string? Message = null,
    NotificationSeverity Severity = NotificationSeverity.Info);

/// <summary>What a send did: the inbox id when the in-app channel delivered it, and the channels that did.</summary>
/// <param name="NotificationId">The inbox id, or null when nothing was delivered.</param>
/// <param name="Delivered">The channels that delivered it. Empty when the person muted every channel for the category.</param>
public sealed record NotificationDelivery(Guid? NotificationId, IReadOnlyList<NotificationChannel> Delivered);
