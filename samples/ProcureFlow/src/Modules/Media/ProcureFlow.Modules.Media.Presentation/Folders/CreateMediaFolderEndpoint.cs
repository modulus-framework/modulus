using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Folders.Commands;
using ModulusSample.Modules.Media.Application.Folders.Dtos;

namespace ModulusSample.Modules.Media.Presentation.Folders;

internal sealed class CreateMediaFolderEndpoint : Endpoint<CreateMediaFolderEndpoint.CreateMediaFolderRequest, MediaFolderDto>
{
    private readonly IMediator _mediator;

    public CreateMediaFolderEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/media/folders");
        Tag("Media");
        Summary("Create a new media folder");
    }

    public override async Task HandleAsync(CreateMediaFolderRequest request, CancellationToken ct)
    {
        MediaFolderDto result = await _mediator.SendAsync(
            new CreateMediaFolderCommand(request.Name, request.Description, request.ParentFolderId), ct);

        await SendCreatedAsync(result, $"/api/media/folders/{result.Id}", ct);
    }

    public sealed record CreateMediaFolderRequest(string Name, string? Description, Guid? ParentFolderId)
    {
        public CreateMediaFolderRequest() : this(string.Empty, null, null)
        {
        }
    }
}
