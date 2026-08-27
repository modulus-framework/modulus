using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Constants;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Repositories;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;

public sealed class GetRootFoldersHandler(
    IVirtualFolderRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetRootFoldersQuery, Result<IReadOnlyList<FolderResponse>>>
{
    public async Task<Result<IReadOnlyList<FolderResponse>>> HandleAsync(GetRootFoldersQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<VirtualFolder> folders = await repository.GetByParentAsync(null, tenantId, ct);
        IReadOnlyList<FolderResponse> responses = folders.Select(ToResponse).ToList();
        return Result.Success(responses);
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

public sealed class GetFolderContentsHandler(
    IVirtualFolderRepository folderRepository,
    IVirtualFileRepository fileRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetFolderContentsQuery, Result<FolderContentsResponse>>
{
    public async Task<Result<FolderContentsResponse>> HandleAsync(GetFolderContentsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        VirtualFolderId folderId = VirtualFolderId.From(request.FolderId);

        var folder = await folderRepository.GetByIdAsync(folderId, tenantId, ct);
        if (folder is null)
        {
            return Result.Failure<FolderContentsResponse>(VirtualFileExplorerErrors.FolderNotFound);
        }

        IReadOnlyList<VirtualFolder> subFolders = await folderRepository.GetByParentAsync(folderId, tenantId, ct);
        IReadOnlyList<VirtualFile> files = await fileRepository.GetByFolderAsync(folderId, tenantId, ct);

        return Result.Success(new FolderContentsResponse(
            folder.Id.Value,
            folder.Name,
            folder.ParentFolderId?.Value,
            subFolders.Select(ToFolderResponse).ToList(),
            files.Select(ToFileResponse).ToList()));
    }

    private static FolderResponse ToFolderResponse(VirtualFolder f) => new(
        f.Id.Value,
        f.Name,
        f.ParentFolderId?.Value,
        f.CreatedAt,
        f.CreatedBy,
        f.LastModifiedAt,
        f.LastModifiedBy);

    private static FileResponse ToFileResponse(VirtualFile f) => new(
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

public sealed class GetFolderTreeHandler(
    IVirtualFolderRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetFolderTreeQuery, Result<IReadOnlyList<FolderTreeNodeResponse>>>
{
    public async Task<Result<IReadOnlyList<FolderTreeNodeResponse>>> HandleAsync(GetFolderTreeQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<VirtualFolder> all = await repository.GetAllAsync(tenantId, ct);

        IReadOnlyList<FolderTreeNodeResponse> roots = BuildChildren(all, null);
        return Result.Success(roots);
    }

    private static IReadOnlyList<FolderTreeNodeResponse> BuildChildren(
        IReadOnlyList<VirtualFolder> all,
        VirtualFolderId? parentId)
    {
        return all
            .Where(f => f.ParentFolderId == parentId)
            .Select(f => new FolderTreeNodeResponse(
                f.Id.Value,
                f.Name,
                f.ParentFolderId?.Value,
                BuildChildren(all, f.Id)))
            .ToList();
    }
}

public sealed class GetFileByIdHandler(
    IVirtualFileRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetFileByIdQuery, Result<FileResponse>>
{
    public async Task<Result<FileResponse>> HandleAsync(GetFileByIdQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        var file = await repository.GetByIdAsync(VirtualFileId.From(request.FileId), tenantId, ct);
        if (file is null)
        {
            return Result.Failure<FileResponse>(VirtualFileExplorerErrors.FileNotFound);
        }

        return Result.Success(ToResponse(file));
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

public sealed class ListFolderFilesHandler(
    IVirtualFileRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListFolderFilesQuery, Result<PagedResult<FileResponse>>>
{
    public async Task<Result<PagedResult<FileResponse>>> HandleAsync(ListFolderFilesQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        VirtualFolderId folderId = VirtualFolderId.From(request.FolderId);

        var paged = await repository.GetPagedAsync(
            folderId,
            tenantId,
            request.SearchTerm,
            request.PageNumber,
            request.PageSize,
            ct);

        var responses = paged.Items.Select(ToResponse).ToList();

        return Result.Success(new PagedResult<FileResponse>(
            responses,
            paged.TotalCount,
            request.PageNumber,
            request.PageSize));
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
