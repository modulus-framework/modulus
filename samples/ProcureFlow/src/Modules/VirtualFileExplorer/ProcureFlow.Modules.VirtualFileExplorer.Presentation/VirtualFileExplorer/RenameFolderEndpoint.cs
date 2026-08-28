using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class RenameFolderEndpoint : Endpoint<RenameFolderEndpoint.RenameFolderRequest, FolderResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public RenameFolderEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Put("/file-explorer/folders/{folderId}");
        Tag(Tags.VirtualFileExplorer);
        Summary("Rename a virtual folder");
    }

    public override async Task HandleAsync(RenameFolderRequest req, CancellationToken ct)
    {
        var command = new RenameFolderCommand(
            req.FolderId,
            req.Name,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<FolderResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class RenameFolderRequest
    {
        public Guid FolderId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
