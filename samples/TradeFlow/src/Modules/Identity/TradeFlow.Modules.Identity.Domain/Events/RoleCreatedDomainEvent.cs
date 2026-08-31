using TradeFlow.Modules.Identity.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Events;

public sealed record RoleCreatedDomainEvent(
    RoleId RoleId,
    string RoleName) : Modulus.Core.Abstractions.Domain.DomainEventBase;
