using Microsoft.EntityFrameworkCore;
using ModulusSample.Modules.Notifications.Domain.Entities;
using ModulusSample.Modules.Notifications.Domain.Repositories;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;
using ModulusSample.Modules.Notifications.Infrastructure.Database;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Infrastructure.Repositories;

public sealed class EfNotificationRepository(NotificationsDbContext context) : INotificationRepository
{
    public async Task<Notification?> GetByIdAsync(NotificationId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId, ct);
    }

    public async Task<PagedResult<Notification>> GetByUserAsync(
        Guid recipientUserId,
        Guid tenantId,
        bool? isRead,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        IQueryable<Notification> query = context.Notifications
            .Where(n => n.RecipientUserId == recipientUserId && n.TenantId == tenantId);

        if (isRead.HasValue)
        {
            query = query.Where(n => n.IsRead == isRead.Value);
        }

        int totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Notification>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<long> CountUnreadAsync(Guid recipientUserId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.Notifications
            .LongCountAsync(n => n.RecipientUserId == recipientUserId && n.TenantId == tenantId && !n.IsRead, ct);
    }

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        await context.Notifications.AddAsync(notification, ct);
    }

    public async Task UpdateAsync(Notification notification, CancellationToken ct = default)
    {
        context.Notifications.Update(notification);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(Notification notification, CancellationToken ct = default)
    {
        context.Notifications.Remove(notification);
        await Task.CompletedTask;
    }
}