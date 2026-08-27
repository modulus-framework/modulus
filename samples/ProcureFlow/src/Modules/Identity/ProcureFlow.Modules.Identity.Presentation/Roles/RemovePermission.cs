using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Permissions.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Roles;

internal sealed class RemovePermissionEndpoint : Endpoint<RemovePermissionEndpoint.RemovePermissionRequest>
{
    private readonly IMediator _mediator;

    public RemovePermissionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/roles/{roleId:guid}/permissions/{permission}");
        Tag(Tags.Roles);
        Summary("Remove permission from role");
    }

    public override async Task HandleAsync(RemovePermissionRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RemovePermissionCommand(req.RoleId, req.Permission), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class RemovePermissionRequest
    {
        public Guid RoleId { get; set; }
        public string Permission { get; set; } = string.Empty;
    }
}
