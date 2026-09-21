using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Registrations.Application.IntegrationEvents;

/// <summary>Published when a registration is confirmed (→ UserAccess activates the user).</summary>
public sealed record UserRegistrationConfirmedIntegrationEvent(
    Guid RegistrationId,
    string Login)
    : IntegrationEventBase("registrations.user-registration-confirmed.v1"), IDomainEvent;
