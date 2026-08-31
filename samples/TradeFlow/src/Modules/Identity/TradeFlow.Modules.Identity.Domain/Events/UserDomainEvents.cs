using Modulus.Events.Abstractions;
using TradeFlow.Modules.Identity.Domain.Enums;

namespace TradeFlow.Modules.Identity.Domain.Events;

public sealed record UserActivatedDomainEvent(
    Guid UserId) : Modulus.Core.Abstractions.Domain.DomainEventBase;

/// <summary>
/// Published (via the transactional outbox) whenever a user is registered. The
/// integration name is "Users.UserRegistered.v1" — the contract downstream
/// consumers subscribe to.
/// </summary>
[IntegrationEventName("Users.UserRegistered.v1")]
public sealed record UserCreatedDomainEvent(
    Guid UserId,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    UserType UserType,
    DateTime CreatedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Users.UserRegistered.v1";
}

[IntegrationEventName("Users.UserEmailVerified.v1")]
public sealed record UserEmailVerifiedEvent(
    Guid UserId,
    string Email) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Users.UserEmailVerified.v1";
}

[IntegrationEventName("Users.UserProfileUpdated.v1")]
public sealed record UserProfileUpdatedDomainEvent(
    Guid UserId) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Users.UserProfileUpdated.v1";
}
