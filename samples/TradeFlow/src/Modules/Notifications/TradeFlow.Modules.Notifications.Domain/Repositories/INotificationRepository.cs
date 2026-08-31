using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationRepository
{
    Task<Notification?> GetByIdAsync(NotificationId id, Guid tenantId, CancellationToken ct = default);
    Task<PagedResult<Notification>> GetByUserAsync(Guid recipientUserId, Guid tenantId, bool? isRead, int pageNumber, int pageSize, CancellationToken ct = default);
    Task<long> CountUnreadAsync(Guid recipientUserId, Guid tenantId, CancellationToken ct = default);
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task UpdateAsync(Notification notification, CancellationToken ct = default);
    Task DeleteAsync(Notification notification, CancellationToken ct = default);
}
