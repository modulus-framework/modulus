namespace Modulus.Notifications;

using System.Collections.Concurrent;
using Modulus.Core.Abstractions.Common;

/// <summary>
/// Dependency-free <see cref="INotificationStore"/> default. Single-node:
/// replace with a durable store for multi-instance deployments.
/// </summary>
public sealed class InMemoryNotificationStore : INotificationStore
{
    public const int MaxPageSize = 100;

    private readonly ConcurrentDictionary<Guid, UserNotification> _notifications = new();

    public Task InsertAsync(UserNotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _notifications[notification.Id] = notification;
        return Task.CompletedTask;
    }

    public Task<PagedList<UserNotification>> ListAsync(
        Guid userId,
        Guid? tenantId = null,
        bool unreadOnly = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var filtered = _notifications.Values
            .Where(n => n.UserId == userId)
            .Where(n => tenantId is null || n.TenantId == tenantId)
            .Where(n => !unreadOnly || !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        return Task.FromResult(new PagedList<UserNotification>
        {
            Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = filtered.Count,
            Page = page,
            PageSize = pageSize,
        });
    }

    public Task<UserNotification?> GetOrNullAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_notifications.TryGetValue(id, out var notification) ? notification : null);

    public Task<bool> MarkAsReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        if (!_notifications.TryGetValue(id, out var current) || current.UserId != userId || current.IsRead)
            return Task.FromResult(false);

        // Recipient check first (fail-closed), then atomic read-claim so two
        // concurrent readers cannot both flip an unread row.
        var updated = current with { ReadAt = DateTimeOffset.UtcNow };
        return Task.FromResult(_notifications.TryUpdate(id, updated, current));
    }

    public Task<int> MarkAllAsReadAsync(Guid userId, Guid? tenantId = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var marked = 0;
        foreach (var (id, current) in _notifications)
        {
            if (current.UserId != userId || current.IsRead)
                continue;
            if (tenantId.HasValue && current.TenantId != tenantId)
                continue;

            if (_notifications.TryUpdate(id, current with { ReadAt = now }, current))
                marked++;
        }

        return Task.FromResult(marked);
    }

    public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        // Recipient check before removal: one user cannot delete another's row.
        if (!_notifications.TryGetValue(id, out var current) || current.UserId != userId)
            return Task.FromResult(false);

        return Task.FromResult(_notifications.TryRemove(id, out _));
    }
}
