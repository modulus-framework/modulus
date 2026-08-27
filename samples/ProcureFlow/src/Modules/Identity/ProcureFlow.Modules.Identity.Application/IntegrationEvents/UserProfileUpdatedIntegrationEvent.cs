using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record UserProfileUpdatedIntegrationEvent(Guid UserId, string Email)
    : IntegrationEventBase("Users.UserProfileUpdated.v1")
{
    public Guid UserId { get; } = UserId;
    public string Email { get; } = Email;
}
