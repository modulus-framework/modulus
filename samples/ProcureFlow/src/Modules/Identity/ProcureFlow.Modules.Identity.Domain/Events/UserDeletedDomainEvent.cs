using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Events;

[IntegrationEventName("Users.UserDeleted.v1")]
public sealed record UserDeletedDomainEvent(
    UserId UserId,
    string Reason,
    DateTime DeletedAtUtc,
    string? ProfilePhotoStoragePath) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Users.UserDeleted.v1";
}
