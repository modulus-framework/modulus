using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Events;

public sealed record RoleRemovedFromUserDomainEvent(
    UserId UserId,
    RoleId RoleId,
    DateTime RemovedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
