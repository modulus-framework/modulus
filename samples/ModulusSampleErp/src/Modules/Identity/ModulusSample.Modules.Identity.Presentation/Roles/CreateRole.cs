using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Commands;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Roles;

internal sealed class CreateRoleEndpoint : Endpoint<CreateRoleEndpoint.CreateRoleRequest, CreateRoleResponse>
{
    private readonly IMediator _mediator;

    public CreateRoleEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/roles");
        Tag(Tags.Roles);
        Summary("Create role");
    }

    public override async Task HandleAsync(CreateRoleRequest req, CancellationToken ct)
    {
        var command = new CreateRoleCommand(req.Name, req.Description);
        Result<CreateRoleResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/roles/{result.Value.RoleId}", ct);
    }

    internal sealed class CreateRoleRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
