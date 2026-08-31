using Modulus.Events.Abstractions;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;

namespace TradeFlow.Modules.Notifications.Domain.Events;

[IntegrationEventName("Notifications.NotificationCreated.v1")]
public sealed record NotificationCreatedDomainEvent(
    NotificationId NotificationId,
    Guid RecipientUserId,
    string Title,
    string Message,
    NotificationType Type,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Notifications.NotificationCreated.v1";
}
