using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class RenameFileEndpoint : Endpoint<RenameFileEndpoint.RenameFileRequest, FileResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public RenameFileEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Put("/file-explorer/files/{fileId}");
        Tag(Tags.VirtualFileExplorer);
        Summary("Rename a file in a virtual folder");
        RequireAuthorization(); ;
    }

    public override async Task HandleAsync(RenameFileRequest req, CancellationToken ct)
    {
        var command = new RenameFileCommand(
            req.FileId,
            req.Name,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<FileResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class RenameFileRequest
    {
        public Guid FileId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
