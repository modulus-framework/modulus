using ProcureFlow.Modules.Identity.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Events;

public sealed record RoleCreatedDomainEvent(
    RoleId RoleId,
    string RoleName) : Modulus.Core.Abstractions.Domain.DomainEventBase;
