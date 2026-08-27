using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.IntegrationEvents;

public sealed record EmailVerificationResentIntegrationEvent(Guid UserId, string Email)
    : IntegrationEventBase("Users.EmailVerificationResent.v1")
{
    public Guid UserId { get; } = UserId;
    public string Email { get; } = Email;
}
