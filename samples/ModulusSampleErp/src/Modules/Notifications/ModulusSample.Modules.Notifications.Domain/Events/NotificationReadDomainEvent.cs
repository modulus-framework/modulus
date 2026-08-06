using ModulusSample.Modules.Notifications.Domain.ValueObjects;

namespace ModulusSample.Modules.Notifications.Domain.Events;

public sealed record NotificationReadDomainEvent(
    NotificationId NotificationId,
    Guid RecipientUserId,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;