using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Domain.Events;

public sealed record UserSuspendedDomainEvent(
    UserId UserId,
    string Reason,
    DateTime SuspendedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
