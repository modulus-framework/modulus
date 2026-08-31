namespace TradeFlow.Modules.Notifications.Domain.ValueObjects;

/// <summary>
/// Lifecycle status of a dispatched notification (per channel per recipient).
/// </summary>
public enum NotificationLogStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
    Retrying = 6,
    DeadLettered = 7
}
