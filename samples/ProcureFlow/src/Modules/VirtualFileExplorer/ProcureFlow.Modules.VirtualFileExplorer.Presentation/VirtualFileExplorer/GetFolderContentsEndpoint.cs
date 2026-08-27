using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class GetFolderContentsEndpoint : Endpoint<GetFolderContentsEndpoint.GetFolderContentsRequest, FolderContentsResponse>
{
    private readonly IMediator _mediator;

    public GetFolderContentsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/file-explorer/folders/{folderId}");
        Tag(Tags.VirtualFileExplorer);
        Summary("Get a folder's sub-folders and files");
    }

    public override async Task HandleAsync(GetFolderContentsRequest req, CancellationToken ct)
    {
        Result<FolderContentsResponse> result = await _mediator.QueryAsync(new GetFolderContentsQuery(req.FolderId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetFolderContentsRequest
    {
        public Guid FolderId { get; set; }
    }
}
