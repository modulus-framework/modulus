namespace Modulus.Notifications;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Core.Abstractions.Exceptions;

/// <inheritdoc cref="INotificationInbox" />
public sealed class NotificationInbox(
    INotificationStore store,
    INotificationPreferenceStore preferences,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : INotificationInbox
{
    public async Task<PagedList<UserNotification>> ListAsync(bool unreadOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var (tenantId, userId) = Caller();
        return await store.ListAsync(userId, tenantId, unreadOnly, page, pageSize, ct).ConfigureAwait(false);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        var (tenantId, userId) = Caller();
        var unread = await store.ListAsync(userId, tenantId, unreadOnly: true, page: 1, pageSize: 1, ct).ConfigureAwait(false);
        return unread.TotalCount;
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var (tenantId, userId) = Caller();

        // The id alone is not enough: a notification from another company is not in this inbox, even for the same person.
        var item = await store.GetOrNullAsync(id, ct).ConfigureAwait(false);
        if (item is null || item.UserId != userId || item.TenantId != tenantId)
            throw new NotFoundException("No such notification.");

        await store.MarkAsReadAsync(id, userId, ct).ConfigureAwait(false);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct = default)
    {
        var (tenantId, userId) = Caller();
        return await store.MarkAllAsReadAsync(userId, tenantId, ct).ConfigureAwait(false);
    }

    public Task<bool> GetPreferenceAsync(string category, NotificationChannel channel, CancellationToken ct = default)
    {
        var (tenantId, userId) = Caller();
        return PreferenceOrDefault(tenantId, userId, NotificationCategory.Require(category), channel, ct);
    }

    public Task SetPreferenceAsync(string category, NotificationChannel channel, bool enabled, CancellationToken ct = default)
    {
        var (tenantId, userId) = Caller();
        return preferences.SetAsync(tenantId, userId, NotificationCategory.Require(category), channel, enabled, ct);
    }

    private async Task<bool> PreferenceOrDefault(Guid? tenantId, Guid userId, string category, NotificationChannel channel, CancellationToken ct)
        => await preferences.GetAsync(tenantId, userId, category, channel, ct).ConfigureAwait(false) ?? true;

    private (Guid? TenantId, Guid UserId) Caller()
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        return (currentTenant.TenantId, userId);
    }
}
