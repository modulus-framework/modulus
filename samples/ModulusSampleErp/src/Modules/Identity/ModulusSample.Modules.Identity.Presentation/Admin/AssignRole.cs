using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Admin;

internal sealed class AssignRoleEndpoint : Endpoint<AssignRoleEndpoint.AssignRoleRequest>
{
    private readonly IMediator _mediator;

    public AssignRoleEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/admin/users/{userId:guid}/roles/{roleId:guid}");
        Tag(Tags.AdminUsers);
        Summary("Assign role to user");
    }

    public override async Task HandleAsync(AssignRoleRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new AssignRoleCommand(req.UserId, req.RoleId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class AssignRoleRequest
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
    }
}
