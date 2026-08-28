using ProcureFlow.Modules.Notifications.Domain.Entities;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationRuleRepository
{
    Task<NotificationRule?> GetByIdAsync(NotificationRuleId id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRule>> GetByEventKeyAsync(string eventKey, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRule>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(NotificationRule rule, CancellationToken ct = default);
    Task UpdateAsync(NotificationRule rule, CancellationToken ct = default);
    Task DeleteAsync(NotificationRule rule, CancellationToken ct = default);
}
