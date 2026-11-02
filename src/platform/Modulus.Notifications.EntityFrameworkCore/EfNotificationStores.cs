namespace Modulus.Notifications.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions.Common;

/// <summary>
/// The durable <see cref="INotificationStore"/>. Every query names the person and the company, so one person's inbox is never returned for
/// another. Read claims are single conditional updates, so two concurrent readers cannot both mark the same notification.
/// </summary>
public sealed class EfNotificationStore(ModulusNotificationsDbContext db) : INotificationStore
{
    public async Task InsertAsync(UserNotification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        db.Notifications.Add(NotificationRow.From(notification));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<PagedList<UserNotification>> ListAsync(
        Guid userId,
        Guid? tenantId = null,
        bool unreadOnly = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, InMemoryNotificationStore.MaxPageSize);

        var rows = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.TenantId == tenantId);
        if (unreadOnly)
            rows = rows.Where(n => n.ReadAtTicks == null);

        var total = await rows.CountAsync(ct).ConfigureAwait(false);
        var items = await rows
            .OrderByDescending(n => n.CreatedAtTicks)
            .ThenBy(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedList<UserNotification>
        {
            Items = items.Select(r => r.ToNotification()).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<UserNotification?> GetOrNullAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct).ConfigureAwait(false);
        return row?.ToNotification();
    }

    public async Task<bool> MarkAsReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        // The recipient check and the unread check are part of the update itself, so the claim is atomic.
        long? readAt = DateTimeOffset.UtcNow.UtcTicks;
        var changed = await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAtTicks == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtTicks, readAt), ct)
            .ConfigureAwait(false);
        return changed > 0;
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId, Guid? tenantId = null, CancellationToken ct = default)
    {
        long? readAt = DateTimeOffset.UtcNow.UtcTicks;
        return await db.Notifications
            .Where(n => n.UserId == userId && n.TenantId == tenantId && n.ReadAtTicks == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtTicks, readAt), ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var deleted = await db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        return deleted > 0;
    }
}

/// <summary>The durable <see cref="INotificationPreferenceStore"/>: one row per person, company, category and channel.</summary>
public sealed class EfNotificationPreferenceStore(ModulusNotificationsDbContext db) : INotificationPreferenceStore
{
    public async Task<bool?> GetAsync(Guid? tenantId, Guid userId, string category, NotificationChannel channel, CancellationToken ct = default)
    {
        var row = await db.Preferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.Category == category && p.Channel == channel, ct)
            .ConfigureAwait(false);
        return row?.Enabled;
    }

    public async Task SetAsync(Guid? tenantId, Guid userId, string category, NotificationChannel channel, bool enabled, CancellationToken ct = default)
    {
        var row = await db.Preferences
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.Category == category && p.Channel == channel, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            db.Preferences.Add(new NotificationPreferenceRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                Category = category,
                Channel = channel,
                Enabled = enabled,
            });
        }
        else
        {
            row.Enabled = enabled;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
