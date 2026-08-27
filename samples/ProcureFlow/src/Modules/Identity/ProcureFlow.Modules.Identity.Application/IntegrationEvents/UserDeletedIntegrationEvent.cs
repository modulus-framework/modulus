using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record UserDeletedIntegrationEvent(Guid UserId, string Email)
    : IntegrationEventBase("Users.UserDeleted.v1")
{
    public Guid UserId { get; } = UserId;
    public string Email { get; } = Email;
}
