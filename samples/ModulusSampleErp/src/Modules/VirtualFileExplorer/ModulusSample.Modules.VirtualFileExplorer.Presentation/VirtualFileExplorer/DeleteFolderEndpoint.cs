using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class DeleteFolderEndpoint : Endpoint<DeleteFolderEndpoint.DeleteFolderRequest>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public DeleteFolderEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Delete("/file-explorer/folders/{folderId}");
        Tag(Tags.VirtualFileExplorer);
        Summary("Delete an empty virtual folder");
        RequireAuthorization(); ;
    }

    public override async Task HandleAsync(DeleteFolderRequest req, CancellationToken ct)
    {
        var command = new DeleteFolderCommand(req.FolderId, _currentTenant.TenantId ?? Guid.Empty);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteFolderRequest
    {
        public Guid FolderId { get; set; }
    }
}
