namespace TradeFlow.Modules.VirtualFileExplorer.Domain.Events;

using TradeFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

public sealed record VirtualFolderDeletedDomainEvent(
    VirtualFolderId FolderId,
    string Name,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
