using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Permissions.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Roles;

internal sealed class AddPermissionEndpoint : Endpoint<AddPermissionEndpoint.AddPermissionRequest>
{
    private readonly IMediator _mediator;

    public AddPermissionEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/roles/{roleId:guid}/permissions");
        Tag(Tags.Roles);
        Summary("Add permission to role");
    }

    public override async Task HandleAsync(AddPermissionRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new AddPermissionCommand(req.RoleId, req.Permission), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class AddPermissionRequest
    {
        public Guid RoleId { get; set; }
        public string Permission { get; set; } = string.Empty;
    }
}
