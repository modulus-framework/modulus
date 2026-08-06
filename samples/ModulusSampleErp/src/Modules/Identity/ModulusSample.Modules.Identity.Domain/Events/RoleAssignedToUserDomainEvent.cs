using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record RoleAssignedToUserDomainEvent(
    UserId UserId,
    RoleId RoleId,
    DateTime AssignedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
