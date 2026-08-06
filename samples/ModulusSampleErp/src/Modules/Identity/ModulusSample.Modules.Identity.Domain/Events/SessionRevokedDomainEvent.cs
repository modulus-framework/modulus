namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record SessionRevokedDomainEvent(
    Guid SessionId,
    Guid UserId,
    string Reason,
    DateTime RevokedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
