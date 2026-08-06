using ModulusSample.Modules.Identity.Domain.Enums;

namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record UserEmailVerifiedEvent(
    Guid UserId,
    string Email) : Modulus.Core.Abstractions.Domain.DomainEventBase;

public sealed record UserCreatedDomainEvent(
    Guid UserId,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    UserType UserType,
    DateTime CreatedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;

public sealed record UserActivatedDomainEvent(
    Guid UserId) : Modulus.Core.Abstractions.Domain.DomainEventBase;

public sealed record UserProfileUpdatedDomainEvent(
    Guid UserId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
