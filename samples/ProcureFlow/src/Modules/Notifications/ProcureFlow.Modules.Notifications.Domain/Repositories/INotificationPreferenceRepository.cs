using ProcureFlow.Modules.Notifications.Domain.Entities;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationPreferenceRepository
{
    Task<NotificationPreference?> GetByIdAsync(NotificationPreferenceId id, Guid tenantId, CancellationToken ct = default);
    Task<NotificationPreference?> GetByUserAndCategoryAsync(Guid userId, string eventCategory, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationPreference>> GetByUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(NotificationPreference preference, CancellationToken ct = default);
    Task UpdateAsync(NotificationPreference preference, CancellationToken ct = default);
    Task DeleteAsync(NotificationPreference preference, CancellationToken ct = default);
}
