using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Roles.Commands;
using ProcureFlow.Modules.Identity.Application.Roles.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Roles;

internal sealed class UpdateRoleEndpoint : Endpoint<UpdateRoleEndpoint.UpdateRoleRequest, RoleDetailResponse>
{
    private readonly IMediator _mediator;

    public UpdateRoleEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/roles/{roleId:guid}");
        Tag(Tags.Roles);
        Summary("Update role");
    }

    public override async Task HandleAsync(UpdateRoleRequest req, CancellationToken ct)
    {
        var command = new UpdateRoleCommand(req.RoleId, req.Name, req.Description);
        Result<RoleDetailResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class UpdateRoleRequest
    {
        public Guid RoleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
