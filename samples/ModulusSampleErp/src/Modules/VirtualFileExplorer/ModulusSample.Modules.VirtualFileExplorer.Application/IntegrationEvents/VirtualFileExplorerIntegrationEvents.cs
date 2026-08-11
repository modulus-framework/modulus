using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.VirtualFileExplorer.Application.IntegrationEvents;

public sealed record VirtualFolderCreatedIntegrationEvent(
    Guid FolderId,
    string Name,
    Guid? ParentFolderId,
    Guid TenantId) : IntegrationEventBase("VirtualFileExplorer.VirtualFolderCreated.v1");

public sealed record VirtualFolderDeletedIntegrationEvent(
    Guid FolderId,
    string Name,
    Guid TenantId) : IntegrationEventBase("VirtualFileExplorer.VirtualFolderDeleted.v1");

public sealed record VirtualFileUploadedIntegrationEvent(
    Guid FileId,
    string Name,
    Guid FolderId,
    long SizeBytes,
    Guid TenantId) : IntegrationEventBase("VirtualFileExplorer.VirtualFileUploaded.v1");

public sealed record VirtualFileDeletedIntegrationEvent(
    Guid FileId,
    string Name,
    Guid FolderId,
    Guid TenantId) : IntegrationEventBase("VirtualFileExplorer.VirtualFileDeleted.v1");
