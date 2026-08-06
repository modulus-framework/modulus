using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class GetFolderTreeEndpoint : EndpointWithoutRequest<IReadOnlyList<FolderTreeNodeResponse>>
{
    private readonly IMediator _mediator;

    public GetFolderTreeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/file-explorer/tree");
        Tag(Tags.VirtualFileExplorer);
        Summary("Get the full folder tree");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<FolderTreeNodeResponse>> result = await _mediator.QueryAsync(new GetFolderTreeQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}