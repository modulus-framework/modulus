namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Events;

using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;

public sealed record VirtualFolderDeletedDomainEvent(
    VirtualFolderId FolderId,
    string Name,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;