using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Dtos;
using ModulusSample.Modules.VirtualFileExplorer.Application.VirtualFileExplorer.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Presentation.VirtualFileExplorer;

internal sealed class GetFileByIdEndpoint : Endpoint<GetFileByIdEndpoint.GetFileByIdRequest, FileResponse>
{
    private readonly IMediator _mediator;

    public GetFileByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/file-explorer/files/{fileId}");
        Tag(Tags.VirtualFileExplorer);
        Summary("Get a file's metadata");
    }

    public override async Task HandleAsync(GetFileByIdRequest req, CancellationToken ct)
    {
        Result<FileResponse> result = await _mediator.QueryAsync(new GetFileByIdQuery(req.FileId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetFileByIdRequest
    {
        public Guid FileId { get; set; }
    }
}