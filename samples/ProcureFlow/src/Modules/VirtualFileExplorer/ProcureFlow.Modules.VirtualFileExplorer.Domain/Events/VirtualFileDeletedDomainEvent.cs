namespace ProcureFlow.Modules.VirtualFileExplorer.Domain.Events;

using ProcureFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

public sealed record VirtualFileDeletedDomainEvent(
    VirtualFileId FileId,
    string Name,
    VirtualFolderId FolderId,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
