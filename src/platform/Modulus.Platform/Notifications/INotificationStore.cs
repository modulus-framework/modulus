namespace Modulus.Notifications;

using Modulus.Core.Abstractions.Common;

/// <summary>
/// Per-user notification persistence. Recipient ids are explicit so the
/// store stays correct outside a user scope (system-sent notifications).
/// </summary>
public interface INotificationStore
{
    Task InsertAsync(UserNotification notification, CancellationToken ct = default);

    Task<PagedList<UserNotification>> ListAsync(
        Guid userId,
        Guid? tenantId = null,
        bool unreadOnly = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<UserNotification?> GetOrNullAsync(Guid id, CancellationToken ct = default);

    Task<bool> MarkAsReadAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task<int> MarkAllAsReadAsync(Guid userId, Guid? tenantId = null, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);
}
