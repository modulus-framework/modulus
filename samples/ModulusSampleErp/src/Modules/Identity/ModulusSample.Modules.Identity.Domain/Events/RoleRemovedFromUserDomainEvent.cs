using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record RoleRemovedFromUserDomainEvent(
    UserId UserId,
    RoleId RoleId,
    DateTime RemovedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
