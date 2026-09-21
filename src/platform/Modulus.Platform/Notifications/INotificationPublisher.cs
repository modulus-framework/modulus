namespace Modulus.Notifications;

/// <summary>
/// Ambient-context notification publisher: stamps tenant/time so call sites
/// stay one line. Real-time fan-out (SignalR) subscribes to the store separately.
/// </summary>
public interface INotificationPublisher
{
    Task<UserNotification> PublishAsync(
        Guid userId,
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid? tenantId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<UserNotification>> PublishToUsersAsync(
        IEnumerable<Guid> userIds,
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
