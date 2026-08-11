using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class ListFolderFilesEndpoint : Endpoint<ListFolderFilesEndpoint.ListFolderFilesRequest, PagedResult<FileResponse>>
{
    private readonly IMediator _mediator;

    public ListFolderFilesEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/file-explorer/folders/{folderId}/files");
        Tag(Tags.VirtualFileExplorer);
        Summary("List files in a folder with optional search and paging");
        RequireAuthorization(); ;
    }

    public override async Task HandleAsync(ListFolderFilesRequest req, CancellationToken ct)
    {
        var query = new ListFolderFilesQuery(req.FolderId, req.SearchTerm, req.PageNumber, req.PageSize);
        Result<PagedResult<FileResponse>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ListFolderFilesRequest
    {
        public Guid FolderId { get; set; }
        public string? SearchTerm { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
