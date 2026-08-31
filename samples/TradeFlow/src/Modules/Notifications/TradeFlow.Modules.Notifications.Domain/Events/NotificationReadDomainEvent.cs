using Modulus.Events.Abstractions;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;

namespace TradeFlow.Modules.Notifications.Domain.Events;

[IntegrationEventName("Notifications.NotificationRead.v1")]
public sealed record NotificationReadDomainEvent(
    NotificationId NotificationId,
    Guid RecipientUserId,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Notifications.NotificationRead.v1";
}
