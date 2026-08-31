using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationRuleRepository
{
    Task<NotificationRule?> GetByIdAsync(NotificationRuleId id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRule>> GetByEventKeyAsync(string eventKey, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRule>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(NotificationRule rule, CancellationToken ct = default);
    Task UpdateAsync(NotificationRule rule, CancellationToken ct = default);
    Task DeleteAsync(NotificationRule rule, CancellationToken ct = default);
}
