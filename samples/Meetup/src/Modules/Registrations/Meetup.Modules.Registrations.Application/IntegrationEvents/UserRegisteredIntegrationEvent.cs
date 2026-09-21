using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Registrations.Application.IntegrationEvents;

/// <summary>Published when a new user registers (→ UserAccess creates the user).</summary>
public sealed record UserRegisteredIntegrationEvent(
    Guid RegistrationId,
    string Login,
    string Email)
    : IntegrationEventBase("registrations.user-registered.v1"), IDomainEvent;
