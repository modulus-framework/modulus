using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Events;

public sealed record RoleRemovedFromUserDomainEvent(
    UserId UserId,
    RoleId RoleId,
    DateTime RemovedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
