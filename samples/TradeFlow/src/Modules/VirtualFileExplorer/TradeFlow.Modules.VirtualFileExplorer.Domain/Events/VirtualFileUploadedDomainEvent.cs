namespace TradeFlow.Modules.VirtualFileExplorer.Domain.Events;

using TradeFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

public sealed record VirtualFileUploadedDomainEvent(
    VirtualFileId FileId,
    string Name,
    VirtualFolderId FolderId,
    long SizeBytes,
    Guid TenantId) : Modulus.Core.Abstractions.Domain.DomainEventBase;
