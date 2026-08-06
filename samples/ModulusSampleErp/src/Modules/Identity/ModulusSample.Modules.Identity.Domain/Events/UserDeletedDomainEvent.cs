using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record UserDeletedDomainEvent(
    UserId UserId,
    string Reason,
    DateTime DeletedAtUtc,
    string? ProfilePhotoStoragePath) : Modulus.Core.Abstractions.Domain.DomainEventBase;
