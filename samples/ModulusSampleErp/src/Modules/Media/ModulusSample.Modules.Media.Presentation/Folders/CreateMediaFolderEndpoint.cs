using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Shared.Domain;

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
        RequireAuthorization();
    }

    public override async Task HandleAsync(CreateMediaFolderRequest request, CancellationToken ct)
    {
        var result = await _mediator.SendAsync(
            new CreateMediaFolderCommand(request.Name, request.Description, request.ParentFolderId), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/media/folders/{result.Value.Id}", result.Value, ct);
    }

    public sealed record CreateMediaFolderRequest(string Name, string? Description, Guid? ParentFolderId);
}
