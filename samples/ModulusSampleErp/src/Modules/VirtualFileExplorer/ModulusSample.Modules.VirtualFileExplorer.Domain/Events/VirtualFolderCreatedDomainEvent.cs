namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Events;

using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;

public sealed record VirtualFolderCreatedDomainEvent(
    VirtualFolderId FolderId,
    string Name,
    VirtualFolderId? ParentFolderId,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;