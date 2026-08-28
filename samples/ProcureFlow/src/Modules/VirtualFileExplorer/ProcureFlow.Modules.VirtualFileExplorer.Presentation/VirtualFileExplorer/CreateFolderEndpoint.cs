using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class CreateFolderEndpoint : Endpoint<CreateFolderEndpoint.CreateFolderRequest, FolderResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public CreateFolderEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Post("/file-explorer/folders");
        Tag(Tags.VirtualFileExplorer);
        Summary("Create a new virtual folder");
    }

    public override async Task HandleAsync(CreateFolderRequest req, CancellationToken ct)
    {
        var command = new CreateFolderCommand(
            req.Name,
            req.ParentFolderId,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<FolderResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/file-explorer/folders/{result.Value.FolderId}", ct);
    }

    internal sealed class CreateFolderRequest
    {
        public string Name { get; set; } = string.Empty;
        public Guid? ParentFolderId { get; set; }
    }
}
