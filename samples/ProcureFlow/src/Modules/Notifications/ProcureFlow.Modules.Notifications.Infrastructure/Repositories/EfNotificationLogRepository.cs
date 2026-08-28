using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Notifications.Domain.Entities;
using ProcureFlow.Modules.Notifications.Domain.Repositories;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Modules.Notifications.Infrastructure.Database;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Infrastructure.Repositories;

public sealed class EfNotificationLogRepository(NotificationsDbContext context) : INotificationLogRepository
{
    public async Task<NotificationLog?> GetByIdAsync(NotificationLogId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationLogs
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<NotificationLog>> GetByNotificationAsync(Guid notificationId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationLogs
            .Where(l => l.NotificationId == notificationId && l.TenantId == tenantId)
            .OrderBy(l => l.Channel)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationLog>> GetByUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationLogs
            .Where(l => l.RecipientUserId == userId && l.TenantId == tenantId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<NotificationLog>> GetFailedAsync(Guid tenantId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        IQueryable<NotificationLog> query = context.NotificationLogs
            .Where(l => l.TenantId == tenantId &&
                (l.Status == NotificationLogStatus.Failed || l.Status == NotificationLogStatus.DeadLettered));

        int totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<NotificationLog>(items, totalCount, pageNumber, pageSize);
    }

    public async Task AddAsync(NotificationLog log, CancellationToken ct = default)
    {
        await context.NotificationLogs.AddAsync(log, ct);
    }

    public async Task UpdateAsync(NotificationLog log, CancellationToken ct = default)
    {
        context.NotificationLogs.Update(log);
        await Task.CompletedTask;
    }
}
