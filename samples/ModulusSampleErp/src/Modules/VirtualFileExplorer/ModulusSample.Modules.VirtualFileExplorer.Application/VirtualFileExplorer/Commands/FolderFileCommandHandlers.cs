using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Constants;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Repositories;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;

public sealed class CreateFolderCommandHandler(
    IVirtualFolderRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CreateFolderCommand, Result<FolderResponse>>
{
    public async Task<Result<FolderResponse>> HandleAsync(CreateFolderCommand request, CancellationToken ct)
    {
        VirtualFolderId? parentId = request.ParentFolderId.HasValue
            ? VirtualFolderId.From(request.ParentFolderId.Value)
            : null;

        if (parentId.HasValue)
        {
            var parent = await repository.GetByIdAsync(parentId.Value, request.TenantId, ct);
            if (parent is null)
            {
                return Result.Failure<FolderResponse>(VirtualFileExplorerErrors.FolderNotFound);
            }
        }

        var exists = await repository.ExistsAsync(request.Name, parentId, request.TenantId, ct);
        if (exists)
        {
            return Result.Failure<FolderResponse>(VirtualFileExplorerErrors.FolderAlreadyExists);
        }

        var folderResult = VirtualFolder.Create(
            VirtualFolderId.Create(),
            request.Name,
            parentId,
            request.TenantId,
            currentUser.UserId?.ToString());

        if (folderResult.IsFailure)
        {
            return Result.Failure<FolderResponse>(folderResult.Error);
        }

        await repository.AddAsync(folderResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(folderResult.Value));
    }

    private static FolderResponse ToResponse(VirtualFolder f) => new(
        f.Id.Value,
        f.Name,
        f.ParentFolderId?.Value,
        f.CreatedAt,
        f.CreatedBy,
        f.LastModifiedAt,
        f.LastModifiedBy);
}

public sealed class RenameFolderCommandHandler(
    IVirtualFolderRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RenameFolderCommand, Result<FolderResponse>>
{
    public async Task<Result<FolderResponse>> HandleAsync(RenameFolderCommand request, CancellationToken ct)
    {
        var folder = await repository.GetByIdAsync(
            VirtualFolderId.From(request.FolderId),
            request.TenantId,
            ct);

        if (folder is null)
        {
            return Result.Failure<FolderResponse>(VirtualFileExplorerErrors.FolderNotFound);
        }

        var renameResult = folder.Rename(request.Name, currentUser.UserId?.ToString() ?? "system");
        if (renameResult.IsFailure)
        {
            return Result.Failure<FolderResponse>(renameResult.Error);
        }

        await repository.UpdateAsync(folder, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new FolderResponse(
            folder.Id.Value,
            folder.Name,
            folder.ParentFolderId?.Value,
            folder.CreatedAt,
            folder.CreatedBy,
            folder.LastModifiedAt,
            folder.LastModifiedBy));
    }
}

public sealed class DeleteFolderCommandHandler(
    IVirtualFolderRepository folderRepository,
    IVirtualFileRepository fileRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<DeleteFolderCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteFolderCommand request, CancellationToken ct)
    {
        Guid tenantId = request.TenantId;

        var folder = await folderRepository.GetByIdAsync(
            VirtualFolderId.From(request.FolderId),
            tenantId,
            ct);

        if (folder is null)
        {
            return Result.Failure(VirtualFileExplorerErrors.FolderNotFound);
        }

        var subFolders = await folderRepository.GetByParentAsync(folder.Id, tenantId, ct);
        long fileCount = await fileRepository.CountInFolderAsync(folder.Id, tenantId, ct);

        if (subFolders.Count > 0 || fileCount > 0)
        {
            return Result.Failure(VirtualFileExplorerErrors.FolderNotEmpty);
        }

        folder.Delete(currentUser.UserId?.ToString() ?? "system");
        await folderRepository.DeleteAsync(folder, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}

public sealed class UploadFileCommandHandler(
    IVirtualFileRepository repository,
    IVirtualFolderRepository folderRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<UploadFileCommand, Result<FileResponse>>
{
    public async Task<Result<FileResponse>> HandleAsync(UploadFileCommand request, CancellationToken ct)
    {
        var folder = await folderRepository.GetByIdAsync(
            VirtualFolderId.From(request.FolderId),
            request.TenantId,
            ct);

        if (folder is null)
        {
            return Result.Failure<FileResponse>(VirtualFileExplorerErrors.FolderNotFound);
        }

        var exists = await repository.ExistsAsync(request.FileName, folder.Id, request.TenantId, ct);
        if (exists)
        {
            return Result.Failure<FileResponse>(VirtualFileExplorerErrors.FileAlreadyExists);
        }

        var fileResult = VirtualFile.Create(
            VirtualFileId.Create(),
            request.FileName,
            BuildStoragePath(request.TenantId, folder.Id, request.FileName),
            request.ContentType,
            request.SizeBytes,
            folder.Id,
            request.TenantId,
            currentUser.UserId?.ToString());

        if (fileResult.IsFailure)
        {
            return Result.Failure<FileResponse>(fileResult.Error);
        }

        await repository.AddAsync(fileResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(fileResult.Value));
    }

    private static string BuildStoragePath(Guid tenantId, VirtualFolderId folderId, string fileName)
    {
        return $"{tenantId}/{folderId.Value}/{fileName}";
    }

    private static FileResponse ToResponse(VirtualFile f) => new(
        f.Id.Value,
        f.Name,
        f.ContentType,
        f.SizeBytes,
        f.FolderId.Value,
        f.CreatedAt,
        f.CreatedBy,
        f.LastModifiedAt,
        f.LastModifiedBy);
}

public sealed class RenameFileCommandHandler(
    IVirtualFileRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<RenameFileCommand, Result<FileResponse>>
{
    public async Task<Result<FileResponse>> HandleAsync(RenameFileCommand request, CancellationToken ct)
    {
        Guid tenantId = request.TenantId;

        var file = await repository.GetByIdAsync(VirtualFileId.From(request.FileId), tenantId, ct);
        if (file is null)
        {
            return Result.Failure<FileResponse>(VirtualFileExplorerErrors.FileNotFound);
        }

        var renameResult = file.Rename(request.Name, currentUser.UserId?.ToString() ?? "system");
        if (renameResult.IsFailure)
        {
            return Result.Failure<FileResponse>(renameResult.Error);
        }

        await repository.UpdateAsync(file, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new FileResponse(
            file.Id.Value,
            file.Name,
            file.ContentType,
            file.SizeBytes,
            file.FolderId.Value,
            file.CreatedAt,
            file.CreatedBy,
            file.LastModifiedAt,
            file.LastModifiedBy));
    }
}

public sealed class DeleteFileCommandHandler(
    IVirtualFileRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<DeleteFileCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteFileCommand request, CancellationToken ct)
    {
        Guid tenantId = request.TenantId;

        var file = await repository.GetByIdAsync(VirtualFileId.From(request.FileId), tenantId, ct);
        if (file is null)
        {
            return Result.Failure(VirtualFileExplorerErrors.FileNotFound);
        }

        file.Delete(currentUser.UserId?.ToString() ?? "system");
        await repository.DeleteAsync(file, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
