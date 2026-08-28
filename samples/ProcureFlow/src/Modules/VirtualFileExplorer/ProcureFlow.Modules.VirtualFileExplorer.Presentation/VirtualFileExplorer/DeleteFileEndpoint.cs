using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class DeleteFileEndpoint : Endpoint<DeleteFileEndpoint.DeleteFileRequest>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public DeleteFileEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Delete("/file-explorer/files/{fileId}");
        Tag(Tags.VirtualFileExplorer);
        Summary("Delete a file from a virtual folder");
    }

    public override async Task HandleAsync(DeleteFileRequest req, CancellationToken ct)
    {
        var command = new DeleteFileCommand(req.FileId, _currentTenant.TenantId ?? Guid.Empty);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteFileRequest
    {
        public Guid FileId { get; set; }
    }
}
