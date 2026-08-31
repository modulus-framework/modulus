using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Events;

public sealed record RoleAssignedToUserDomainEvent(
    UserId UserId,
    RoleId RoleId,
    DateTime AssignedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
