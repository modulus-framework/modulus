namespace ModulusSample.Modules.Identity.Domain.Events;

/// <summary>
/// Domain event raised when an email verification token is used.
/// </summary>
public sealed record EmailVerificationTokenUsedDomainEvent(
    Guid TokenId,
    Guid UserId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
