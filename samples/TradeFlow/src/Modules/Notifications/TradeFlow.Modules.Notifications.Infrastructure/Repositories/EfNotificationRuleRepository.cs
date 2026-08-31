using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Modules.Notifications.Infrastructure.Database;

namespace TradeFlow.Modules.Notifications.Infrastructure.Repositories;

public sealed class EfNotificationRuleRepository(NotificationsDbContext context) : INotificationRuleRepository
{
    public async Task<NotificationRule?> GetByIdAsync(NotificationRuleId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationRules
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<NotificationRule>> GetByEventKeyAsync(string eventKey, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationRules
            .Where(r => r.EventKey == eventKey && r.TenantId == tenantId)
            .OrderBy(r => r.Severity)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationRule>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationRules
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.EventKey)
            .ToListAsync(ct);
    }

    public async Task AddAsync(NotificationRule rule, CancellationToken ct = default)
    {
        await context.NotificationRules.AddAsync(rule, ct);
    }

    public async Task UpdateAsync(NotificationRule rule, CancellationToken ct = default)
    {
        context.NotificationRules.Update(rule);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(NotificationRule rule, CancellationToken ct = default)
    {
        context.NotificationRules.Remove(rule);
        await Task.CompletedTask;
    }
}
