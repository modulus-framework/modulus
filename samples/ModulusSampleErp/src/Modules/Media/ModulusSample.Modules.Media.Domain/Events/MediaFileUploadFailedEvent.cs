namespace ModulusSample.Modules.Media.Domain.Events;

public sealed record MediaFileUploadFailedEvent(
    Guid MediaFileId,
    string FileName,
    string Reason) : Modulus.Core.Abstractions.Domain.DomainEventBase;
