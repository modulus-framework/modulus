using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class GetRootFoldersEndpoint : EndpointWithoutRequest<IReadOnlyList<FolderResponse>>
{
    private readonly IMediator _mediator;

    public GetRootFoldersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/file-explorer/folders");
        Tag(Tags.VirtualFileExplorer);
        Summary("Get all root-level folders");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<IReadOnlyList<FolderResponse>> result = await _mediator.QueryAsync(new GetRootFoldersQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
