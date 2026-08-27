namespace ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;

public sealed record FolderResponse(
    Guid FolderId,
    string Name,
    Guid? ParentFolderId,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime LastModifiedAt,
    string? LastModifiedBy);

public sealed record FileResponse(
    Guid FileId,
    string Name,
    string? ContentType,
    long SizeBytes,
    Guid FolderId,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime LastModifiedAt,
    string? LastModifiedBy);

public sealed record FolderContentsResponse(
    Guid FolderId,
    string Name,
    Guid? ParentFolderId,
    IReadOnlyList<FolderResponse> SubFolders,
    IReadOnlyList<FileResponse> Files);

public sealed record FolderTreeNodeResponse(
    Guid FolderId,
    string Name,
    Guid? ParentFolderId,
    IReadOnlyList<FolderTreeNodeResponse> Children);
