namespace TradeFlow.Modules.VirtualFileExplorer.Domain.Events;

using TradeFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

public sealed record VirtualFileDeletedDomainEvent(
    VirtualFileId FileId,
    string Name,
    VirtualFolderId FolderId,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
