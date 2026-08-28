namespace ProcureFlow.Modules.Notifications.Domain.ValueObjects;

/// <summary>
/// Supported notification delivery channels.
/// </summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    InApp = 1,
    Email = 2,
    Sms = 4,
    WhatsApp = 8,
    Push = 16,
    Webhook = 32
}
