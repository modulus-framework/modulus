using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Modules.Notifications.Infrastructure.Database;

namespace TradeFlow.Modules.Notifications.Infrastructure.Repositories;

public sealed class EfNotificationPreferenceRepository(NotificationsDbContext context) : INotificationPreferenceRepository
{
    public async Task<NotificationPreference?> GetByIdAsync(NotificationPreferenceId id, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId, ct);
    }

    public async Task<NotificationPreference?> GetByUserAndCategoryAsync(
        Guid userId, string eventCategory, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationPreferences
            .FirstOrDefaultAsync(p =>
                p.UserId == userId &&
                p.EventCategory == eventCategory &&
                p.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<NotificationPreference>> GetByUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        return await context.NotificationPreferences
            .Where(p => p.UserId == userId && p.TenantId == tenantId)
            .OrderBy(p => p.EventCategory)
            .ToListAsync(ct);
    }

    public async Task AddAsync(NotificationPreference preference, CancellationToken ct = default)
    {
        await context.NotificationPreferences.AddAsync(preference, ct);
    }

    public async Task UpdateAsync(NotificationPreference preference, CancellationToken ct = default)
    {
        context.NotificationPreferences.Update(preference);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(NotificationPreference preference, CancellationToken ct = default)
    {
        context.NotificationPreferences.Remove(preference);
        await Task.CompletedTask;
    }
}
