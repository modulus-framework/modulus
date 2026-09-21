namespace Modulus.Notifications;

using Modulus.Core.Abstractions;

/// <inheritdoc cref="INotificationPublisher" />
public sealed class NotificationPublisher(
    INotificationStore store,
    ICurrentTenant currentTenant,
    TimeProvider clock) : INotificationPublisher
{
    private readonly INotificationStore _store = store;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly TimeProvider _clock = clock;

    public async Task<UserNotification> PublishAsync(
        Guid userId,
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var notification = new UserNotification
        {
            TenantId = tenantId ?? _currentTenant.TenantId,
            UserId = userId,
            Severity = severity,
            Title = title,
            Message = message,
            CreatedAt = _clock.GetUtcNow(),
        };

        await _store.InsertAsync(notification, ct).ConfigureAwait(false);
        return notification;
    }

    public async Task<IReadOnlyList<UserNotification>> PublishToUsersAsync(
        IEnumerable<Guid> userIds,
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var published = new List<UserNotification>();
        foreach (var userId in userIds)
            published.Add(await PublishAsync(userId, title, message, severity, tenantId, ct).ConfigureAwait(false));

        return published;
    }
}
