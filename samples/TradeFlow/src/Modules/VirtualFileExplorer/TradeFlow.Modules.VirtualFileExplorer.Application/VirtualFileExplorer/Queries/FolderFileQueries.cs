using TradeFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;

public sealed record GetRootFoldersQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<FolderResponse>>>;

public sealed record GetFolderContentsQuery(
    Guid FolderId) : Modulus.Mediator.Abstractions.IQuery<Result<FolderContentsResponse>>;

public sealed record GetFolderTreeQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<FolderTreeNodeResponse>>>;

public sealed record GetFileByIdQuery(
    Guid FileId) : Modulus.Mediator.Abstractions.IQuery<Result<FileResponse>>;

public sealed record ListFolderFilesQuery(
    Guid FolderId,
    string? SearchTerm = null,
    int PageNumber = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<FileResponse>>>;
