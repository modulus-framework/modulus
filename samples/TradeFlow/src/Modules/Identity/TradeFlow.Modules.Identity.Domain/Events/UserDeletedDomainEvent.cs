using Modulus.Events.Abstractions;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Events;

[IntegrationEventName("Users.UserDeleted.v1")]
public sealed record UserDeletedDomainEvent(
    UserId UserId,
    string Reason,
    DateTime DeletedAtUtc,
    string? ProfilePhotoStoragePath) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Users.UserDeleted.v1";
}
