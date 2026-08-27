using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Identity.Domain.Events;

[IntegrationEventName("Users.UserProfilePhotoChanged.v1")]
public sealed record UserProfilePhotoChangedEvent(
    Guid UserId,
    string? OldProfilePhotoStoragePath) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Users.UserProfilePhotoChanged.v1";
}
