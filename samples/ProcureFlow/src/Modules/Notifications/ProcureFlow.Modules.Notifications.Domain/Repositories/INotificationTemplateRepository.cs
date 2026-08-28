using ProcureFlow.Modules.Notifications.Domain.Entities;
using ProcureFlow.Modules.Notifications.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Notifications.Domain.Repositories;

public interface INotificationTemplateRepository
{
    Task<NotificationTemplate?> GetByIdAsync(NotificationTemplateId id, Guid tenantId, CancellationToken ct = default);
    Task<NotificationTemplate?> GetActiveByKeyAndChannelAsync(string templateKey, NotificationChannel channel, string locale, Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationTemplate>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(NotificationTemplate template, CancellationToken ct = default);
    Task UpdateAsync(NotificationTemplate template, CancellationToken ct = default);
    Task DeleteAsync(NotificationTemplate template, CancellationToken ct = default);
}
