using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record UserSuspendedDomainEvent(
    UserId UserId,
    string Reason,
    DateTime SuspendedAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
