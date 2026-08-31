using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationLogRepository
{
    Task<NotificationLog?> GetByIdAsync(NotificationLogId id, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationLog>> GetByNotificationAsync(Guid notificationId, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationLog>> GetByUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
    Task<PagedResult<NotificationLog>> GetFailedAsync(Guid tenantId, int pageNumber, int pageSize, CancellationToken ct = default);
    Task AddAsync(NotificationLog log, CancellationToken ct = default);
    Task UpdateAsync(NotificationLog log, CancellationToken ct = default);
}
