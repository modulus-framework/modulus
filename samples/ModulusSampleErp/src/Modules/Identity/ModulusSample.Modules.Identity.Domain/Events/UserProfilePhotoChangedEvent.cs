namespace ModulusSample.Modules.Identity.Domain.Events;

public sealed record UserProfilePhotoChangedEvent(
    Guid UserId,
    string? OldProfilePhotoStoragePath) : Modulus.Core.Abstractions.Domain.DomainEventBase;
