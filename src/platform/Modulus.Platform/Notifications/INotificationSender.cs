namespace Modulus.Notifications;

/// <summary>
/// Sends categorised notifications and honours each person's channel choices. Sending is a code-level call: the caller decides who
/// is notified. Use <see cref="INotificationPublisher"/> when a notification has no category and needs no preference check.
/// </summary>
public interface INotificationSender
{
    Task<NotificationDelivery> SendAsync(NotificationRequest request, CancellationToken ct = default);
}
