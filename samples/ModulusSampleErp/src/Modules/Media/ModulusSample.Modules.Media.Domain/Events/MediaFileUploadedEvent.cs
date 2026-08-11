namespace ModulusSample.Modules.Media.Domain.Events;

public sealed record MediaFileUploadedEvent(
    Guid MediaFileId,
    string FileName,
    string StoragePath) : Modulus.Core.Abstractions.Domain.DomainEventBase;
