using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Commands;
using TradeFlow.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class UploadFileEndpoint : Endpoint<UploadFileEndpoint.UploadFileRequest, FileResponse>
{
    private readonly IMediator _mediator;
    private readonly ICurrentTenant _currentTenant;

    public UploadFileEndpoint(IMediator mediator, ICurrentTenant currentTenant)
    {
        _mediator = mediator;
        _currentTenant = currentTenant;
    }

    public override void Configure()
    {
        Post("/file-explorer/folders/{folderId}/files");
        Tag(Tags.VirtualFileExplorer);
        Summary("Register a file in a virtual folder");
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
    {
        var command = new UploadFileCommand(
            req.FolderId,
            req.FileName,
            req.ContentType,
            req.SizeBytes,
            _currentTenant.TenantId ?? Guid.Empty);

        Result<FileResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/file-explorer/files/{result.Value.FileId}", ct);
    }

    internal sealed class UploadFileRequest
    {
        public Guid FolderId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long SizeBytes { get; set; }
    }
}
