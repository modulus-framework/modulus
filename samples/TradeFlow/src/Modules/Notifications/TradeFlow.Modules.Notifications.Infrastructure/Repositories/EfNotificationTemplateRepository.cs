using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Modules.Notifications.Infrastructure.Database;

namespace TradeFlow.Modules.Notifications.Infrastructure.Repositories;

public sealed class EfNotificationTemplateRepository(NotificationsDbContext context) : INotificationTemplateRepository
{
    public async Task<NotificationTemplate?> GetByIdAsync(NotificationTemplateId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
    }

    public async Task<NotificationTemplate?> GetActiveByKeyAndChannelAsync(
        string templateKey, NotificationChannel channel, string locale, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationTemplates
            .FirstOrDefaultAsync(t =>
                t.TemplateKey == templateKey &&
                t.Channel == channel &&
                t.Locale == locale &&
                t.TenantId == tenantId &&
                t.IsActive, ct);
    }

    public async Task<IReadOnlyList<NotificationTemplate>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationTemplates
            .Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.TemplateKey)
            .ThenBy(t => t.Channel)
            .ToListAsync(ct);
    }

    public async Task AddAsync(NotificationTemplate template, CancellationToken ct = default)
    {
        await context.NotificationTemplates.AddAsync(template, ct);
    }

    public async Task UpdateAsync(NotificationTemplate template, CancellationToken ct = default)
    {
        context.NotificationTemplates.Update(template);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(NotificationTemplate template, CancellationToken ct = default)
    {
        context.NotificationTemplates.Remove(template);
        await Task.CompletedTask;
    }
}
