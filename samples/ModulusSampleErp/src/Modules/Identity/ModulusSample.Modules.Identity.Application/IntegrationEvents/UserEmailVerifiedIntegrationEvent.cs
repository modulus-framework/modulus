using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record UserEmailVerifiedIntegrationEvent(Guid UserId, string Email)
    : IntegrationEventBase("Users.UserEmailVerified.v1")
{
    public Guid UserId { get; } = UserId;
    public string Email { get; } = Email;
}
