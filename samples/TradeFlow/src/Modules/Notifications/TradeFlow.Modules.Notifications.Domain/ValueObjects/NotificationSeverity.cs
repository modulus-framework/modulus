namespace TradeFlow.Modules.Notifications.Domain.ValueObjects;

/// <summary>
/// Severity drives default channel mapping and quiet-hours piercing.
/// Info → in-app only; Normal → in-app+email; High → in-app+email+push; Critical → +SMS/WhatsApp, ignores quiet hours.
/// </summary>
public enum NotificationSeverity
{
    Info = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
