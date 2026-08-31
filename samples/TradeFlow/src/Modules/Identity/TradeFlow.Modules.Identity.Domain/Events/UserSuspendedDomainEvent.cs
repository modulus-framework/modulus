using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Events;

public sealed record UserSuspendedDomainEvent(
    UserId UserId,
    string Reason,
    DateTime SuspendedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
