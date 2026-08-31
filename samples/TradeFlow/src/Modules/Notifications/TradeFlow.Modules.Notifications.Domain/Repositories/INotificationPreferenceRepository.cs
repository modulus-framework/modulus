using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationPreferenceRepository
{
    Task<NotificationPreference?> GetByIdAsync(NotificationPreferenceId id, Guid tenantId, CancellationToken ct = default);
    Task<NotificationPreference?> GetByUserAndCategoryAsync(Guid userId, string eventCategory, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationPreference>> GetByUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(NotificationPreference preference, CancellationToken ct = default);
    Task UpdateAsync(NotificationPreference preference, CancellationToken ct = default);
    Task DeleteAsync(NotificationPreference preference, CancellationToken ct = default);
}
