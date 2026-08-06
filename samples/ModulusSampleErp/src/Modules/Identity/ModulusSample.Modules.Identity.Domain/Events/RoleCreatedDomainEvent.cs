using ModulusSample.Modules.Identity.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record RoleCreatedDomainEvent(
    RoleId RoleId,
    string RoleName) : Modulus.Core.Abstractions.Domain.DomainEventBase;
