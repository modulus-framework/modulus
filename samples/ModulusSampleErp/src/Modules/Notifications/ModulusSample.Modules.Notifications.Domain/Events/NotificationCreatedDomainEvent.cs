using ModulusSample.Modules.Notifications.Domain.ValueObjects;

namespace ModulusSample.Modules.Notifications.Domain.Events;

public sealed record NotificationCreatedDomainEvent(
    NotificationId NotificationId,
    Guid RecipientUserId,
    string Title,
    string Message,
    NotificationType Type,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
