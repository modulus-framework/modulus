using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record UserProfilePhotoChangedIntegrationEvent(Guid UserId, string? PhotoUrl)
    : IntegrationEventBase("Users.UserProfilePhotoChanged.v1")
{
    public Guid UserId { get; } = UserId;
    public string? PhotoUrl { get; } = PhotoUrl;
}
