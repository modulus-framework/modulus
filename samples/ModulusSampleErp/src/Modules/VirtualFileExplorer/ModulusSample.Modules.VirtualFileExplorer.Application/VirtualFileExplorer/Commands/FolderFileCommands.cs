using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;

public sealed record CreateFolderCommand(
    string Name,
    Guid? ParentFolderId,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<FolderResponse>>;

public sealed record RenameFolderCommand(
    Guid FolderId,
    string Name,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<FolderResponse>>;

public sealed record DeleteFolderCommand(
    Guid FolderId,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result>;

public sealed record UploadFileCommand(
    Guid FolderId,
    string FileName,
    string? ContentType,
    long SizeBytes,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<FileResponse>>;

public sealed record RenameFileCommand(
    Guid FileId,
    string Name,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<FileResponse>>;

public sealed record DeleteFileCommand(
    Guid FileId,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result>;