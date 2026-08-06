using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record UserRegisteredIntegrationEvent(Guid UserId, string Email, string UserType)
    : IntegrationEventBase("Users.UserRegistered.v1")
{
    public Guid UserId { get; } = UserId;
    public string Email { get; } = Email;
    public string UserType { get; } = UserType;
}
