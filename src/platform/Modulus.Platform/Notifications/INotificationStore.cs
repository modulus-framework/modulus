namespace Modulus.Notifications;

using Modulus.Core.Abstractions.Common;

/// <summary>
/// Per-user notification persistence. Recipient ids are explicit so the
/// store stays correct outside a user scope (system-sent notifications).
/// </summary>
/// <remarks>
/// The tenant is matched exactly, so one login that reaches several companies sees each company's inbox
/// separately: <c>tenantId: null</c> lists the notifications sent without a company (host or single-tenant
/// apps), never every company's.
/// </remarks>
public interface INotificationStore
{
    Task InsertAsync(UserNotification notification, CancellationToken ct = default);

    /// <summary>The user's notifications in <paramref name="tenantId"/> (null = those sent without a company).</summary>
    Task<PagedList<UserNotification>> ListAsync(
        Guid userId,
        Guid? tenantId = null,
        bool unreadOnly = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<UserNotification?> GetOrNullAsync(Guid id, CancellationToken ct = default);

    Task<bool> MarkAsReadAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Marks the user's notifications in <paramref name="tenantId"/> read (null = those sent without a company).</summary>
    Task<int> MarkAllAsReadAsync(Guid userId, Guid? tenantId = null, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);
}
