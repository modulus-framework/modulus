using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Roles;

internal sealed class DeleteRoleEndpoint : Endpoint<DeleteRoleEndpoint.DeleteRoleRequest>
{
    private readonly IMediator _mediator;

    public DeleteRoleEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/roles/{roleId:guid}");
        Tag(Tags.Roles);
        Summary("Delete role");
    }

    public override async Task HandleAsync(DeleteRoleRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new DeleteRoleCommand(req.RoleId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteRoleRequest
    {
        public Guid RoleId { get; set; }
    }
}
