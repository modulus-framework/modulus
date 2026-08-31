using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Identity.Application.Roles.Commands;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Presentation.Admin;

internal sealed class RemoveRoleEndpoint : Endpoint<RemoveRoleEndpoint.RemoveRoleRequest>
{
    private readonly IMediator _mediator;

    public RemoveRoleEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/admin/users/{userId:guid}/roles/{roleId:guid}");
        Tag(Tags.AdminUsers);
        Summary("Remove role from user");
    }

    public override async Task HandleAsync(RemoveRoleRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RemoveRoleCommand(req.UserId, req.RoleId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class RemoveRoleRequest
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
    }
}
